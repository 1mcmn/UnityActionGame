using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCombat : MonoBehaviour
{
    [Header("攻击参数")]
    [SerializeField] private float attackDuration = 0.45f;
    [SerializeField] private float attackDamage = 20f;
    [SerializeField] private float attackRadius = 2.5f;
    [SerializeField] private LayerMask enemyLayer;

    public LayerMask EnemyLayer => enemyLayer;

    [Header("弹反")]
    [SerializeField] private float _parryRadius  = 2f;
    [SerializeField] private float _parryDamage  = 30f;    // 弹反僵直伤害
    [SerializeField] private float _parryWindow  = 0.3f;   // 弹反窗口（秒）

    [Header("生命值")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    private bool isInvulnerable;

    public static event Action<float> OnPlayerDamaged;
    public float CurrentHealth => currentHealth;

    private float attackTimer;
    private float forceExitTimer;

    public bool IsAttacking => attackTimer > 0f;
    public bool CanCombo => attackTimer > 0f;

    public void Initialize()
    {
        currentHealth = maxHealth;
    }

    // ─── 攻击计时 ──────────────────────────────────

    public void StartAttack()
    {
        attackTimer = attackDuration;
        forceExitTimer = 0f;
    }

    /// <summary>连击：重置计时器，延长连击窗口。调用前须通过 CanCombo 检查。</summary>
    public void TryCombo()
    {
        if (attackTimer <= 0f) return;
        attackTimer = attackDuration;
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
    }

    // ─── 伤害判定 ──────────────────────────────────

    public void PerformHitDetection(Vector3 origin)
    {
        Collider[] colliders = Physics.OverlapSphere(origin, attackRadius, enemyLayer);
        Debug.Log($"[Combat] 攻击判定 origin={origin}, radius={attackRadius}, 命中数={colliders.Length}");

        foreach (Collider col in colliders)
        {
            Enemy enemy = col.GetComponent<Enemy>();
            if (enemy != null)
            {
                Debug.Log($"[Combat] 攻击命中 {enemy.name}，造成 {attackDamage} 点伤害");
                enemy.TakeDamage(attackDamage);
            }
        }
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