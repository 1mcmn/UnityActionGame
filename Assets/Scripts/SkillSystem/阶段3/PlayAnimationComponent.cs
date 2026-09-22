using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    // 帧音效事件：在指定整数帧触发音效。
    // 注意：volume/loop 字段暂未生效——现有 SoundManager 只有 Play(id, pos, pitch)，
    // 需要扩展 SoundManager 重载后才能支持。
    [Serializable]
    public class SoundFrameEvent
    {
        public int frame;                   // 触发帧号（整数，30fps 基准）
        public string soundId;              // 音效 ID，对应 SoundManager
        [Range(0f, 1f)] public float volume = 1f;
        public bool loop = false;
    }

    // 动画播放组件：播放动画、驱动帧事件、报告结束。
    // ⚠️ 顺序要求：本组件必须排在 HitWindow / InvincibleWindow 之前（它们读取本组件写入的 CurrentFrame）。
    [Serializable]
    public class PlayAnimationComponent : SkillComponent
    {
        [Header("动画设置")]
        public string stateName = "Attack";   // 动画机中的状态名
        public float crossFade = 0.1f;        // 过渡时间（秒）；0.02~0.05 = 瞬间切换
        [Tooltip("技能总时长（秒）。填 <=0 表示【不自动结束】：一直播到被 InterruptSkill " +
                 "或 SkillManager.SwitchSkill 打断（架势/保持类技能用）。")]
        public float duration = 1f;           // 后备时长（拿不到 clip 长度时使用）

        [Header("帧事件列表")]
        public List<SoundFrameEvent> frameEvents = new List<SoundFrameEvent>();

        [Header("动画覆盖（可选，方案B：动画 clip 映射）")]
        [Tooltip("留空 = 播 stateName 状态原本的动画。填了 = 运行时用 AnimatorOverrideController " +
                 "把该状态的动画换成这个 clip（策划换动画不用碰 Animator）。")]
        public AnimationClip overrideClip;

        [Tooltip("stateName 状态在【基础 controller】里原本绑定的 clip。" +
                 "由技能编辑器自动解析后写进资产，运行时用它做覆盖映射。一般不用手改。")]
        public AnimationClip baseClip;

        [Header("结束后交还动画控制权")]
        [Tooltip("技能结束或被打断时，建议角色回到哪个动画状态（如 Idle / Locomotion）。\n" +
                 "留空 = 由角色自己决定（推荐）—— 角色会按当前移动速度选择待机或移动混合树。\n" +
                 "不填也能正常工作，填了则强制回到这个状态名。")]
        public string returnStateName = "";

        // 运行时状态（每个技能实例独立，存 ctx 而不存组件本身——组件资产被多技能共享，不能有实例字段）
        private class AnimationState
        {
            public Animator animator;
            public AnimationClip clip;          // 过渡完成后捕获的真实 clip（帧率从它读）
            public float clipLength;
            public int lastFrame = -1;
            public bool lengthCaptured = false;
            public bool hasEnded = false;
        }

        public override void OnStart(SkillContext ctx)
        {
            var animator = ctx.caster.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("PlayAnimation: caster 没有 Animator 组件");
                return;
            }

            ApplyAnimationOverride(animator);   // 配了 overrideClip 才动 controller

            animator.CrossFadeInFixedTime(stateName, crossFade);

            var state = new AnimationState
            {
                animator = animator,
                clipLength = duration   // 先用后备值，过渡完成后取真实值
            };
            ctx.runtimeData["PlayAnimation"] = state;
        }

        public override void OnTick(SkillContext ctx)
        {
            if (!ctx.runtimeData.TryGetValue("PlayAnimation", out var v)) return;
            var state = (AnimationState)v;

            var stateInfo = state.animator.GetCurrentAnimatorStateInfo(0);
            float elapsed = Time.time - ctx.startTime;

            // 过渡完成后取一次真实 clip 长度。
            // 注意：不能用 IsName(stateName) 判断——stateName 可能是子状态机名（如 "Attack"），
            // 实际状态是子状态机里的子状态，IsName 永远匹配不上（这会让技能永远无法自然结束）。
            if (!state.lengthCaptured && elapsed >= crossFade)
            {
                var clipInfo = state.animator.GetCurrentAnimatorClipInfo(0);
                if (clipInfo.Length > 0 && clipInfo[0].clip != null)
                {
                    state.clip = clipInfo[0].clip;
                    state.clipLength = state.clip.length;
                    state.lengthCaptured = true;   // 只有真拿到才算捕获成功；拿不到下一帧再试
                }
            }

            float rawNormalized = stateInfo.normalizedTime;  // 原始值：非循环动画播完会停在 1.0
            float loopedNormalized = rawNormalized % 1f;     // 循环取余，只用于算帧号

            // 帧率统一由 SkillFrameUtil 提供（取 clip.frameRate；clip 还没捕获到就用兜底 30fps）
            int currentFrame = SkillFrameUtil.TimeToFrame(state.clip, loopedNormalized * state.clipLength);
            ctx.runtimeData["CurrentFrame"] = currentFrame;

            // 帧跨越检测：防止重复触发和跳帧漏触发
            if (currentFrame != state.lastFrame)
            {
                foreach (var evt in frameEvents)
                {
                    if (evt != null && evt.frame > state.lastFrame && evt.frame <= currentFrame)
                        TriggerSoundEvent(ctx, evt);
                }
                state.lastFrame = currentFrame;
            }

            // 结束判定（v3）：
            //   duration > 0  → 按时间结束（数据驱动，界面里可调）；动画自身播到 1.0 也提前结束
            //   duration <= 0 → 【不自动结束】：一直播到被 InterruptSkill / SwitchSkill 接管
            //                   （架势、保持姿势这类"由玩家/敌人决定多久"的技能）
            bool timeUp = duration > 0f && elapsed >= duration;
            bool clipEnd = duration > 0f && !stateInfo.loop && rawNormalized >= 1f;
            if (!state.hasEnded && (timeUp || clipEnd))
            {
                state.hasEnded = true;
                ctx.onFinish?.Invoke();
            }
        }

        private void TriggerSoundEvent(SkillContext ctx, SoundFrameEvent evt)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.Play(evt.soundId, ctx.caster.position);
            // volume / loop 字段待 SoundManager 扩展重载后生效
        }

        // ──────────────── 动画覆盖（方案 B：动画 clip 映射）────────────────
        // AnimatorOverrideController 的映射是【基础 controller 里的原 clip → 替换 clip】，
        // 所以 baseClip 必须是"基础 controller 里那个原 clip"（由技能编辑器解析后写进资产）。
        // 运行时读不到 UnityEditor，不能现场解析动画机，所以必须预先存好。

        /// <summary>记住每个 Animator 最初挂的 controller。
        /// 玩家 Animator 本来就可能挂着 AnimatorOverrideController（如持斧版），
        /// 不能把它当成"我们套的壳"剥掉，否则会丢原有的覆盖。</summary>
        private static readonly Dictionary<Animator, RuntimeAnimatorController> OriginalControllers
            = new Dictionary<Animator, RuntimeAnimatorController>();

        /// <summary>AOC 缓存：避免每次放技能都 new 一个 AnimatorOverrideController</summary>
        private static readonly Dictionary<string, AnimatorOverrideController> OverrideCache
            = new Dictionary<string, AnimatorOverrideController>();

        private const int OverrideCacheLimit = 16;

        private void ApplyAnimationOverride(Animator animator)
        {
            if (!OriginalControllers.TryGetValue(animator, out var original) || original == null)
            {
                original = animator.runtimeAnimatorController;
                OriginalControllers[animator] = original;
            }
            if (original == null) return;

            // 没配覆盖 → 保证用的是原始 controller（被前面带覆盖的技能换过就换回来）
            if (overrideClip == null)
            {
                if (animator.runtimeAnimatorController != original)
                    animator.runtimeAnimatorController = original;
                return;
            }

            if (baseClip == null)
            {
                Debug.LogWarning($"[Skill] 组件配了 overrideClip「{overrideClip.name}」但没有 baseClip —— " +
                                 "请在技能编辑器里选中这个「播放动画」组件（编辑器会自动解析并保存），" +
                                 "或清空 overrideClip。本次按 stateName 原样播放。");
                return;
            }

            var aoc = GetOverrideController(original, baseClip, overrideClip);
            if (aoc != null && animator.runtimeAnimatorController != aoc)
                animator.runtimeAnimatorController = aoc;
        }

        private static AnimatorOverrideController GetOverrideController(
            RuntimeAnimatorController original, AnimationClip baseClip, AnimationClip overrideClip)
        {
            string key = $"{original.GetInstanceID()}:{baseClip.GetInstanceID()}:{overrideClip.GetInstanceID()}";
            if (OverrideCache.TryGetValue(key, out var cached) && cached != null) return cached;

            if (OverrideCache.Count > OverrideCacheLimit) OverrideCache.Clear();

            try
            {
                var srcAoc = original as AnimatorOverrideController;
                var baseAc = srcAoc != null ? srcAoc.runtimeAnimatorController : original;
                if (baseAc == null) return null;

                var aoc = new AnimatorOverrideController(baseAc);

                // 继承原有覆盖（例如持斧版替换掉的 7 个动画）
                if (srcAoc != null)
                {
                    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    srcAoc.GetOverrides(pairs);
                    aoc.ApplyOverrides(pairs);
                }

                aoc[baseClip] = overrideClip;   // 我们的映射
                OverrideCache[key] = aoc;
                return aoc;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Skill] 创建 AnimatorOverrideController 失败，本次按 stateName 原样播放：{e.Message}");
                return null;
            }
        }

        // 技能结束 / 被打断：把动画控制权交还给角色（由角色决定回待机还是移动）
        public override void OnEnd(SkillContext ctx)
        {
            SkillEvents.RequestAnimationReturn(ctx != null ? ctx.caster : null, returnStateName);
        }

        public override void OnInterrupt(SkillContext ctx)
        {
            SkillEvents.RequestAnimationReturn(ctx != null ? ctx.caster : null, returnStateName);
        }
    }
}
