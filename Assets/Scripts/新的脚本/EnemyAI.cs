using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 怪物 AI 状态机。接管所有行为决策和动画切换。
/// Animator Controller 只需把所有动画 clip 拖成独立 state，不需要连线；
/// 代码通过 CrossFadeInFixedTime 直接切换动画。
/// </summary>
[RequireComponent(typeof(Enemy))]
public class EnemyAI : MonoBehaviour
{
    // ==================== 检测范围 ====================

    [Header("检测范围")]
    [SerializeField] private float _detectRadius = 10f;  // 发现玩家距离
    [SerializeField] private float _closeRadius  = 4f;   // 近距离（可 walk→run 切换）
    [SerializeField] private float _attackRadius = 2.5f;  // 可攻击距离
    [SerializeField] private float _attack06Radius = 2f;  // 06 嚎叫有效距离

    [Header("移动速度")]
    [SerializeField] private float _walkSpeed     = 1.5f;
    [SerializeField] private float _runSpeed      = 4.5f;
    [SerializeField] private float _rotationSpeed = 8f;

    [Header("攻击间隔")]
    [SerializeField] private float _comboWindow    = 0.6f; // 连段递进窗口
    [SerializeField] private float _attackCooldown = 1.5f; // 一套连段打完后的冷却
    [Range(0, 1)] [SerializeField] private float _attack06Chance = 0.3f;

    [Header("眩晕")]
    [SerializeField] private float _stunDuration = 3f;

    [Header("动画平滑")]
    [SerializeField] private float _speedLerpRate = 5f; // Speed 参数平滑过渡速度

    // ==================== 组件 ====================

    private Enemy _enemy;
    private Animator _animator;
    private Rigidbody _rigidbody;
    private Transform _player;

    // ==================== 状态机 ====================

    private EnemyState _state;
    private int _attackStep;          // 攻击连段序号 1~7
    private float _stateTimer;        // 当前状态计时器
    private float _comboTimer;        // 连段窗口倒计时（动画播完后才开始）
    private float _attackCooldownTimer; // 攻击冷却
    private float _stunTimer;         // 眩晕倒计时
    private bool _inComboWindow;      // 是否处于动画播完后的连段窗口

    // 动画参数平滑
    private float _targetAnimSpeed;   // 目标 Speed 值
    private float _currentAnimSpeed;  // 当前实际 Speed 值（逐帧 lerp）

    private Vector3 _lastAttackerDir; // 记录最后一次受击/弹反方向

    // ==================== 动画 Hash ====================

    private static readonly int StateIDHash   = Animator.StringToHash("StateID");
    private static readonly int AttackStepHash = Animator.StringToHash("AttackStep");
    private static readonly int HitTypeHash    = Animator.StringToHash("HitType");
    private static readonly int DeathDirHash   = Animator.StringToHash("DeathDir");
    private static readonly int SpeedHash      = Animator.StringToHash("Speed");

    private int _locomotionHash;    // blend tree 默认状态的 hash，用于从过渡动画切回

    // ==================== Unity 生命周期 ====================

    private void Awake()
    {
        _enemy     = GetComponent<Enemy>();
        _animator  = GetComponent<Animator>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        _player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (_player == null)
        {
            Debug.LogError($"[EnemyAI] {name} 未找到 Player！请确认角色 Tag 设为 'Player'");
        }
        else
        {
            Debug.Log($"[EnemyAI] {name} 找到 Player: {_player.name}");
        }

        if (_animator == null)
            Debug.LogError($"[EnemyAI] {name} 没有 Animator 组件！");
        else if (_animator.runtimeAnimatorController == null)
            Debug.LogError($"[EnemyAI] {name} Animator 没有 Controller！");
        else
            Debug.Log($"[EnemyAI] {name} Animator OK, clip 数量: {_animator.runtimeAnimatorController.animationClips.Length}");

        // 订阅 Enemy 事件
        _enemy.OnStaggered += OnStaggered;
        _enemy.OnKnockdown += OnKnockdown;
        _enemy.OnDeath     += OnDeath;
        _enemy.OnHit       += OnHit;

        // 记录 blend tree 默认状态的 hash，后续从 RunStart 等过渡动画切回时用
        _locomotionHash = _animator.GetCurrentAnimatorStateInfo(0).fullPathHash;

        // 开局播出生动画
        ChangeState(EnemyState.Spawn);
    }

