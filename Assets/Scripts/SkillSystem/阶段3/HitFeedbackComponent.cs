using System;
using UnityEngine;

namespace SkillSystem
{
    // 击打反馈组件：命中时执行 音效 + 顿帧。
    // 击退说明：Enemy.TakeDamage 内部已用 AddForce 处理击退，这里不再重复施力；
    // knockbackDistance 保留为配置字段，供未来对非 Enemy 目标（可破坏物等）使用。
    [Serializable]
    public class HitFeedbackComponent : SkillComponent
    {
        [Header("命中音效")]
        public string soundIdPrefix = "hit_";

        [Header("顿帧")]
        public float hitStopDuration = 0.05f;
        [Range(0.01f, 0.5f)] public float hitStopTimeScale = 0.1f;

        [Header("击退（备用，见类注释）")]
        public float knockbackDistance = 0.5f;

        public override void OnStart(SkillContext ctx) { }
        public override void OnTick(SkillContext ctx) { }
        public override void OnEnd(SkillContext ctx) { }
        public override void OnInterrupt(SkillContext ctx) { }

        // 供 HitWindowComponent 在每次命中时调用
        public void Execute(SkillContext ctx, Transform hitTarget)
        {
            if (hitTarget == null || ctx == null) return;

            // 1. 命中音效（前缀随机播放，与项目攻击音效同一套机制）
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlayByPrefix(soundIdPrefix, hitTarget.position);

            // 2. 顿帧：委托 SkillManager（组件资产是共享的、没有协程，持续型行为必须委托 MonoBehaviour）
            if (SkillManager.Instance != null)
                SkillManager.Instance.RunHitStop(hitStopDuration, hitStopTimeScale);
        }
    }
}
