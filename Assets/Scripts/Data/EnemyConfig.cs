using UnityEngine;

/// <summary>
/// 怪物配置资产。把检测/移动/攻击/属性等参数从 EnemyAI/Enemy 解耦，
/// 供怪物生成器（Enemy Spawner）批量配置，运行时读取。
/// </summary>
[CreateAssetMenu(fileName = "EnemyConfig", menuName = "Combat/EnemyConfig")]
public class EnemyConfig : ScriptableObject
{
    [Header("检测范围")]
    public float detectRadius  = 10f;
    public float closeRadius   = 4f;
    public float attackRadius  = 2.5f;
    public float attack06Radius = 2f;

    [Header("移动速度")]
    public float walkSpeed     = 1.5f;
    public float runSpeed      = 4.5f;
    public float rotationSpeed = 8f;

    [Header("攻击间隔")]
    public float comboWindow    = 0.6f;
    public float attackCooldown = 1.5f;
    [Range(0, 1)] public float attack06Chance = 0.3f;

    [Header("眩晕")]
    public float stunDuration = 3f;

    [Header("动画平滑")]
    public float speedLerpRate = 5f;

    [Header("属性")]
    public float maxHealth       = 100f;
    public float maxPoise        = 100f;
    public float poiseDecayRate  = 5f;
    [Range(0, 1)] public float damageReduction = 0.7f;
    public float knockbackForce   = 5f;
    public float invulnerabilityDuration = 0.2f;
    public float deathDelay = 1.5f;
}
