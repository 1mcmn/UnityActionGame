using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : MonoBehaviour
{
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

    [Header("弹反")]
    [Tooltip("弹反球形检测半径")]
    [SerializeField] private float _parryRadius  = 2f;
    [Tooltip("弹反造成的僵直伤害")]
    [SerializeField] private float _parryDamage  = 30f;
    [Tooltip("弹反有效窗口（秒）")]
    [SerializeField] private float _parryWindow  = 0.3f;

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
    public float CurrentHealth => currentHealth;

    private float attackTimer;
    private float forceExitTimer;
    private int   _comboStep;          // 当前是第几段连击（0=普攻1, 1=普攻2, ...）
    private bool  _comboWindowOpen;    // 攻击全程（含收刀）允许连击输入

    // 命中追踪（供 Animation Event 链使用：PerformHitDetection → PlayHitSfx / TriggerHitStop）
    private bool    _hasHitThisSwing;
    private Vector3 _lastHitPoint;

    public bool IsAttacking => attackTimer > 0f;
    /// <summary>连击窗口：攻击状态期间始终可接下一段（含收刀动作）</summary>
    public bool CanCombo => true;

    public void Initialize()
    {
        currentHealth = maxHealth;
    }

    // ─── 攻击计时 ──────────────────────────────────

    public void StartAttack()
    {
        attackTimer = attackDuration;
        forceExitTimer = 0f;
        _comboStep = 0;
    }

    /// <summary>连击：重置计时器，延长连击窗口，推进到下一段伤害。</summary>
    public void TryCombo()
    {
        if (attackTimer <= 0f) return;
        attackTimer = attackDuration;
        _comboStep++;
    }

    /// <summary>每帧递减攻击计时器（步骤 2）。仅在 IsAttacking 时调用。</summary>
    public void DecrementTimer(float deltaTime)
    {
        attackTimer -= deltaTime;
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

    /// <summary>攻击状态退出时清零所有计时器。</summary>
    public void ResetAllTimers()
    {
        attackTimer = 0f;
        forceExitTimer = 0f;
        _comboStep = 0;
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
            if (_comboDamages == null || _comboDamages.Length == 0) return 10f;  // fallback
            int idx = Mathf.Clamp(_comboStep, 0, _comboDamages.Length - 1);
            return _comboDamages[idx];
        }
    }

    public void PlaySfx(string prefix)
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayByPrefix(prefix, transform.position);
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

        Collider[] colliders = Physics.OverlapSphere(origin, attackRadius, enemyLayer);

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

    private System.Collections.IEnumerator HitStopRoutine()
    {
        float normal = Time.timeScale;
        Time.timeScale = _hitStopScale;
        yield return new WaitForSecondsRealtime(_hitStopDuration);
        Time.timeScale = normal;
    }

    public void TakeDamage(float damage)
    {
        if (isInvulnerable) return;
        currentHealth -= damage;
        OnPlayerDamaged?.Invoke(currentHealth);
        if (currentHealth <= 0) Debug.Log("玩家死亡！");
    }

    public void SetInvulnerable(bool value) => isInvulnerable = value;

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
            }
        }

        return hitAny;
    }
}