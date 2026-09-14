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
        [Header("命中音效（按结果选前缀）")]
        [Tooltip("普通命中：按这个前缀随机播一条（库里要有 hit_01 这类 ID）")]
        public string soundIdPrefix = "hit_";

        [Tooltip("被格挡：金属撞击声（库里要有 block_01 这类 ID）")]
        public string blockSoundIdPrefix = "block_";

        [Tooltip("被弹反：振刀声（库里要有 parry_01 这类 ID）")]
        public string parrySoundIdPrefix = "parry_";

        [Header("顿帧")]
        public float hitStopDuration = 0.05f;
        [Range(0.01f, 0.5f)] public float hitStopTimeScale = 0.1f;
        [Tooltip("被格挡/弹反时的顿帧时长 —— 一般比普通命中长，格挡手感更「重」")]
        public float blockedHitStopDuration = 0.08f;

        [Header("击退（备用，见类注释）")]
        public float knockbackDistance = 0.5f;

        public override void OnStart(SkillContext ctx) { }
        public override void OnTick(SkillContext ctx) { }
        public override void OnEnd(SkillContext ctx) { }
        public override void OnInterrupt(SkillContext ctx) { }

        /// <summary>普通命中（保留旧签名，兼容已有调用）</summary>
        public void Execute(SkillContext ctx, Transform hitTarget) => Execute(ctx, hitTarget, HitOutcome.Hit);

        /// <summary>按命中结果播对应音效 + 顿帧（格挡/弹反用不同前缀）</summary>
        public void Execute(SkillContext ctx, Transform hitTarget, HitOutcome outcome)
        {
            if (hitTarget == null || ctx == null) return;

            // 1. 音效：不同结果用不同前缀（库里没有该前缀只会打一条 warning，不会崩）
            string prefix = soundIdPrefix;
            float stopDuration = hitStopDuration;

            if (outcome == HitOutcome.Blocked)
            {
                prefix = blockSoundIdPrefix;
                stopDuration = blockedHitStopDuration;
            }
            else if (outcome == HitOutcome.Parried)
            {
                prefix = parrySoundIdPrefix;
                stopDuration = blockedHitStopDuration;
            }

            if (SoundManager.Instance != null && !string.IsNullOrEmpty(prefix))
                SoundManager.Instance.PlayByPrefix(prefix, hitTarget.position);

            // 2. 顿帧：委托 SkillManager（组件资产是共享的、没有协程，持续型行为必须委托 MonoBehaviour）
            if (SkillManager.Instance != null)
                SkillManager.Instance.RunHitStop(stopDuration, hitStopTimeScale);
        }
    }
}
