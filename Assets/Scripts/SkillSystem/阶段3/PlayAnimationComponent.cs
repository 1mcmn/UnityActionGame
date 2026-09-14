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
        public float crossFade = 0.1f;        // 过渡时间（秒）
        public float duration = 1f;           // 后备时长（拿不到 clip 长度时使用）

        [Header("帧事件列表")]
        public List<SoundFrameEvent> frameEvents = new List<SoundFrameEvent>();

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
                if (clipInfo.Length > 0)
                {
                    state.clip = clipInfo[0].clip;
                    state.clipLength = state.clip.length;
                }
                state.lengthCaptured = true;
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

            // 结束判定（v3）：duration 为权威时长（数据驱动，界面里可调）；
            // 动画自身播到 1.0 也会提前结束。不再用"离开目标状态"判定（子状态机名匹配不上）。
            bool timeUp = elapsed >= duration;
            bool clipEnd = !stateInfo.loop && rawNormalized >= 1f;
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

        public override void OnEnd(SkillContext ctx) { }
        public override void OnInterrupt(SkillContext ctx) { }
    }
}
