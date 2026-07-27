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

    [Header("血条")]
    [SerializeField] private GameObject _healthBarPrefab;
    [SerializeField] private Vector3 _healthBarOffset = new Vector3(0f, 2.5f, 0f);

    public event Action<float> OnHealthChanged;
    public event Action<float> OnHit;
    public event Action OnDeath;

    private float _currentHealth;
    private float _invulnerabilityTimer;
    private bool _isDead;

    private Collider _collider;
    private Rigidbody _rigidbody;
    private Animator _animator;
    private EnemyHealthBar _healthBar;

    private static readonly int HitParam = Animator.StringToHash("Hit");
    private static readonly int IsDeadParam = Animator.StringToHash("IsDead");

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => _maxHealth;
    public bool IsDead => _isDead;

    private void Awake()
    {
        _currentHealth = _maxHealth;
        _collider = GetComponent<Collider>();
        _rigidbody = GetComponent<Rigidbody>();
        _animator = GetComponent<Animator>();

        // 如果没挂碰撞体，自动加一个（防呆）
        if (_collider == null)
        {
            _collider = gameObject.AddComponent<BoxCollider>();
        }

        // 👑【核心修复】强制锁定刚体的旋转，防止怪物受力后在地上翻滚
        if (_rigidbody != null)
        {
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void Start()
    {
        if (_healthBarPrefab != null)
        {
            Vector3 worldPos = transform.position + _healthBarOffset;
            GameObject barObj = Instantiate(_healthBarPrefab, worldPos, Quaternion.identity);
            _healthBar = barObj.GetComponent<EnemyHealthBar>();
            if (_healthBar != null)
            {
                _healthBar.Bind(this, _healthBarOffset);
                Debug.Log($"[Enemy] {name} 血条创建成功");
            }
            else
            {
                Debug.LogError($"[Enemy] 预制体上没有 EnemyHealthBar 脚本！预制体名: {_healthBarPrefab.name}");
            }
        }
        else
        {
            Debug.LogError($"[Enemy] {name} 的 _healthBarPrefab 未赋值！请在 Inspector 拖入血条预制体。");
        }
    }

    private void Update()
    {
        if (_invulnerabilityTimer > 0f)
        {
            _invulnerabilityTimer -= Time.deltaTime;
        }
    }

    /// <summary>受击入口。hitDirection 为击退方向（可选）。</summary>
    public void TakeDamage(float damage, Vector3? hitDirection = null)
    {
        if (_isDead || _invulnerabilityTimer > 0f) return;

        _currentHealth -= damage;
        _invulnerabilityTimer = _invulnerabilityDuration;
        Debug.Log($"[Enemy] {name} 受到 {damage} 点伤害，剩余血量 {_currentHealth}/{_maxHealth}");

        OnHealthChanged?.Invoke(_currentHealth);
        OnHit?.Invoke(damage);

        if (hitDirection.HasValue && hitDirection.Value != Vector3.zero)
        {
            ApplyKnockback(hitDirection.Value);
        }

        if (_animator != null)
        {
            _animator.SetTrigger(HitParam);
        }

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
       
        if (_rigidbody != null && !_isDead) // 👑 防止死后继续飞
        {
            _rigidbody.AddForce(direction.normalized * _knockbackForce, ForceMode.Impulse);
        }
    }

    private void Die()
    {
        if (_collider != null) _collider.enabled = false;
        if (_rigidbody != null) _rigidbody.isKinematic = true;

        if (_animator != null)
        {
            _animator.SetBool(IsDeadParam, true);
        }

        Destroy(gameObject, _deathDelay);
    }

    private void OnDestroy()
    {
        if (_healthBar != null)
        {
            _healthBar.Unbind();
            Destroy(_healthBar.gameObject);
        }
    }
}