using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Enemy : MonoBehaviour
{
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
        _currentHealth = _maxHealth;
        _currentPoise = 0f;
        _collider = GetComponent<Collider>();
        _rigidbody = GetComponent<Rigidbody>();

        // root motion 在 Animator 上禁用（EnemyAI 自己做移动）
        var animator = GetComponent<Animator>();
        if (animator != null)
            animator.applyRootMotion = false;

        if (_collider == null)
            _collider = gameObject.AddComponent<BoxCollider>();

        if (_rigidbody != null)
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Start()
    {
        if (_statusBarPrefab != null)
        {
            Vector3 worldPos = transform.position + _statusBarOffset;
            GameObject barObj = Instantiate(_statusBarPrefab, worldPos, Quaternion.identity);
            _statusBar = barObj.GetComponent<EnemyStatusBar>();
            if (_statusBar != null)
            {
                _statusBar.Bind(this, _statusBarOffset);
                Debug.Log($"[Enemy] {name} 状态条创建成功");
            }
            else
            {
                Debug.LogError($"[Enemy] 预制体上没有 EnemyStatusBar 脚本！预制体名: {_statusBarPrefab.name}");
            }
        }
        else
        {
            Debug.LogError($"[Enemy] {name} 的 _statusBarPrefab 未赋值！请在 Inspector 拖入状态条预制体。");
        }
    }

    private void Update()
    {
        // 无敌帧倒计时
        if (_invulnerabilityTimer > 0f)
            _invulnerabilityTimer -= Time.deltaTime;

        // 僵直条衰减（不受击时缓慢回复）
        if (_currentPoise > 0f && _invulnerabilityTimer <= 0f)
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
        if (_isDead) return;

        // 无敌帧（防止同一攻击多次判定）
        if (_invulnerabilityTimer > 0f) return;
        _invulnerabilityTimer = _invulnerabilityDuration;

        // 减伤：僵直条满之前伤害减免
        float actualDamage = IsKnockedDown ? damage : damage * (1f - _damageReduction);
        _currentHealth -= actualDamage;

        // 僵直值用原始伤害累加
        float oldPoise = _currentPoise;
        _currentPoise = Mathf.Min(_currentPoise + damage, _maxPoise);
        OnPoiseChanged?.Invoke(_currentPoise);

        Debug.Log($"[Enemy] {name} 受到 {damage} 伤害(实际{actualDamage:F1}), " +
                  $"血量{_currentHealth}/{_maxHealth}, 僵直{_currentPoise}/{_maxPoise}");

        // 事件
        OnHealthChanged?.Invoke(_currentHealth);
        OnHit?.Invoke(damage);

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

        // 击退
        if (hitDirection.HasValue && hitDirection.Value != Vector3.zero)
            ApplyKnockback(hitDirection.Value);

        // 死亡
        if (_currentHealth <= 0f)
        {
            _currentHealth = 0f;
            _isDead = true;
            Debug.Log($"[Enemy] {name} 死亡！");
            OnDeath?.Invoke();
            Die();
        }
    }

    private void ApplyKnockback(Vector3 direction)
    {
        if (_rigidbody != null && !_isDead)
            _rigidbody.AddForce(direction.normalized * _knockbackForce, ForceMode.Impulse);
    }

    private void Die()
    {
        // 物理/碰撞清理，动画由 EnemyAI 接管（播放死亡动画后 Destroy）
        if (_collider != null) _collider.enabled = false;
        if (_rigidbody != null) _rigidbody.isKinematic = true;

        Destroy(gameObject, _deathDelay);
    }

    private void OnDestroy()
    {
        if (_statusBar != null)
        {
            _statusBar.Unbind();
            Destroy(_statusBar.gameObject);
        }
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