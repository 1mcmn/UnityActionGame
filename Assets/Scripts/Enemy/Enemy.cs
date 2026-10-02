using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Enemy : MonoBehaviour
{
    [Header("配置资产（可选，覆盖下方默认值）")]
    [SerializeField] private EnemyConfig _config;

    [Header("属性")]
    [SerializeField] private float _maxHealth = 100f;
    [SerializeField] private float _deathDelay = 1.5f;

    [Header("受击")]
    [SerializeField] private float _invulnerabilityDuration = 0.2f;
    [SerializeField] private float _knockbackForce = 5f;

    [Header("僵直条")]
    [SerializeField] private float _maxPoise = 100f;
    [SerializeField] private float _poiseDecayRate = 5f;
    [Range(0f, 1f)] [SerializeField] private float _damageReduction = 0.7f;

    [Header("UI 状态条")]
    [SerializeField] private GameObject _statusBarPrefab;
    [SerializeField] private Vector3 _statusBarOffset = new Vector3(0f, 2.5f, 0f);

    // 事件
    public event Action<float> OnHealthChanged;
    public event Action<float> OnHit;
    public event Action OnDeath;
    public event Action OnStaggered;    // 僵直到50%
    public event Action OnKnockdown;    // 僵直到100%
    public event Action<float> OnPoiseChanged;   // 僵直值变化

    // 血量
    private float _currentHealth;
    private bool _isDead;

    // 僵直
    private float _currentPoise;
    private bool _hasStaggered;

    // 无敌帧
    private float _invulnerabilityTimer;

    // 组件
    private Collider _collider;
    private Rigidbody _rigidbody;
    private EnemyStatusBar _statusBar;
    private GreatSwordEnemyBrain _swordBrain;
    private EnemyHurtbox[] _hurtboxes;
    public bool UsesDedicatedHurtbox => _hurtboxes != null && _hurtboxes.Length > 0;
    public bool AcceptsCollider(Collider candidate) => candidate != null &&
        (!UsesDedicatedHurtbox || candidate.GetComponent<EnemyHurtbox>() != null);

    // 公开属性（EnemyAI 会通过 GetComponent 自己拿 Animator / Rigidbody）

    // 公开属性
    public float CurrentHealth => _currentHealth;
    public float MaxHealth => _maxHealth;
    public float CurrentPoise => _currentPoise;
    public float MaxPoise => _maxPoise;
    public bool IsDead => _isDead;
    public bool IsStaggered => _currentPoise >= _maxPoise * 0.5f;
    public bool IsKnockedDown => _currentPoise >= _maxPoise;

    private void Awake()
    {
        // 配置资产优先：把资产值覆盖到实例字段（后续逻辑保持不变）
        if (_config != null)
        {
            _maxHealth = _config.maxHealth;
            _deathDelay = _config.deathDelay;
            _invulnerabilityDuration = _config.invulnerabilityDuration;
            _knockbackForce = _config.knockbackForce;
            _maxPoise = _config.maxPoise;
            _poiseDecayRate = _config.poiseDecayRate;
            _damageReduction = _config.damageReduction;
        }

        _currentHealth = _maxHealth;
        _currentPoise = 0f;
        _collider = GetComponent<Collider>();
        _rigidbody = GetComponent<Rigidbody>();
        _swordBrain = GetComponent<GreatSwordEnemyBrain>();
        _hurtboxes = GetComponentsInChildren<EnemyHurtbox>(true);

        // EnemyAI 自己处理根运动，不再禁用它

        if (_collider == null)
            _collider = gameObject.AddComponent<BoxCollider>();

        if (_rigidbody != null)
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Start()
    {
        // 敌人血条/僵直条改为屏幕空间 UI（BossHealthBarUI），由 GameManager 绑定，不再在头顶创建 World Space 状态条
    }

    private void Update()
    {
        // 无敌帧倒计时
        if (_invulnerabilityTimer > 0f)
            _invulnerabilityTimer -= Time.deltaTime;

        // 僵直条衰减（不受击时缓慢回复）
        if (_currentPoise > 0f && _invulnerabilityTimer <= 0f && (_swordBrain == null || !_swordBrain.IsBroken))
        {
            _currentPoise = Mathf.Max(0f, _currentPoise - _poiseDecayRate * Time.deltaTime);
            OnPoiseChanged?.Invoke(_currentPoise);
            if (_currentPoise < _maxPoise * 0.5f)
                _hasStaggered = false;
        }
    }

    // ==================== 工具方法 ====================

    /// <summary>攻击者是否在怪物前方（Dot > 0 = 前方）</summary>
    public bool IsAttackerInFront(Vector3 attackerPosition)
    {
        Vector3 toAttacker = (attackerPosition - transform.position).normalized;
        return Vector3.Dot(transform.forward, toAttacker) > 0f;
    }

    /// <summary>获得攻击方向枚举：Front / Back / Left / Right</summary>
    public HitDirection GetHitDirection(Vector3 attackerPosition)
    {
        Vector3 localDir = transform.InverseTransformDirection(
            (attackerPosition - transform.position).normalized);

        if (Mathf.Abs(localDir.x) > Mathf.Abs(localDir.z))
            return localDir.x > 0f ? HitDirection.Right : HitDirection.Left;
        else
            return localDir.z > 0f ? HitDirection.Front : HitDirection.Back;
    }

    // ==================== 受击 ====================

    public void TakeDamage(float damage, Vector3? hitDirection = null)
    {
        Vector3 direction = hitDirection ?? transform.forward;
        ReceiveHit(damage, damage, transform.position - direction.normalized, direction);
    }

    public EnemyHitResult ReceiveHit(float damage, float poiseDamage, Vector3 attackerPosition, Vector3 knockbackDirection)
    {
        if (_isDead || damage < 0 || !ComboData.Finite(damage) || !ComboData.Finite(poiseDamage)) return EnemyHitResult.Ignored;

        // 无敌帧（防止同一攻击多次判定）
        if (_invulnerabilityTimer > 0f) return EnemyHitResult.Ignored;
        _invulnerabilityTimer = _invulnerabilityDuration;

        if (_swordBrain != null && _swordBrain.IsConfigured && _swordBrain.CanGuard(attackerPosition))
        {
            AddPoise(poiseDamage * _swordBrain.Config.guardPoiseMultiplier);
            _swordBrain.OnGuarded();
            return EnemyHitResult.Blocked;
        }

        // 减伤：僵直条满之前伤害减免
        float actualDamage = (IsKnockedDown || (_swordBrain != null && _swordBrain.IsBroken)) ? damage : damage * (1f - _damageReduction);
        _currentHealth = Mathf.Max(0, _currentHealth - actualDamage);
        _isDead = _currentHealth <= 0;

        // 僵直值用原始伤害累加
        AddPoise(poiseDamage);

        GameLog.Log($"[Enemy] {name} 受到 {damage} 伤害(实际{actualDamage:F1}), " +
                  $"血量{_currentHealth}/{_maxHealth}, 僵直{_currentPoise}/{_maxPoise}");

        // 事件
        OnHealthChanged?.Invoke(_currentHealth);
        OnHit?.Invoke(damage);

        if (!_isDead && knockbackDirection.sqrMagnitude > .0001f) ApplyKnockback(knockbackDirection);
        if (_isDead) { OnDeath?.Invoke(); Die(); }
        return EnemyHitResult.Damaged;
    }

    public void AddPoise(float amount)
    {
        if (!ComboData.Finite(amount)) return;
        float oldPoise = _currentPoise;
        _currentPoise = Mathf.Clamp(_currentPoise + Mathf.Max(0, amount), 0, _maxPoise);
        OnPoiseChanged?.Invoke(_currentPoise);
        if (_isDead) return;
        // 僵直条事件（只触发一次）
        if (!_hasStaggered && _currentPoise >= _maxPoise * 0.5f && _currentPoise < _maxPoise)
        {
            _hasStaggered = true;
            OnStaggered?.Invoke();
        }

        if (oldPoise < _maxPoise && _currentPoise >= _maxPoise)
        {
            OnKnockdown?.Invoke();
        }

    }

    public void ResetPoise() { _currentPoise = 0; _hasStaggered = false; OnPoiseChanged?.Invoke(0); }

    private void ApplyKnockback(Vector3 direction)
    {
        if (_swordBrain != null && _swordBrain.IsConfigured) { _swordBrain.QueueKnockback(direction, _knockbackForce * .05f); return; }
        if (_rigidbody != null && !_isDead)
            _rigidbody.AddForce(direction.normalized * _knockbackForce, ForceMode.Impulse);
    }

    private void Die()
    {
        // 物理/碰撞清理，动画由 EnemyAI 接管（播放死亡动画后 Destroy）
        if (_collider != null) _collider.enabled = false;
        if (_hurtboxes != null) foreach (var hurt in _hurtboxes) if (hurt != null) hurt.GetComponent<Collider>().enabled = false;
        if (_rigidbody != null) _rigidbody.isKinematic = true;

        Destroy(gameObject, _deathDelay);
    }
}

/// <summary>受击方向</summary>
public enum HitDirection
{
    Front,
    Back,
    Left,
    Right
}

public enum EnemyHitResult { Ignored, Blocked, Damaged }
