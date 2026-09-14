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

        [Header("绑定节点（推荐填：武器/手部骨骼名）")]
        [Tooltip("判定盒挂在哪个节点上。填【骨骼名字】（如 J_Bip_R_Hand）或【相对路径】（Armature/Body/.../J_Bip_R_Hand）。\n" +
                 "留空 = 挂在释放者根节点 + offset（旧行为，判定不跟动画走 → 容易隔空）。\n" +
                 "填了 = 判定盒跟随该节点的位置/朝向，并在两帧之间做扫掠采样防高速挥砍漏判。\n" +
                 "⚠ ScriptableObject 资产不允许引用场景对象，所以这里存的是【名字】，运行时再解析。")]
        public string attachNodeName = "";

        [Header("检测形状")]
        public HitShape shape = HitShape.Sphere;
        public float radius = 1.5f;                       // 球/胶囊的半径
        [Tooltip("胶囊：沿节点前向的总长度（如刀剑刃长）")]
        public float capsuleLength = 1.2f;
        [Tooltip("盒形：三个半边长")]
        public Vector3 boxHalfExtents = new Vector3(0.6f, 0.4f, 0.6f);
        public Vector3 offset = new Vector3(0, 0.5f, 0.5f); // 相对节点朝向的偏移

        [Header("朝向对齐（骨骼轴向不沿刀身时用这个）")]
        [Tooltip("判定盒相对【绑定节点】的旋转偏移（欧拉角，度）。\n" +
                 "手部/武器骨骼的局部坐标轴通常不沿刀身 —— 用这个把判定盒转过来对准武器方向。\n" +
                 "最直观的调法：在 Scene 视图里拖判定盒上的旋转手柄（红/绿/蓝圈）。\n" +
                 "注意：offset 在【节点坐标系】里，所以旋转对齐不会让判定盒跑位。")]
        public Vector3 rotationOffset = Vector3.zero;

        /// <summary>
        /// 判定盒中心（世界坐标）。偏移在【节点坐标系】里计算，与旋转对齐无关。
        /// 运行时和编辑器可视化共用，保证"所见即所得"。
        /// </summary>
        public Vector3 GetHitCenter(Transform node)
        {
            return node == null ? Vector3.zero : node.position + node.rotation * offset;
        }

        /// <summary>判定盒朝向 = 【节点朝向】×【rotationOffset】</summary>
        public Quaternion GetHitRotation(Transform node)
        {
            return node == null ? Quaternion.identity : node.rotation * Quaternion.Euler(rotationOffset);
        }

        [Header("检测目标层")]
        [Tooltip("敌人的 Layer 掩码，与 PlayerCombat.enemyLayer 保持一致（当前配置为 128 = 第 7 层）")]
        public LayerMask targetMask = 128;

        [Header("挂载的其他组件（引用关系，内嵌序列化）")]
        public DamageComponent damageComponent;
        public HitFeedbackComponent feedbackComponent;

        [Header("交互结果")]
        [Tooltip("被格挡时的伤害倍率（0.2 = 只吃 20% 伤害）")]
        [Range(0f, 1f)] public float blockedDamageScale = 0.2f;

        private class HitWindowState
        {
            public HashSet<Transform> hitTargets = new HashSet<Transform>();
            public bool isActive = false;
            // 扫掠检测：记住上一帧的节点变换，两帧之间插值采样
            public Vector3 lastNodePos;
            public Quaternion lastNodeRot;
            public bool hasLastPos = false;
            // 绑定节点解析缓存（attachNodeName → Transform）
            public Transform node;
            public bool nodeResolved = false;
        }

        /// <summary>两帧之间插值采样次数（防高速挥砍"从敌人左边跳到右边"漏判）</summary>
        private const int SweepSamples = 3;

        /// <summary>
        /// 按【名字】或【相对路径】解析绑定节点。
        /// 编辑器做可视化时也调用这个（所以是 public static）。
        /// ⚠ 之所以不直接存 Transform 引用：ScriptableObject 资产不允许引用场景对象。
        /// </summary>
        public static Transform ResolveNodeStatic(Transform root, string nameOrPath)
        {
            if (root == null || string.IsNullOrEmpty(nameOrPath)) return null;

            // 先按相对路径找（支持 "Armature/Body/J_Bip_R_Hand"）
            var byPath = root.Find(nameOrPath);
            if (byPath != null) return byPath;

            // 再按名字递归找
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == nameOrPath) return t;

            return null;
        }

        private Transform GetNode(SkillContext ctx, HitWindowState state)
        {
            if (string.IsNullOrEmpty(attachNodeName)) return null;   // 未绑定 → 走旧行为

            if (!state.nodeResolved)
            {
                state.node = ResolveNodeStatic(ctx.caster, attachNodeName);
                state.nodeResolved = true;

                if (state.node == null)
                    Debug.LogWarning($"[Skill] 判定窗口找不到绑定节点「{attachNodeName}」—— " +
                                     "本次退化为挂在角色根节点。请检查技能编辑器里填的名字。");
            }
            return state.node;
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

            Collider[] hits = DetectHits(ctx, state);

            foreach (var hit in hits)
            {
                if (hit == null) continue;
                if (state.hitTargets.Contains(hit.transform)) continue;
                state.hitTargets.Add(hit.transform);
                ApplyHit(ctx, hit.transform);
            }
        }

        /// <summary>
        /// 判定检测。
        /// attachNode 为空 → 单次检测，几何全部基于释放者根节点（**与旧行为完全一致**）。
        /// attachNode 有填 → 几何基于该节点，并在"上一帧节点变换 → 这一帧节点变换"之间做 SweepSamples 次插值采样，
        /// 把每帧扫过的体积都查一遍（解决"隔空"和"高速挥砍漏判"）。
        /// </summary>
        private Collider[] DetectHits(SkillContext ctx, HitWindowState state)
        {
            // 绑定节点（没绑 → null，下面退回释放者根节点 = 旧行为）
            Transform bound = GetNode(ctx, state);
            Transform node = bound != null ? bound : ctx.caster;

            Vector3 curPos = node.position;
            Quaternion curNodeRot = node.rotation;

            // 没有绑定节点时保持旧行为：只查当前位置，不做扫掠
            int samples = (bound != null && state.hasLastPos) ? SweepSamples : 1;

            var found = new List<Collider>();
            for (int s = 0; s < samples; s++)
            {
                float t = samples == 1 ? 1f : (s + 1f) / samples;
                Vector3 pos = state.hasLastPos ? Vector3.Lerp(state.lastNodePos, curPos, t) : curPos;
                Quaternion nodeRot = state.hasLastPos ? Quaternion.Slerp(state.lastNodeRot, curNodeRot, t) : curNodeRot;
                CollectHitsAt(pos + nodeRot * offset, nodeRot * Quaternion.Euler(rotationOffset), found);
            }

            state.lastNodePos = curPos;
            state.lastNodeRot = curNodeRot;
            state.hasLastPos = true;

            return found.ToArray();
        }

        /// <summary>在给定的世界中心/朝向下，按当前形状查一次；结果合并去重</summary>
        private void CollectHitsAt(Vector3 center, Quaternion rot, List<Collider> found)
        {
            Collider[] hits;

            switch (shape)
            {
                case HitShape.Capsule:
                    Vector3 dir = rot * Vector3.forward;
                    Vector3 p0 = center - dir * (capsuleLength * 0.5f);
                    Vector3 p1 = center + dir * (capsuleLength * 0.5f);
                    hits = Physics.OverlapCapsule(p0, p1, radius, targetMask);
                    break;

                case HitShape.Box:
                    hits = Physics.OverlapBox(center, boxHalfExtents, rot, targetMask);
                    break;

                default:   // Sphere
                    hits = Physics.OverlapSphere(center, radius, targetMask);
                    break;
            }

            if (hits == null) return;
            foreach (var h in hits)
            {
                if (h == null) continue;
                if (!found.Contains(h)) found.Add(h);
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
                state.hasLastPos = false;   // 下一轮挥击重新建立扫掠基准
            }
        }

        private void ApplyHit(SkillContext ctx, Transform target)
        {
            // ① 先问目标："这一下打在你身上算什么结果？"（没人实现接口时默认 Hit，行为与旧版一致）
            HitOutcome outcome = HitResolver.Resolve(target, ctx.caster);

            // 无敌帧：伤害和反馈全部跳过（否则会有"假命中音效"）
            if (outcome == HitOutcome.Invincible) return;

            // ② 伤害（格挡减伤、弹反不吃伤害）
            if (damageComponent != null)
            {
                Enemy enemy = target.GetComponent<Enemy>();
                if (enemy != null)
                {
                    float damage = damageComponent.damage;
                    if (outcome == HitOutcome.Blocked) damage *= blockedDamageScale;
                    else if (outcome == HitOutcome.Parried) damage = 0f;

                    if (damage > 0f)
                    {
                        // 伤害 + 击退由 Enemy.TakeDamage 内部统一处理（AddForce 击退、僵直、无敌帧、死亡）
                        Vector3 dir = (target.position - ctx.caster.position).normalized;
                        enemy.TakeDamage(damage, dir);
                    }
                }
            }

            // ③ 反馈：按结果选不同的音效前缀 + 顿帧
            if (feedbackComponent != null)
                feedbackComponent.Execute(ctx, target, outcome);
        }
    }

    /// <summary>一次攻击落在目标身上的"结果"——决定播哪个音效、吃多少伤害</summary>
    public enum HitOutcome
    {
        Hit,          // 正常命中
        Blocked,      // 被格挡（目标正在格挡 + 攻击来自正面）
        Parried,      // 被弹反（命中弹反窗口）
        Invincible,   // 目标在无敌帧 → 伤害和反馈全部跳过
    }

    /// <summary>
    /// "能对被击中做出反应"的目标实现这个接口（敌人格挡、玩家弹反…）。
    /// 没人实现时 HitResolver 一律返回 Hit —— 所以这套是【零侵入】的：
    /// 现有游戏行为完全不变；等你给 Enemy / PlayerCombat 补上属性，格挡/弹反立刻生效。
    ///
    /// 例：
    ///   public class EnemyAI : MonoBehaviour, IHitReactable {
    ///       public bool IsBlocking => _state == EnemyState.BlockLoop;
    ///       public bool IsParryWindow => false;
    ///   }
    /// </summary>
    public interface IHitReactable
    {
        bool IsBlocking { get; }      // 正在格挡
        bool IsParryWindow { get; }   // 在弹反窗口内
    }

    public static class HitResolver
    {
        /// <summary>正面判定角度：攻击者在目标正前方这个角度内才算"挡住"</summary>
        private const float FrontalAngle = 120f;

        public static HitOutcome Resolve(Transform target, Transform attacker)
        {
            if (target == null) return HitOutcome.Hit;

            var reactor = target.GetComponentInParent<IHitReactable>();
            if (reactor == null) return HitOutcome.Hit;      // 没实现接口 → 默认命中

            if (reactor.IsParryWindow) return HitOutcome.Parried;
            if (reactor.IsBlocking && IsFrontal(target, attacker)) return HitOutcome.Blocked;
            return HitOutcome.Hit;
        }

        private static bool IsFrontal(Transform target, Transform attacker)
        {
            if (attacker == null) return true;

            Vector3 toAttacker = attacker.position - target.position;
            toAttacker.y = 0f;
            if (toAttacker.sqrMagnitude < 0.0001f) return true;

            return Vector3.Angle(target.forward, toAttacker.normalized) <= FrontalAngle * 0.5f;
        }
    }
}