    private void Update()
    {
        if (_enemy.IsDead) return;

        // 全局计时器
        _stateTimer           -= Time.deltaTime;
        _attackCooldownTimer  -= Time.deltaTime;

        // 平滑过渡 Animator Speed 参数（避免 blend tree 直接跳跃）
        _currentAnimSpeed = Mathf.MoveTowards(_currentAnimSpeed, _targetAnimSpeed, _speedLerpRate * Time.deltaTime);
        _animator.SetFloat(SpeedHash, _currentAnimSpeed);

        StateUpdate();
    }

    // ==================== Root Motion 回写 ====================

    /// <summary>
    /// 即使 applyRootMotion = false，deltaPosition 仍然被 Animator 计算。
    /// 在动画主导的状态中，手动把 root bone 位移写到 Rigidbody，
    /// 确保 Mesh 和 Collider 同步移动，不会 "脱离刚体"。
    /// </summary>
    private void OnAnimatorMove()
    {
        if (_animator == null || _rigidbody == null) return;
        if (_enemy.IsDead) return;

        // 只在动画主导状态应用 root motion，避免和 velocity 移动双重位移
        bool isAnimDriven;
        switch (_state)
        {
            case EnemyState.Attack:
            case EnemyState.Spawn:
            case EnemyState.Staggered:
            case EnemyState.StunStart:
            case EnemyState.StunLoop:
            case EnemyState.StunEnd:
            case EnemyState.StunHit:
            case EnemyState.Death:
                isAnimDriven = true;
                break;
            default:
                isAnimDriven = false;
                break;
        }

        if (!isAnimDriven) return;

        // 把 Animator 算出的 root bone 位移同步到 Rigidbody（带动 Collider）
        _rigidbody.MovePosition(_rigidbody.position + _animator.deltaPosition);

        // 同时同步旋转
        Quaternion deltaRot = _animator.deltaRotation;
        if (deltaRot != Quaternion.identity)
            _rigidbody.MoveRotation(_rigidbody.rotation * deltaRot);
    }

    // ==================== 

    private void OnDestroy()
    {
        if (_enemy != null)
        {
            _enemy.OnStaggered -= OnStaggered;
            _enemy.OnKnockdown -= OnKnockdown;
            _enemy.OnDeath     -= OnDeath;
            _enemy.OnHit       -= OnHit;
        }
    }

    // ==================== 状态机主循环 ====================

