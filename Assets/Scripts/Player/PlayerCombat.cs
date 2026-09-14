using System;
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

    /// <summary>Animation Event：执行攻击碰撞检测并造成伤害</summary>
    public void PerformHitDetection()
    {
        _hasHitThisSwing = false;
        Vector3 origin = transform.position + transform.forward * 1.5f;

        Collider[] colliders = Physics.OverlapSphere(origin, AttackRadius, enemyLayer);

        foreach (Collider col in colliders)
        {
            Enemy enemy = col.GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(CurrentAttackDamage);
                _hasHitThisSwing = true;
                _lastHitPoint = col.transform.position;
                DamagePopupManager.Instance?.Show(col.bounds.center, CurrentAttackDamage);
            }
        }

        // 命中后：播放命中音效 + 顿帧（打击感核心）
        if (_hasHitThisSwing)
        {
            PlayHitSfxByStep();
            TriggerHitStop();
        }
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