using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : MonoBehaviour
{
    [Header("攻击数据（可选：用 ComboData 资产覆盖下方默认值）")]
    [SerializeField] private ComboData comboData;

    [Header("攻击")]
    [Tooltip("每段攻击的锁定时间（秒）")]
    [SerializeField] private float attackDuration = 0.45f;
    [Tooltip("第1~N段连击的伤害值")]
    [SerializeField] private float[] _comboDamages = { 15f, 18f, 22f, 28f, 35f };
    [Tooltip("攻击球形判定半径")]
    [SerializeField] private float attackRadius = 2.5f;
    [Tooltip("敌人所在的 Layer")]
    [SerializeField] private LayerMask enemyLayer;

    [Header("武器判定（自动贴合武器网格）")]
    [Tooltip("是否启用武器判定。取消勾选 = 回到角色中心大球（旧行为，用于对照实验）")]
    [SerializeField] private bool _useWeaponHitbox = true;
    [Tooltip("手动指定武器/挂点名字（留空 = 自动使用 WeaponAttachment 挂到手上的那把武器）")]
    [SerializeField] private string _weaponNodeName = "";
    [Tooltip("半径缩放：1 = 刚好包住武器；觉得判定太苛刻就调大（1.2~1.5）")]
    [SerializeField] private float _weaponRadiusScale = 1f;
    [Tooltip("半径下限（米），避免细长的武器判定过窄")]
    [SerializeField] private float _weaponRadiusMin = 0.08f;
    [Tooltip("兜底半径：完全拿不到武器网格时才用")]
    [SerializeField] private float _weaponCastRadius = 0.25f;
    [Tooltip("兜底刃长：完全拿不到武器网格时才用")]
    [SerializeField] private float _weaponBladeLength = 0.7f;

    public LayerMask EnemyLayer => enemyLayer;

    // 数据资产优先，未赋值时回退到 Inspector 内联字段
    private float AttackDuration => comboData != null ? comboData.attackDuration : attackDuration;
    private float AttackRadius   => comboData != null ? comboData.attackRadius   : attackRadius;
    private float ComboWindowPercent => comboData != null ? comboData.comboWindowPercent : 0.35f;
    private float ComboGraceWindow  => comboData != null ? comboData.comboGraceWindow  : 0.15f;
    private int   MaxComboStep      => comboData != null ? comboData.maxComboStep      : 4;
    private float[] ComboDamages => comboData != null ? comboData.comboDamages  : _comboDamages;

    [Header("弹反")]
    [Tooltip("弹反球形检测半径")]
    [SerializeField] private float _parryRadius  = 2f;

    [Header("顿帧")]
    [Tooltip("命中时画面停顿的时长（现实秒）")]
    [SerializeField] private float _hitStopDuration = 0.05f;
    [Tooltip("顿帧期间的时间缩放（0.1=10%速度）")]
    [SerializeField] private float _hitStopScale    = 0.1f;

    [Header("生命值")]
    [Tooltip("玩家最大生命值")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    private bool isInvulnerable;

    public static event Action<float> OnPlayerDamaged;
    public static event Action<int> OnPlayerHit;      // 参数 = 受击方向（PlayerHitType）
    public static event Action OnPlayerDeath;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    private float attackTimer;
    private float graceTimer;          // 残响窗口计时（段结束后慢点击续招）
    private float forceExitTimer;
    private int   _comboStep;          // 当前是第几段连击（0=普攻1, 1=普攻2, ...）
    private bool  _comboWindowOpen;    // 攻击全程（含收刀）允许连击输入
    private bool  _comboBuffered;      // 连击输入已缓冲，窗口内自动接上

    // 命中追踪（供 Animation Event 链使用：PerformHitDetection → PlayHitSfx / TriggerHitStop）
    private bool    _hasHitThisSwing;
    private Vector3 _lastHitPoint;

    // 武器判定：节点缓存 + 上一帧位置（做"上一帧→这一帧"的扫掠）
    private Transform _weaponNode;
    private WeaponAttachment _weaponAttachment;
    private Vector3   _lastWeaponPos;
    private bool      _hasLastWeaponPos;

    public bool IsAttacking => attackTimer > 0f;

    /// <summary>攻击进度（0=刚起手，1=收招结束）。供状态机判断取消窗口。</summary>
    public float AttackProgress01 => AttackDuration > 0.0001f ? 1f - attackTimer / AttackDuration : 1f;

    /// <summary>连击输入缓冲：由状态机在"动画窗口内"调用（窗口外点击会被状态机直接丢弃）。</summary>
    public void BufferCombo() => _comboBuffered = true;

    /// <summary>是否有待消费的连击缓冲（供状态机判断"新起手还是续段"）。</summary>
    public bool HasBufferedCombo => _comboBuffered;

    /// <summary>连击窗口起点（动画进度 0~1），由状态机读取动画归一化时间比较。</summary>
    public float ComboWindowStart => 1f - ComboWindowPercent;

    /// <summary>窗口内且有缓冲输入时执行连击，推进到下一段。成功返回 true。</summary>
    public bool ConsumeBufferedCombo()
    {
        if (!_comboBuffered) return false;

        // 攻击已结束：残响窗口内仍可推进；超时丢弃
        if (attackTimer <= 0f)
        {
            if (graceTimer <= 0f)
            {
                _comboBuffered = false;
                return false;
            }
        }

        // 段数钳制：已到最后一段不再推进（防数组越界/触发器空转）
        if (_comboStep >= MaxComboStep)
        {
            _comboBuffered = false;
            return false;
        }

        attackTimer = AttackDuration;
        graceTimer = 0f;
        _comboStep++;
        _comboBuffered = false;
        PlaySwingSfx();
        return true;
    }

    public void Initialize()
    {
        currentHealth = maxHealth;
    }

    // ─── 攻击计时 ──────────────────────────────────

    public void StartAttack()
    {
        attackTimer = AttackDuration;
        forceExitTimer = 0f;
        graceTimer = 0f;
        _comboStep = 0;
        _comboBuffered = false;
        PlaySwingSfx();
    }

    /// <summary>每帧递减攻击计时器（步骤 2）。仅在 IsAttacking 时调用。</summary>
    public void DecrementTimer(float deltaTime)
    {
        attackTimer -= deltaTime;
        // 计时归零时打开残响窗口（慢点击续招的兜底）
        if (attackTimer <= 0f && graceTimer <= 0f)
            graceTimer = ComboGraceWindow;
    }

    // ─── 强制退出保险 ──────────────────────────────

    /// <summary>
    /// 攻击计时器到期后，每帧累加强制退出计时器。
    /// 返回 true 表示已超 0.3 秒安全上限。
    /// </summary>
    public bool IncrementForceExitTimer(float deltaTime)
    {
        forceExitTimer += deltaTime;
        return forceExitTimer >= 0.3f;
    }

    public void ResetForceExitTimer()
    {
        forceExitTimer = 0f;
    }

    /// <summary>段计时结束后的残响窗口是否仍在（残响内保持攻击状态，等慢点击）。</summary>
    public bool IsInGrace => attackTimer <= 0f && graceTimer > 0f;

    /// <summary>逐帧递减残响窗口计时（仅在 IsAttacking 为 false 时调用）。</summary>
    public void DecrementGraceTimer(float deltaTime)
    {
        if (attackTimer <= 0f)
            graceTimer = Mathf.Max(0f, graceTimer - deltaTime);
    }

    /// <summary>攻击状态退出时清零所有计时器。</summary>
    public void ResetAllTimers()
    {
        attackTimer = 0f;
        forceExitTimer = 0f;
        graceTimer = 0f;
        _comboStep = 0;
        _comboBuffered = false;
    }

    // ─── Animation Event 钩子 ────────────────────────
    // 由攻击动画 clip 的 Animation Event 在精确帧调用。
    // 典型顺序：PlaySfx("atk01_swing") → PerformHitDetection → PlayHitSfx("atk01_hit") → TriggerHitStop
    //
    // SoundLibrary 命名规则：每个攻击独立前缀，一一对应：
    //   atk01_swing   ← 普攻1挥砍     atk01_hit   ← 普攻1命中
    //   atk02_swing   ← 普攻2挥砍     atk02_hit   ← 普攻2命中
    //   ...
    // Animation Event String 栏填前缀，如 atk01_swing

    /// <summary>当前攻击段的伤害值（根据 _comboStep 从数组取，越界则取第一段）</summary>
    private float CurrentAttackDamage
    {
        get
        {
            float[] d = ComboDamages;
            if (d == null || d.Length == 0) return 10f;  // fallback
            int idx = Mathf.Clamp(_comboStep, 0, d.Length - 1);
            return d[idx];
        }
    }

    public void PlaySfx(string prefix)
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayByPrefix(prefix, transform.position);
    }

    /// <summary>播放当前攻击段的挥砍音效（atk0X_swing，X=段号）</summary>
    private void PlaySwingSfx()
    {
        int seg = Mathf.Clamp(_comboStep + 1, 1, 5);
        PlaySfx($"atk0{seg}_swing");
    }

    /// <summary>播放当前攻击段的命中音效（atk0X_hit，X=段号）</summary>
    private void PlayHitSfxByStep()
    {
        int seg = Mathf.Clamp(_comboStep + 1, 1, 5);
        PlaySfx($"atk0{seg}_hit");
    }

    /// <summary>Animation Event：播放脚步声（自动随机 pitch 0.9~1.1，避免短音效重复感）</summary>
    public void PlayFootstepSfx(string prefix)
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayByPrefix(prefix, transform.position, 0.9f, 1.1f);
    }

    /// <summary>
    /// Animation Event：挥击帧的命中检测。
    /// 默认走【武器骨骼 + 胶囊体积 + 帧间扫掠】两条检测：
    ///   ① Physics.OverlapCapsule —— 刀身体积（对应任务书要求的 Capsule Collider 语义），
    ///      解决"根本没挥到却打中"的误判；
    ///   ② Physics.SphereCastAll —— 从上一帧刀身位置扫到这一帧（对应 SphereCast 语义），
    ///      解决高速挥砍时"两帧之间从敌人身上跳过去"的漏判。
    /// 找不到武器节点、或关掉 _useWeaponHitbox 时，退化为原来的角色中心球形判定。
    /// </summary>
    public void PerformHitDetection()
    {
        _hasHitThisSwing = false;

        Collider[] colliders = (_useWeaponHitbox && ResolveWeaponNode() != null)
            ? CollectWeaponHits()
            : Physics.OverlapSphere(transform.position + transform.forward * 1.5f, AttackRadius, enemyLayer);

        var hitEnemies = new List<Enemy>();   // 同一次挥击对同一个敌人只结算一次
        foreach (Collider col in colliders)
        {
            if (col == null) continue;

            Enemy enemy = col.GetComponent<Enemy>();
            if (enemy == null || hitEnemies.Contains(enemy)) continue;
            hitEnemies.Add(enemy);

            enemy.TakeDamage(CurrentAttackDamage);
            _hasHitThisSwing = true;
            _lastHitPoint = col.transform.position;
            DamagePopupManager.Instance?.Show(col.bounds.center, CurrentAttackDamage);
        }

        // 命中后：播放命中音效 + 顿帧（打击感核心）
        if (_hasHitThisSwing)
        {
            PlayHitSfxByStep();
            TriggerHitStop();
        }
    }

    /// <summary>
    /// 武器判定：拿到判定胶囊（优先用武器上的 CapsuleCollider）+ 帧间扫掠，结果合并去重。
    /// 胶囊来源优先级：① 武器上自己摆的 CapsuleCollider → ② 按武器网格自动拟合 → ③ 节点前向兜底。
    /// </summary>
    private Collider[] CollectWeaponHits()
    {
        var found = new List<Collider>();

        if (!TryGetWeaponColliderCapsule(out Vector3 p0, out Vector3 p1, out float radius)
            && !TryFitWeaponCapsule(out p0, out p1, out radius)
            && !TryGetFallbackCapsule(out p0, out p1, out radius))
            return found.ToArray();

        // ① 武器体积（Capsule Collider 语义）：胶囊刚好包住整把武器
        AddHits(Physics.OverlapCapsule(p0, p1, radius, enemyLayer), found);

        // ② 上一帧 → 这一帧的扫掠（SphereCast 语义）：防高速挥砍从敌人身上"跳"过去
        Vector3 center = (p0 + p1) * 0.5f;
        if (_hasLastWeaponPos)
        {
            Vector3 delta = center - _lastWeaponPos;
            float dist = delta.magnitude;
            if (dist > 0.0005f)
                AddHits(Physics.SphereCastAll(_lastWeaponPos, radius, delta / dist, dist, enemyLayer), found);
        }

        return found.ToArray();
    }

    /// <summary>
    /// ① 优先：直接读【武器上自己摆的 CapsuleCollider】的几何参数（中心/半径/高度/方向），
    /// 换算成世界空间的胶囊两端点 + 半径。这样判定形状与你肉眼看到的碰撞体完全一致，
    /// 也正好对上任务书"命中检测使用 Capsule Collider"的要求。
    /// 运行时用武器实例上的碰撞体；编辑器里用 weaponPrefab 上的碰撞体预览。
    /// </summary>
    private bool TryGetWeaponColliderCapsule(out Vector3 p0, out Vector3 p1, out float radius)
    {
        p0 = p1 = Vector3.zero;
        radius = 0f;

        if (!TryGetWeaponFitSource(out Transform geomRoot, out _, out Matrix4x4 placement))
            return false;

        var capsule = geomRoot.GetComponentInChildren<CapsuleCollider>(true);
        if (capsule == null) return false;

        Transform ct = capsule.transform;
        Vector3 axisLocal = capsule.direction == 0 ? Vector3.right
                          : capsule.direction == 1 ? Vector3.up
                          : Vector3.forward;
        float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);

        // 碰撞体局部 → 武器根局部 → 世界（用相对矩阵，prefab 与实例都适用）
        Matrix4x4 toRoot = geomRoot.worldToLocalMatrix * ct.localToWorldMatrix;
        Vector3 localP0 = toRoot.MultiplyPoint3x4(capsule.center - axisLocal * halfSegment);
        Vector3 localP1 = toRoot.MultiplyPoint3x4(capsule.center + axisLocal * halfSegment);

        Vector3 rootScale = toRoot.lossyScale;
        float colliderScale = (Mathf.Abs(rootScale.x) + Mathf.Abs(rootScale.y) + Mathf.Abs(rootScale.z)) / 3f;
        float localRadius = capsule.radius * colliderScale;

        p0 = placement.MultiplyPoint3x4(localP0);
        p1 = placement.MultiplyPoint3x4(localP1);

        Vector3 placeScale = placement.lossyScale;
        float uniform = (Mathf.Abs(placeScale.x) + Mathf.Abs(placeScale.y) + Mathf.Abs(placeScale.z)) / 3f;
        radius = localRadius * uniform;

        return radius > 0.0005f;
    }

    /// <summary>兜底：完全拿不到武器网格时，用节点前向 + 固定刃长/半径。</summary>
    private bool TryGetFallbackCapsule(out Vector3 p0, out Vector3 p1, out float radius)
    {
        p0 = p1 = Vector3.zero;
        radius = _weaponCastRadius;

        var node = ResolveWeaponNode();
        if (node == null) return false;

        Vector3 dir = node.forward;
        Vector3 c = node.position;
        p0 = c - dir * (_weaponBladeLength * 0.5f);
        p1 = c + dir * (_weaponBladeLength * 0.5f);
        return true;
    }

    /// <summary>
    /// 按武器网格自动拟合判定胶囊：
    /// 合并武器所有 Renderer 的包围盒 → 最长轴就是刀身方向 → 胶囊刚好包住整把武器。
    /// 运行时用实际挂上的武器实例；编辑器里没有实例时用 WeaponAttachment 的 prefab 预览。
    /// </summary>
    private bool TryFitWeaponCapsule(out Vector3 p0, out Vector3 p1, out float radius)
    {
        p0 = p1 = Vector3.zero;
        radius = 0f;

        if (!TryGetWeaponFitSource(out Transform geomRoot, out Renderer[] renderers, out Matrix4x4 placement))
            return false;
        if (!TryGetBoundsInSpace(geomRoot, renderers, out Bounds bounds))
            return false;

        Vector3 size = bounds.size;
        Vector3 axis = Vector3.right;
        if (size.y >= size.x && size.y >= size.z) axis = Vector3.up;
        else if (size.z >= size.x && size.z >= size.y) axis = Vector3.forward;

        Vector3 half = size * 0.5f;
        float halfAlong, crossMax;
        if (axis == Vector3.right) { halfAlong = half.x; crossMax = Mathf.Max(half.y, half.z); }
        else if (axis == Vector3.up) { halfAlong = half.y; crossMax = Mathf.Max(half.x, half.z); }
        else { halfAlong = half.z; crossMax = Mathf.Max(half.x, half.y); }

        float localRadius = Mathf.Max(_weaponRadiusMin, crossMax * _weaponRadiusScale);
        float halfLen = Mathf.Max(0f, halfAlong - localRadius);

        Vector3 localP0 = bounds.center - axis * halfLen;
        Vector3 localP1 = bounds.center + axis * halfLen;

        p0 = placement.MultiplyPoint3x4(localP0);
        p1 = placement.MultiplyPoint3x4(localP1);

        Vector3 scale = placement.lossyScale;
        float uniform = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
        radius = localRadius * uniform;

        return radius > 0.0005f;
    }

    /// <summary>
    /// 取几何来源：① 运行时已挂上的武器实例；② 手动指定的节点（其下确有 Renderer）；
    /// ③ 编辑器里用 WeaponAttachment 的 prefab 预览（不实例化）。
    /// </summary>
    private bool TryGetWeaponFitSource(out Transform geomRoot, out Renderer[] renderers, out Matrix4x4 placement)
    {
        geomRoot = null;
        renderers = null;
        placement = Matrix4x4.identity;

        if (_weaponAttachment == null)
            _weaponAttachment = GetComponentInChildren<WeaponAttachment>();

        // ① 运行时：WeaponAttachment 挂到手上的武器实例
        if (_weaponAttachment != null && _weaponAttachment.Weapon != null)
        {
            var weapon = _weaponAttachment.Weapon;
            var rs = weapon.GetComponentsInChildren<Renderer>(true);
            if (rs != null && rs.Length > 0)
            {
                geomRoot = weapon.transform;
                renderers = rs;
                placement = weapon.transform.localToWorldMatrix;
                return true;
            }
        }

        // ② 手动指定了节点，且它下面确实有带网格的物体
        var node = ResolveWeaponNode();
        if (node != null)
        {
            var rs = node.GetComponentsInChildren<Renderer>(true);
            if (rs != null && rs.Length > 0)
            {
                geomRoot = node;
                renderers = rs;
                placement = node.localToWorldMatrix;
                return true;
            }
        }

        // ③ 编辑器预览：用 weaponPrefab 的包围盒 + 推算的挂载矩阵
        if (_weaponAttachment != null && _weaponAttachment.WeaponPrefab != null)
        {
            var prefab = _weaponAttachment.WeaponPrefab;
            var rs = prefab.GetComponentsInChildren<Renderer>(true);
            if (rs != null && rs.Length > 0)
            {
                geomRoot = prefab.transform;
                renderers = rs;
                placement = _weaponAttachment.GetWeaponWorldMatrix();
                return true;
            }
        }

        return false;
    }

    /// <summary>把一组 Renderer 的包围盒合并到 space 的局部空间（相对矩阵，prefab 与实例都适用）。</summary>
    private static bool TryGetBoundsInSpace(Transform space, Renderer[] renderers, out Bounds bounds)
    {
        bounds = new Bounds();
        if (space == null || renderers == null) return false;

        Matrix4x4 toLocal = space.worldToLocalMatrix;
        bool any = false;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

            Bounds lb = r.localBounds;
            Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
            Vector3 c = lb.center, e = lb.extents;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);

                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }

        return any;
    }

    private static void AddHits(Collider[] cols, List<Collider> acc)
    {
        if (cols == null) return;
        foreach (var c in cols)
            if (c != null && !acc.Contains(c)) acc.Add(c);
    }

    private static void AddHits(RaycastHit[] hits, List<Collider> acc)
    {
        if (hits == null) return;
        foreach (var h in hits)
            if (h.collider != null && !acc.Contains(h.collider)) acc.Add(h.collider);
    }

    /// <summary>解析武器/手部节点（复用技能系统的同名解析，支持名字或相对路径）。</summary>
    private Transform ResolveWeaponNode()
    {
        if (_weaponNode != null) return _weaponNode;
        if (string.IsNullOrEmpty(_weaponNodeName)) return null;
        _weaponNode = SkillSystem.HitWindowComponent.ResolveNodeStatic(transform, _weaponNodeName);
        return _weaponNode;
    }

    /// <summary>每帧记录判定胶囊中心（动画求值之后），供挥击帧做"上一帧 → 这一帧"的扫掠。</summary>
    private void LateUpdate()
    {
        if (TryGetWeaponColliderCapsule(out Vector3 p0, out Vector3 p1, out _)
            || TryFitWeaponCapsule(out p0, out p1, out _))
        {
            _lastWeaponPos = (p0 + p1) * 0.5f;
            _hasLastWeaponPos = true;
            return;
        }

        if (TryGetFallbackCapsule(out p0, out p1, out _))
        {
            _lastWeaponPos = (p0 + p1) * 0.5f;
            _hasLastWeaponPos = true;
            return;
        }

        _hasLastWeaponPos = false;
    }

    /// <summary>在 Scene 视图里按武器网格画出判定胶囊（所见即判定体积）。</summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 p0, p1;
        float radius;

        if (TryGetWeaponColliderCapsule(out p0, out p1, out radius))
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.95f); // 绿色 = 用武器上的 CapsuleCollider
        }
        else if (TryFitWeaponCapsule(out p0, out p1, out radius))
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.95f);  // 橙色 = 按武器网格自动拟合
        }
        else
        {
            if (!TryGetFallbackCapsule(out p0, out p1, out radius)) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);    // 黄色 = 兜底形状（没拿到武器网格）
        }

        DrawWireCapsule(p0, p1, radius);
    }

    /// <summary>用两个端面圆 + 四条母线画胶囊线框（比"两个球加一条线"直观）。</summary>
    private static void DrawWireCapsule(Vector3 p0, Vector3 p1, float radius)
    {
        Vector3 axis = p1 - p0;
        if (axis.sqrMagnitude < 1e-8f)
        {
            Gizmos.DrawWireSphere(p0, radius);
            return;
        }

        Quaternion rot = Quaternion.LookRotation(axis.normalized, Vector3.up);
        Vector3 up = rot * Vector3.up;
        Vector3 right = rot * Vector3.right;
        const int segments = 20;

        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 o0 = right * (Mathf.Cos(a0) * radius) + up * (Mathf.Sin(a0) * radius);
            Vector3 o1 = right * (Mathf.Cos(a1) * radius) + up * (Mathf.Sin(a1) * radius);
            Gizmos.DrawLine(p0 + o0, p0 + o1);
            Gizmos.DrawLine(p1 + o0, p1 + o1);
        }

        Gizmos.DrawLine(p0 + up * radius, p1 + up * radius);
        Gizmos.DrawLine(p0 - up * radius, p1 - up * radius);
        Gizmos.DrawLine(p0 + right * radius, p1 + right * radius);
        Gizmos.DrawLine(p0 - right * radius, p1 - right * radius);
    }

    /// <summary>Animation Event：播指定前缀的命中音效（未命中自动跳过，String 栏填前缀，如 atk01_hit）</summary>
    public void PlayHitSfx(string prefix)
    {
        if (!_hasHitThisSwing) return;
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayByPrefix(prefix, _lastHitPoint);
    }

    /// <summary>Animation Event：触发顿帧（需在 PerformHitDetection 之后）</summary>
    public void TriggerHitStop()
    {
        if (!_hasHitThisSwing) return;
        StartCoroutine(HitStopRoutine());
    }

    /// <summary>强制触发顿帧（弹刀等非命中场景使用）</summary>
    public void TriggerHitStopForce()
    {
        StartCoroutine(HitStopRoutine());
    }

    private System.Collections.IEnumerator HitStopRoutine()
    {
        float normal = Time.timeScale;
        Time.timeScale = _hitStopScale;
        yield return new WaitForSecondsRealtime(_hitStopDuration);
        Time.timeScale = normal;
    }

    public void TakeDamage(float damage, Vector3? attackerPosition = null)
    {
        if (isInvulnerable) return;
        currentHealth -= damage;
        OnPlayerDamaged?.Invoke(currentHealth);

        // 计算受击方向并广播（供动画/状态机选择受击动画）
        int hitType = CalcHitType(attackerPosition);
        OnPlayerHit?.Invoke(hitType);

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            OnPlayerDeath?.Invoke();
        }
    }

    /// <summary>根据攻击者位置计算受击方向（相对玩家朝向）</summary>
    private int CalcHitType(Vector3? attackerPosition)
    {
        if (!attackerPosition.HasValue) return (int)PlayerHitType.Forward;

        Vector3 dir = attackerPosition.Value - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return (int)PlayerHitType.Forward;

        float angle = Vector3.SignedAngle(transform.forward, dir, Vector3.up);
        if (angle > 45f) return (int)PlayerHitType.Right;
        if (angle < -45f) return (int)PlayerHitType.Left;
        return (int)PlayerHitType.Forward;
    }

    public void SetInvulnerable(bool value) => isInvulnerable = value;

    // ─── 格挡架势（小弹刀）────────────────────────
    [Header("格挡架势")]
    [Tooltip("架势上限，满了触发击飞（倒地）")]
    [SerializeField] private float maxPosture = 100f;
    private float _posture;

    public float Posture => _posture;
    public float MaxPosture => maxPosture;
    public bool IsPostureBroken => _posture >= maxPosture;

    /// <summary>格挡受击时累加架势值，满值由 ThirdPersonController 切 Knockdown 状态</summary>
    public void AddPosture(float amount)
    {
        if (_posture >= maxPosture) return;
        _posture = Mathf.Min(maxPosture, _posture + amount);
    }

    /// <summary>起身时清零架势</summary>
    public void ResetPosture() => _posture = 0f;

    // ─── 弹反 ──────────────────────────────────────

    /// <summary>
    /// 尝试弹反周围的敌人。调用时机：玩家按下弹反键，且处于可弹反状态。
    /// 返回 true 表示至少弹反到一个敌人。
    /// </summary>
    public bool TryParry(Vector3 origin)
    {
        Collider[] colliders = Physics.OverlapSphere(origin, _parryRadius, enemyLayer);
        bool hitAny = false;

        foreach (Collider col in colliders)
        {
            EnemyAI enemyAI = col.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.OnParried(origin);
                hitAny = true;
                Debug.Log($"[Combat] 弹反成功！{col.name}");

                // 弹刀成功反馈：金属碰撞音效 + 顿帧
                if (SoundManager.Instance != null)
                    SoundManager.Instance.Play("sword_hit_03", transform.position);
                TriggerHitStopForce();
            }
        }

        return hitAny;
    }
}

/// <summary>玩家受击方向（对应 Animator HitType 参数）</summary>
public enum PlayerHitType
{
    Forward = 0,
    Left    = 1,
    Right   = 2
}
