using SkillSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem 
{

    public class SkillManager : MonoBehaviour
    {
        public static SkillManager Instance { get; private set; }

        public SkillLibrary library;
        private List<SkillInstance> activeSkills = new List<SkillInstance>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            InterruptAllSkills();   // 角色销毁时清理所有运行中技能
            if (Instance == this) Instance = null;
        }
        public void PlaySkill(string id, Transform caster, Vector3 targetPos, Transform target = null)
        {
            if (library == null)
            {
                Debug.LogError("没有查找到library");
                return;
            }
            SkillConfig config = library.Get(id);
            if (config == null)
            {
                Debug.LogWarning("查找不到组件");
                return;
            }

            // 同 ID 唯一：重复释放先打断旧实例（重放即重开），防止实例叠加、日志刷屏
            InterruptSkill(id);

            SkillContext ctx = new SkillContext(caster, targetPos, target,id);
            SkillInstance instance = new SkillInstance(config,ctx);
            ctx.onFinish = instance.RequestFinish;
            activeSkills.Add(instance);
            instance.state = SkillLifecycleState.Running;
            foreach (var comp in config.components)
            {
                if (comp == null) continue;
                if (instance.state != SkillLifecycleState.Running) break;  // OnStart 期间被打断，不再启动剩余组件
                comp.OnStart(ctx);
            }
            Debug.Log(instance.state == SkillLifecycleState.Running
                ? $"[Skill] {id}: Released → Running"
                : $"[Skill] {id}: Released → Interrupted（OnStart 期间被打断）");
        }
        public void InterruptSkill(string id)
        {
            for (int i = activeSkills.Count - 1; i >= 0; i--)
            {
                var inst = activeSkills[i];
                if (inst.config.skillId == id && inst.state == SkillLifecycleState.Running)
                {
                    inst.RequestInterrupt();
                    ProcessInterrupt(inst);
                    activeSkills.RemoveAt(i);
                    Debug.Log($"[Skill]{id}:Running->Interrupted");

                }
            }
        }
        public void InterruptAllSkills()
        {
            for(int i= activeSkills.Count -1; i >= 0; i--)
            {
                var inst= activeSkills[i];
                if(inst.state == SkillLifecycleState.Running)
                {
                    inst.RequestInterrupt();
                    ProcessInterrupt(inst);
                    activeSkills.RemoveAt(i);
                    Debug.Log($"[Skill]{inst.config.skillId}:Running->Interrupted");
                }
            }
        }
        // ─── 反馈服务（供 HitFeedbackComponent 等组件委托调用）───

        /// <summary>顿帧：画面全局暂停 duration 现实秒，timeScale 如 0.1 = 10% 速度</summary>
        public void RunHitStop(float duration, float timeScale)
        {
            StartCoroutine(HitStopRoutine(duration, timeScale));
        }

        private IEnumerator HitStopRoutine(float duration, float timeScale)
        {
            float normal = Time.timeScale;
            Time.timeScale = timeScale;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = normal;
        }

        /// <summary>参数化击退：duration 秒内把 target 沿 dir 平滑位移 distance（Root Motion 环境下用 MovePosition 稳定）</summary>
        public void ApplyKnockback(Transform target, Vector3 dir, float distance, float duration)
        {
            StartCoroutine(KnockbackRoutine(target, dir, distance, duration));
        }

        private IEnumerator KnockbackRoutine(Transform target, Vector3 dir, float distance, float duration)
        {
            Rigidbody rb = target != null ? target.GetComponent<Rigidbody>() : null;
            if (rb == null || dir.sqrMagnitude < 0.001f) yield break;

            Vector3 start = rb.position;
            Vector3 end = start + dir.normalized * distance;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                rb.MovePosition(Vector3.Lerp(start, end, Mathf.Min(1f, t / duration)));
                yield return null;
            }
        }

        private void Update()
        {

            for (int i = activeSkills.Count - 1; i >= 0; i--)
            {
                SkillInstance inst = activeSkills[i];

                if (inst.state == SkillLifecycleState.Ended)
                {
                    ProcessEnd(inst);
                    activeSkills.RemoveAt(i);
                    Debug.Log($"[Skill]{inst.config.skillId}:Running->Ended");
                    continue;
                }

                if (inst.state != SkillLifecycleState.Running) continue;

                foreach (var comp in inst.config.components)
                {
                    if (comp == null) continue;
                    if (inst.state != SkillLifecycleState.Running) break;  // 同帧门控（加固点 1）
                    comp.OnTick(inst.context);
                }

                if (inst.state == SkillLifecycleState.Ended)
                {
                    ProcessEnd(inst);
                    activeSkills.RemoveAt(i);
                    Debug.Log($"[Skill]{inst.config.skillId}:Running->Ended");
                }
            }
        }

        private void ProcessEnd(SkillInstance inst)
        {
            foreach(var comp in inst.config.components)
            {
                if(comp!=null)
                {
                    comp.OnEnd(inst.context);
                }
            }
            inst.state=SkillLifecycleState.Ended;
        }


        private void ProcessInterrupt(SkillInstance inst)
        {
            foreach (var comp in inst.config.components)
            {
                if (comp != null)
                    comp.OnInterrupt(inst.context);
            }
            inst.state = SkillLifecycleState.Interrupted;
        }
    }
}




    

