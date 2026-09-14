using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillSystem
{
    // 判定形状
    public enum HitShape
    {
        Sphere,   // 球形：圆心 + 半径
        Capsule,  // 胶囊形：沿释放者前向的线段 + 半径（适合刀剑类武器，参考视频同款）
        Box       // 盒形：中心 + 三个半边长（适合锤类/范围判定）
    }

    // 判定窗口组件：在指定帧区间内检测敌人，一次挥击对同一目标只结算一次。
    // ⚠️ 顺序要求：必须排在 PlayAnimationComponent 之后（读取其写入的 CurrentFrame）。
    [Serializable]
    public class HitWindowComponent : SkillComponent
    {
        [Header("判定窗口（帧，30fps 基准）")]
        public int startFrame = 5;
        public int endFrame = 15;

        [Header("检测形状")]
        public HitShape shape = HitShape.Sphere;
        public float radius = 1.5f;                       // 球/胶囊的半径
        [Tooltip("胶囊：沿释放者前向的总长度（如刀剑刃长）")]
        public float capsuleLength = 1.2f;
        [Tooltip("盒形：三个半边长")]
        public Vector3 boxHalfExtents = new Vector3(0.6f, 0.4f, 0.6f);
        public Vector3 offset = new Vector3(0, 0.5f, 0.5f); // 相对释放者朝向的偏移

        [Header("检测目标层")]
        [Tooltip("敌人的 Layer 掩码，与 PlayerCombat.enemyLayer 保持一致（当前配置为 128 = 第 7 层）")]
        public LayerMask targetMask = 128;

        [Header("挂载的其他组件（引用关系，内嵌序列化）")]
        public DamageComponent damageComponent;
        public HitFeedbackComponent feedbackComponent;

        private class HitWindowState
        {
            public HashSet<Transform> hitTargets = new HashSet<Transform>();
            public bool isActive = false;
        }

        public override void OnStart(SkillContext ctx)
        {
            ctx.runtimeData["HitWindow"] = new HitWindowState();
        }

        public override void OnTick(SkillContext ctx)
        {
            if (!ctx.runtimeData.TryGetValue("CurrentFrame", out var frameObj)) return;
            if (!ctx.runtimeData.TryGetValue("HitWindow", out var stateObj)) return;

            int currentFrame = (int)frameObj;
            var state = (HitWindowState)stateObj;

            bool shouldBeActive = currentFrame >= startFrame && currentFrame <= endFrame;

            if (shouldBeActive && !state.isActive)
            {
                state.isActive = true;
                state.hitTargets.Clear();   // 每次进入窗口 = 新一轮挥击，清空去重表
            }
            if (!shouldBeActive)
            {
                state.isActive = false;
                return;
            }

            Collider[] hits = DetectHits(ctx);

            foreach (var hit in hits)
            {
                if (hit == null) continue;
                if (state.hitTargets.Contains(hit.transform)) continue;
                state.hitTargets.Add(hit.transform);
                ApplyHit(ctx, hit.transform);
            }
        }

        private Collider[] DetectHits(SkillContext ctx)
        {
            Vector3 center = ctx.caster.position + ctx.caster.TransformDirection(offset);

            switch (shape)
            {
                case HitShape.Sphere:
                    return Physics.OverlapSphere(center, radius, targetMask);

                case HitShape.Capsule:
                    Vector3 dir = ctx.caster.forward;
                    Vector3 p0 = center - dir * (capsuleLength * 0.5f);
                    Vector3 p1 = center + dir * (capsuleLength * 0.5f);
                    return Physics.OverlapCapsule(p0, p1, radius, targetMask);

                case HitShape.Box:
                    return Physics.OverlapBox(center, boxHalfExtents, ctx.caster.rotation, targetMask);

                default:
                    return Physics.OverlapSphere(center, radius, targetMask);
            }
        }

        public override void OnEnd(SkillContext ctx) => ClearState(ctx);

        public override void OnInterrupt(SkillContext ctx) => ClearState(ctx);

        private void ClearState(SkillContext ctx)
        {
            if (ctx.runtimeData.TryGetValue("HitWindow", out var stateObj))
            {
                var state = (HitWindowState)stateObj;
                state.isActive = false;
                state.hitTargets.Clear();
            }
        }

        private void ApplyHit(SkillContext ctx, Transform target)
        {
            if (damageComponent != null)
            {
                Enemy enemy = target.GetComponent<Enemy>();
                if (enemy != null)
                {
                    // 伤害 + 击退由 Enemy.TakeDamage 内部统一处理（AddForce 击退、僵直、无敌帧、死亡）
                    Vector3 dir = (target.position - ctx.caster.position).normalized;
                    enemy.TakeDamage(damageComponent.damage, dir);
                }
            }
            if (feedbackComponent != null)
                feedbackComponent.Execute(ctx, target);
        }
    }
}
