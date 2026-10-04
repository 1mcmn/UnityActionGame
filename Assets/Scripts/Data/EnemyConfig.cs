using UnityEngine;

/// <summary>
/// 怪物配置资产。把检测/移动/攻击/属性等参数从 EnemyAI/Enemy 解耦，
/// 运行时由 EnemyAI / Enemy 读取；巡逻点的场景引用保存在 EnemyAI 上。
/// </summary>
[CreateAssetMenu(fileName = "EnemyConfig", menuName = "Combat/EnemyConfig")]
public class EnemyConfig : ScriptableObject
{
    [Header("大剑敌人（旧敌人保持关闭）")]
    public bool greatSwordEnabled;
    public AnimationClip idleClip, walkClip, runClip;
    public AnimationClip guardStartClip, guardLoopClip, guardHitClip, hitClip;
    public AnimationClip downStartClip, downLoopClip, getUpClip, deathClip, airborneClip;
    public AnimationClip attackPlaceholderA, attackPlaceholderB;
    public EnemyAttackDefinition lightAttack1 = new EnemyAttackDefinition();
    public EnemyAttackDefinition lightAttack2 = new EnemyAttackDefinition();
    public EnemyAttackDefinition heavyAttack = new EnemyAttackDefinition();
    public EnemyAttackDefinition counterAttack = new EnemyAttackDefinition();
    [Min(.1f)] public float guardDuration = 6f;
    [Range(10, 180)] public float guardHalfAngle = 65f;
    [Min(1)] public int guardCounterHits = 4;
    [Min(.1f)] public float guardCountTimeout = 2f;
    [Range(0, 1)] public float guardPoiseMultiplier = .7f;
    [Min(0)] public float parryPoiseDamage = 45f;
    [Min(.1f)] public float breakDuration = 3f;
    [Min(.1f)] public float ordinaryHitCooldown = .65f;
    [Header("破防浮空（每次破防仅一次）")]
    [Min(.1f)] public float launchHeight = 2.2f;
    [Min(.1f)] public float airGravity = 10f;
    [Min(0)] public float apexHoldTime = .65f;
    [Min(.1f)] public float maximumAirDuration = 3f;
    [Min(.1f)] public float slamSpeed = 8f;
    [Tooltip("空中被追击命中时的受击动画；为空时回退为重播浮空动画")]
    public AnimationClip airHitClip;
    [Tooltip("空中受击时向上补的速度（米/秒），制造追打感")]
    [Min(0)] public float airHitBump = 1.5f;
    [Tooltip("浮空高度按体型缩放的开方倍率放大，放大的敌人不会显得飞不高")]
    public bool scaleLaunchWithBody = true;

    [Header("倒地压制（破防后不挑飞时）")]
    [Tooltip("倒地期间被追打的受击片段；为空时只闪白，不播动作")]
    public AnimationClip downHitClip;
    [Tooltip("倒地期间所受伤害倍率")]
    [Min(1)] public float downDamageMultiplier = 1.25f;
    [Tooltip("每次倒地追打延长的压制时间（秒）")]
    [Min(0)] public float downHitExtend = .35f;
    [Tooltip("单次破防内追打最多累计延长的时间（秒）")]
    [Min(0)] public float downExtendCap = 2f;

    [Header("检测范围")]
    public float detectRadius  = 10f;
    public float closeRadius   = 4f;
    public float attackRadius  = 2.5f;
    public float attack06Radius = 2f;
    [Min(1.1f)] public float loseTargetMultiplier = 1.5f;

    [Header("巡逻（无巡逻点时围绕出生位置）")]
    public bool patrolEnabled = true;
    [Min(0.1f)] public float patrolRadius = 4f;
    [Min(0f)] public float patrolWaitTime = 1.5f;
    [Min(0.1f)] public float patrolArrivalDistance = 0.35f;

    [Header("移动速度")]
    public float walkSpeed     = 1.5f;
    public float runSpeed      = 4.5f;
    public float rotationSpeed = 8f;

    [Header("攻击间隔")]
    public float comboWindow    = 0.6f;
    public float attackCooldown = 1.5f;
    [Range(0, 1)] public float attack06Chance = 0.3f;

    [Header("攻击判定")]
    [Min(0f)] public float attackDamage = 12f;
    [Min(0f)] public float hitDelay = 0.25f;

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

    public System.Collections.Generic.List<string> ValidateGreatSword()
    {
        var errors = new System.Collections.Generic.List<string>();
        if (!greatSwordEnabled) errors.Add("大剑模式未开启。");
        foreach (var clip in new[] { idleClip, walkClip, runClip, guardStartClip, guardLoopClip, guardHitClip, hitClip, downStartClip, downLoopClip, getUpClip, deathClip, airborneClip, attackPlaceholderA, attackPlaceholderB })
            if (clip == null || clip.legacy || !clip.isHumanMotion) errors.Add("状态动画缺失或不是 Humanoid 动画。");
        foreach (var clip in new[] { idleClip, walkClip, runClip, guardLoopClip, downLoopClip })
            if (clip != null && !clip.isLooping) errors.Add(clip.name + " 应为循环动画。");
        foreach (var attack in new[] { lightAttack1, lightAttack2, heavyAttack, counterAttack })
            if (attack == null || !attack.Valid) errors.Add("攻击片段、倍速或窗口配置非法。");
        foreach (float value in new[] { maxHealth, maxPoise, attackRadius, detectRadius, walkSpeed, runSpeed, launchHeight, airGravity, maximumAirDuration, breakDuration, guardDuration })
            if (!ComboData.Finite(value) || value <= 0) errors.Add("血量、范围、速度与时长必须为正有限数。");
        if (guardCounterHits < 1 || !ComboData.Finite(guardHalfAngle) || guardHalfAngle < 10 || guardHalfAngle > 180) errors.Add("格挡角度或计数非法。");
        return errors;
    }
}

[System.Serializable]
public class EnemyAttackDefinition
{
    public AnimationClip clip;
    [Range(.1f, 5f)] public float speed = 1f;
    [Min(0)] public float damage = 12f;
    public float hitStart = .3f, hitEnd = .5f;
    public float lockFacingTime = .25f;
    [Min(0)] public float rootMotionScale = .6f;
    [Min(.01f)] public float hitRadius = .55f;
    public Vector3 hitOffset = new Vector3(0, .9f, 1.1f);
    public bool parryable, superArmor;
    public float parryStart = .1f, parryEnd = .45f;
    public EnemyAttackDefinition Copy() => (EnemyAttackDefinition)MemberwiseClone();
    public bool Valid => clip != null && !clip.legacy && clip.isHumanMotion && !clip.isLooping && ComboData.ValidWindow(hitStart, hitEnd, clip.length)
        && ComboData.Finite(speed) && speed >= .1f && speed <= 5f && ComboData.Finite(damage) && damage >= 0
        && ComboData.Finite(hitRadius) && hitRadius > 0 && ComboData.Finite(rootMotionScale) && rootMotionScale >= 0
        && ComboData.Finite(hitOffset.x) && ComboData.Finite(hitOffset.y) && ComboData.Finite(hitOffset.z)
        && ComboData.Finite(lockFacingTime) && lockFacingTime >= 0 && lockFacingTime <= hitStart
        && (!parryable || ComboData.ValidWindow(parryStart, parryEnd, clip.length));
}