    private void StateUpdate()
    {
        float dist = DistanceToPlayer();

        switch (_state)
        {
            case EnemyState.Spawn:
                if (_stateTimer <= 0f) ChangeState(EnemyState.Idle);
                break;

            case EnemyState.Idle:
                if (_player == null) break;
                if (dist <= _attackRadius)
                {
                    // 玩家已经在攻击范围内 → 冷却好了直接打，没好就原地等
                    if (_attackCooldownTimer <= 0f)
                        ChangeState(EnemyState.Attack);
                }
                else if (dist < _closeRadius)
                    ChangeState(EnemyState.RunStart);
                else if (dist < _detectRadius)
                    ChangeState(EnemyState.Walk);
                // 超过检测范围时输出距离（每秒一次，避免刷屏）
                else if (Time.frameCount % 60 == 0)
                    Debug.Log($"[EnemyAI] {name} Idle: 玩家距离={dist:F1}m, 检测范围={_detectRadius}m");
                break;

            case EnemyState.Walk:
                if (_player == null) { ChangeState(EnemyState.Idle); break; }
                FacePlayer();
                MoveTowardPlayer(_walkSpeed);
                if (dist <= _attackRadius && _attackCooldownTimer <= 0f)
                    ChangeState(EnemyState.Attack);
                else if (dist < _closeRadius)
                    ChangeState(EnemyState.RunStart);
                else if (dist > _detectRadius * 1.2f)
                    ChangeState(EnemyState.Idle);
                break;

            case EnemyState.RunStart:
                if (_stateTimer <= 0f) ChangeState(EnemyState.Run);
                break;

            case EnemyState.Run:
                if (_player == null) { ChangeState(EnemyState.Idle); break; }
                FacePlayer();
                MoveTowardPlayer(_runSpeed);
                if (dist <= _attackRadius)
                    ChangeState(EnemyState.RunEnd);
                break;

            case EnemyState.RunEnd:
                if (_stateTimer <= 0f)
                    ChangeState(EnemyState.Attack);
                break;

            case EnemyState.Attack:
                FacePlayer();
                AttackUpdate(dist);
                break;

            case EnemyState.Staggered:
                if (_stateTimer <= 0f)
                {
                    // 僵直动画播完，回到检测循环
                    ChangeState(EnemyState.Idle);
                }
                break;

            case EnemyState.StunStart:
                if (_stateTimer <= 0f)
                {
                    _stunTimer = _stunDuration;
                    ChangeState(EnemyState.StunLoop);
                }
                break;

            case EnemyState.StunLoop:
                _stunTimer -= Time.deltaTime;
                if (_stunTimer <= 0f)
                    ChangeState(EnemyState.StunEnd);
                break;

            case EnemyState.StunEnd:
                if (_stateTimer <= 0f)
                    ChangeState(EnemyState.Idle);
                break;

            case EnemyState.StunHit:
                if (_stateTimer <= 0f)
                {
                    _stunTimer = Mathf.Max(_stunTimer, 0.3f); // 至少再晕 0.3s
                    ChangeState(EnemyState.StunLoop);
                }
                break;

            case EnemyState.Death:
                // 死亡状态什么也不做，等 Destroy
                break;
        }
    }

    // ==================== 攻击子系统 ====================

    private void AttackUpdate(float dist)
    {
        // 动画还没播完 → 什么都不做，等动画结束
        if (_stateTimer > 0f) return;

        // 动画播完第一帧 → 打开连段窗口，开始倒计时
        if (!_inComboWindow)
        {
            _inComboWindow = true;
            _comboTimer = _comboWindow;
        }

        _comboTimer -= Time.deltaTime;

        // 连段窗口关闭 → 连段结束
        if (_comboTimer <= 0f)
        {
            EndCombo();
            return;
        }

        // 窗口内 → 推进到下一段攻击
        AdvanceCombo(dist);
    }

    /// <summary>推进攻击连段（根据当前 step 播放下一段）</summary>
    private void AdvanceCombo(float dist)
    {
        _attackStep++;
        _inComboWindow = false;   // 关闭窗口，等新动画播完后再开

        switch (_attackStep)
        {
            case 2:
                CrossFade("Goblin_Ani_Attack_02", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_02");
                break;
            case 3:
                CrossFade("Goblin_Ani_Attack_03", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_03");
                break;
            case 4:
                // 04 或 04_01 多段拆分
                bool useSplit = UnityEngine.Random.value > 0.5f;
                string clip04 = useSplit ? "Goblin_Ani_Attack_04_01" : "Goblin_Ani_Attack_04";
                CrossFade(clip04, 0.1f);
                _stateTimer = ClipLength(clip04);
                break;
            case 5:
                // 05 冲击：先播起跑
                CrossFade("Goblin_Ani_Attack_05_Start", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_05_Start");
                StartCoroutine(Attack05Routine());
                break;
            case 6:
                // 06 嚎叫（近距离概率触发）
                if (dist < _attack06Radius && UnityEngine.Random.value < _attack06Chance)
                {
                    CrossFade("Goblin_Ani_Attack_06", 0.1f);
                    _stateTimer = ClipLength("Goblin_Ani_Attack_06");
                }
                else
                {
                    // 跳过 06，直接进 07
                    _attackStep = 7;
                    CrossFade("Goblin_Ani_Attack_07", 0.1f);
                    _stateTimer = ClipLength("Goblin_Ani_Attack_07");
                }
                break;
            case 7:
                CrossFade("Goblin_Ani_Attack_07", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_07");
                break;
            default:
                EndCombo();
                break;
        }
    }

    /// <summary>05 冲击：起跑 → 撞到 / 未撞到 → 完整</summary>
    private IEnumerator Attack05Routine()
    {
        // 等待起跑动画播完
        yield return new WaitForSeconds(ClipLength("Goblin_Ani_Attack_05_Start"));

        float dist = DistanceToPlayer();
        bool hit = dist < _attackRadius * 1.5f;

        if (hit)
        {
            CrossFade("Goblin_Ani_Attack_05", 0.1f);
            yield return new WaitForSeconds(ClipLength("Goblin_Ani_Attack_05"));
            CrossFade("Goblin_Ani_Attack_05_Full", 0.1f);
            _stateTimer = ClipLength("Goblin_Ani_Attack_05_Full");
        }
        else
        {
            CrossFade("Goblin_Ani_Attack_05_Miss_2", 0.1f);
            _stateTimer = ClipLength("Goblin_Ani_Attack_05_Miss_2");
        }
    }

    /// <summary>结束连段，回到 Idle</summary>
    private void EndCombo()
    {
        _attackStep = 0;
        _inComboWindow = false;
        _attackCooldownTimer = _attackCooldown;
        ChangeState(EnemyState.Idle);
    }

    // ==================== 状态切换 ====================

    private void ChangeState(EnemyState newState)
    {
        // 死亡后不允许切到其他状态
        if (_state == EnemyState.Death && newState != EnemyState.Death) return;

        Debug.Log($"[EnemyAI] {name} 状态切换: {_state} → {newState}");

        ExitState(_state);
        _state = newState;
        _stateTimer = 0f;
        EnterState(_state);
    }

    private void EnterState(EnemyState st)
    {
        switch (st)
        {
            case EnemyState.Spawn:
                CrossFade("Goblin_Ani_Born", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Born");
                break;

            case EnemyState.Idle:
                _targetAnimSpeed = 0f;
                EnsureKinematicOff();
                // 不 CrossFade("Idle")，用 Speed=0 让 Blend Tree 自动回 Idle
                break;

            case EnemyState.Walk:
                _targetAnimSpeed = 0.5f;
                EnsureKinematicOff();
                // Walk 用 blend tree 或直接 CrossFade "Walk"
                break;

            case EnemyState.RunStart:
                _targetAnimSpeed = 1f;
                EnsureKinematicOff();
                CrossFade("Goblin_Ani_Run_Start", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Run_Start");
                break;

            case EnemyState.Run:
                _targetAnimSpeed = 1f;
                EnsureKinematicOff();
                // 从 RunStart 的过渡动画切回 blend tree，让 blend tree 在 Speed=1 播 Run 循环
                CrossFade(_locomotionHash, 0.05f);
                break;

            case EnemyState.RunEnd:
                _targetAnimSpeed = 0f;
                EnsureKinematicOff();
                CrossFade("Goblin_Ani_Run_End", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Run_End");
                break;

            case EnemyState.Attack:
                _attackStep = 0;
                _inComboWindow = false;
                // 冻结刚体，防止攻击动画的 root bone 位移导致 mesh 与碰撞体分离
                if (_rigidbody != null)
                {
                    _rigidbody.velocity = Vector3.zero;
                    _rigidbody.isKinematic = true;
                }
                CrossFade("Goblin_Ani_Attack_01", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_01");
                break;

            case EnemyState.Staggered:
                // 根据方向选动画（有轻重区分？先用轻受击）
                PlayHitReaction(false); // false = 不是重击
                break;

            case EnemyState.StunStart:
                CrossFade("Goblin_Ani_Debuff_Stun_Start", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Debuff_Stun_Start");
                break;

            case EnemyState.StunLoop:
                CrossFade("Goblin_Ani_Debuff_Stun_Loop", 0.1f);
                break;

            case EnemyState.StunEnd:
                CrossFade("Goblin_Ani_Debuff_Stun_End", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Debuff_Stun_End");
                break;

            case EnemyState.StunHit:
                PlayStunHitReaction();
                break;

            case EnemyState.Death:
                PlayDeathAnimation();
                break;
        }
    }

    private void ExitState(EnemyState st)
    {
        // 退出动画主导状态时恢复刚体物理
        if ((st == EnemyState.Attack || st == EnemyState.Staggered || st == EnemyState.StunStart || st == EnemyState.StunLoop || st == EnemyState.StunHit) && _rigidbody != null)
        {
            _rigidbody.isKinematic = false;
            Debug.Log($"[EnemyAI] {name} 恢复刚体物理 (isKinematic=false)");
        }
    }

    /// <summary>确保刚体处于非 kinematic 状态（移动状态需要）</summary>
    private void EnsureKinematicOff()
    {
        if (_rigidbody != null && _rigidbody.isKinematic)
        {
            _rigidbody.isKinematic = false;
            Debug.Log($"[EnemyAI] {name} 强制恢复 isKinematic=false（移动状态前检查）");
        }
    }

    // ==================== 事件处理 ====================

    private void OnStaggered()
    {
        Debug.Log($"[EnemyAI] {name} OnStaggered 触发！当前状态={_state}, poise={_enemy.CurrentPoise}/{_enemy.MaxPoise}");

        // 僵直条 ≥ 50% → 播放受击动画
        if (_state == EnemyState.Attack || _state == EnemyState.Walk || 
            _state == EnemyState.Run || _state == EnemyState.Idle ||
            _state == EnemyState.RunStart || _state == EnemyState.RunEnd)
        {
            ChangeState(EnemyState.Staggered);
        }
    }

    private void OnKnockdown()
    {
        Debug.Log($"[EnemyAI] {name} OnKnockdown 触发！当前状态={_state}");
        if (_state != EnemyState.Death)
            ChangeState(EnemyState.StunStart);
    }

    private void OnDeath()
    {
        Debug.Log($"[EnemyAI] {name} OnDeath 触发！");
        ChangeState(EnemyState.Death);
    }

    private void OnHit(float damage)
    {
        // 尝试从 Player 获取方向；如果 _player 为空则用最后一个已知方向
        if (_player != null)
            _lastAttackerDir = _player.position - transform.position;
        else
            _lastAttackerDir = transform.forward; // fallback，保持正面
        Debug.Log($"[EnemyAI] {name} 受击 damage={damage}, poise={_enemy.CurrentPoise}/{_enemy.MaxPoise}");
    }

    /// <summary>从外部调用：被弹反成功</summary>
    public void OnParried(Vector3 parrierPosition)
    {
        if (_enemy.IsDead) return;

        _lastAttackerDir = parrierPosition - transform.position;

        // 弹反 = 大量僵直伤害 + 受击动画
        // Enemy.TakeDamage 会处理僵直累加和事件广播
        _enemy.TakeDamage(30f, -_lastAttackerDir.normalized);

        if (_state == EnemyState.Attack)
        {
            // 攻击被弹反，打断连段
            ChangeState(EnemyState.Staggered);
        }
    }

    // ==================== 动画工具方法 ====================

    /// <summary>根据 _lastAttackerDir 播放受击动画</summary>
    private void PlayHitReaction(bool isHeavy)
    {
        bool fromFront = _enemy.IsAttackerInFront(transform.position + _lastAttackerDir);

        string clip;
        if (isHeavy)
        {
            clip = "Goblin_Ani_Hit_Stay"; // 重型受击 = 硬直停留
        }
        else
        {
            clip = fromFront ? "Goblin_Ani_Hit_L_Front" : "Goblin_Ani_Hit_L_Back";
        }

        CrossFade(clip, 0.05f);
        _stateTimer = ClipLength(clip);
    }

    /// <summary>眩晕期间受击动画</summary>
    private void PlayStunHitReaction()
    {
        bool fromFront = _enemy.IsAttackerInFront(transform.position + _lastAttackerDir);

        // 区分轻重：用随机 / 伤害值判断（这里简化：50% 概率重击）
        bool isHeavy = UnityEngine.Random.value > 0.5f;

        string clip;
        if (isHeavy)
            clip = fromFront ? "Goblin_Ani_Stun_Hit_H_Front" : "Goblin_Ani_Stun_Hit_H_Back";
        else
            clip = fromFront ? "Goblin_Ani_Stun_Hit_L_Front" : "Goblin_Ani_Stun_Hit_L_Back";

        CrossFade(clip, 0.05f);
        _stateTimer = ClipLength(clip);
    }

    /// <summary>播放死亡动画</summary>
    private void PlayDeathAnimation()
    {
        bool fromFront = _enemy.IsAttackerInFront(transform.position + _lastAttackerDir);
        string clip = fromFront ? "Goblin_Ani_Death_Hit_Front" : "Goblin_Ani_Death_Hit_Back";
        CrossFade(clip, 0.1f);
    }

    private void CrossFade(string clipName, float duration)
    {
        if (_animator == null || string.IsNullOrEmpty(clipName))
        {
            Debug.LogWarning($"[EnemyAI] {name} CrossFade 跳过: animator={_animator != null}, clip={clipName}");
            return;
        }

        // 检查 clip 是否存在于 Controller 中
        bool clipExists = false;
        if (_animator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip c in _animator.runtimeAnimatorController.animationClips)
            {
                if (c.name == clipName) { clipExists = true; break; }
            }
        }

        if (!clipExists)
            Debug.LogWarning($"[EnemyAI] {name} CrossFade 失败: clip '{clipName}' 不在 Controller 中！请检查 Animator 里是否有同名的 state");

        _animator.CrossFadeInFixedTime(clipName, duration);
    }

    /// <summary>通过 state hash 切换动画（用于切回 blend tree 等默认状态）</summary>
    private void CrossFade(int stateHash, float duration)
    {
        if (_animator == null) return;
        _animator.CrossFadeInFixedTime(stateHash, duration);
    }

    /// <summary>获取动画 clip 时长（秒）</summary>
    private float ClipLength(string clipName)
    {
        if (_animator == null) return 0.5f;
        if (_animator.runtimeAnimatorController == null) return 0.5f;

        foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == clipName)
                return clip.length;
        }
        return 0.5f; // fallback
    }

    // ==================== 移动 ====================

    private void FacePlayer()
    {
        if (_player == null) return;
        Vector3 dir = (_player.position - transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
        }
    }

    private void MoveTowardPlayer(float speed)
    {
        if (_player == null || _rigidbody == null) return;

        // 间歇日志确认速度和 kinematic 状态（每秒一次，避免刷屏）
        if (Time.frameCount % 60 == 0)
            Debug.Log($"[EnemyAI] {name} MoveTowardPlayer: speed={speed}, isKinematic={_rigidbody.isKinematic}, dist={DistanceToPlayer():F2}m, vel={_rigidbody.velocity}");

        Vector3 dir = (_player.position - transform.position).normalized;
        dir.y = 0f;
        _rigidbody.velocity = new Vector3(dir.x * speed, _rigidbody.velocity.y, dir.z * speed);
    }

    private float DistanceToPlayer()
    {
        if (_player == null) return float.MaxValue;
        return Vector3.Distance(transform.position, _player.position);
    }

    // ==================== Gizmos ====================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _detectRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _closeRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _attackRadius);
    }
}

/// <summary>怪物 AI 状态枚举</summary>
public enum EnemyState
{
    Spawn,      // 出生动画
    Idle,       // 待机
    Walk,       // 缓慢接近
    RunStart,   // 起跑
    Run,        // 奔跑
    RunEnd,     // 刹车
    Attack,     // 攻击连段中
    Staggered,  // 僵直受击动画
    StunStart,  // 倒地进入
    StunLoop,   // 倒地持续
    StunEnd,    // 倒地起身
    StunHit,    // 倒地期间受击
    Death,      // 死亡
}