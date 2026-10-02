using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物 AI 状态机。接管所有行为决策和动画切换。
/// Animator Controller 只需把所有动画 clip 拖成独立 state，不需要连线；
/// 代码通过 CrossFadeInFixedTime 直接切换动画。
/// </summary>
[RequireComponent(typeof(Enemy))]
public class EnemyAI : MonoBehaviour
{
    [Header("配置资产（可选，覆盖下方默认值）")]
    [SerializeField] private EnemyConfig _config;

    // ==================== 检测范围 ====================

    [Header("检测范围")]
    [SerializeField] private float _detectRadius = 10f;  // 发现玩家距离
    [SerializeField] private float _closeRadius  = 4f;   // 近距离（可 walk→run 切换）
    [SerializeField] private float _attackRadius = 2.5f;  // 可攻击距离
    [SerializeField] private float _attack06Radius = 2f;  // 06 嚎叫有效距离
    [Min(1.1f)] [SerializeField] private float _loseTargetMultiplier = 1.5f;

    [Header("巡逻（不填巡逻点则围绕出生位置）")]
    [SerializeField] private bool _patrolEnabled = true;
    [SerializeField] private Transform[] _patrolPoints;
    [Min(0.1f)] [SerializeField] private float _patrolRadius = 4f;
    [Min(0f)] [SerializeField] private float _patrolWaitTime = 1.5f;
    [Min(0.1f)] [SerializeField] private float _patrolArrivalDistance = 0.35f;

    [Header("移动速度")]
    [SerializeField] private float _walkSpeed     = 1.5f;
    [SerializeField] private float _runSpeed      = 4.5f;
    [SerializeField] private float _rotationSpeed = 8f;

    [Header("攻击间隔")]
    [SerializeField] private float _comboWindow    = 0.6f; // 连段递进窗口
    [SerializeField] private float _attackCooldown = 1.5f; // 一套连段打完后的冷却
    [Range(0, 1)] [SerializeField] private float _attack06Chance = 0.3f;

    [Header("攻击伤害")]
    [Tooltip("每段攻击对玩家造成的伤害")]
    [SerializeField] private float _attackDamage = 12f;
    [Tooltip("伤害判定的延迟（秒，攻击动画挥击帧）")]
    [SerializeField] private float _hitDelay = 0.25f;

    [Header("眩晕")]
    [SerializeField] private float _stunDuration = 3f;

    [Header("动画平滑")]
    [SerializeField] private float _speedLerpRate = 5f; // Speed 参数平滑过渡速度

    // ==================== 组件 ====================

    private Enemy _enemy;
    private GreatSwordEnemyBrain _swordBrain;
    private Animator _animator;
    private Rigidbody _rigidbody;
    private Transform _player;
    private ThirdPersonController _playerController;
    private Vector3 _spawnPosition;
    private Vector3 _patrolTarget;
    private int _patrolIndex = -1;
    private float _patrolWaitTimer;
    private bool _patrolWaiting;
    private bool _returnToPatrol;
    private bool _started;

    // ==================== 状态机 ====================

    private EnemyState _state;
    private int _attackStep;          // 攻击连段序号 1~7
    private float _stateTimer;        // 当前状态计时器
    private float _comboTimer;        // 连段窗口倒计时（动画播完后才开始）
    private float _attackCooldownTimer; // 攻击冷却
    private float _stunTimer;         // 眩晕倒计时
    private bool _inComboWindow;      // 是否处于动画播完后的连段窗口
    private int _attackGeneration;
    private bool _attackHitResolved;
    private bool _specialAttackPlaying;
    private Coroutine _damageRoutine;
    private Coroutine _specialAttackRoutine;
    private readonly HashSet<string> _missingStates = new HashSet<string>();

    public EnemyState CurrentState => _swordBrain != null && _swordBrain.IsConfigured ? _swordBrain.State : _state;

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
        _swordBrain = GetComponent<GreatSwordEnemyBrain>();
        // 配置资产优先：把资产值覆盖到实例字段（后续逻辑保持不变）
        if (_config != null)
        {
            _detectRadius   = _config.detectRadius;
            _closeRadius    = _config.closeRadius;
            _attackRadius   = _config.attackRadius;
            _attack06Radius = _config.attack06Radius;
            _loseTargetMultiplier = _config.loseTargetMultiplier;
            _patrolEnabled  = _config.patrolEnabled;
            _patrolRadius   = _config.patrolRadius;
            _patrolWaitTime = _config.patrolWaitTime;
            _patrolArrivalDistance = _config.patrolArrivalDistance;
            _walkSpeed      = _config.walkSpeed;
            _runSpeed       = _config.runSpeed;
            _rotationSpeed  = _config.rotationSpeed;
            _comboWindow    = _config.comboWindow;
            _attackCooldown = _config.attackCooldown;
            _attack06Chance = _config.attack06Chance;
            _attackDamage   = _config.attackDamage;
            _hitDelay       = _config.hitDelay;
            _stunDuration   = _config.stunDuration;
            _speedLerpRate  = _config.speedLerpRate;
        }

        _enemy     = GetComponent<Enemy>();
        _animator  = GetComponent<Animator>();
        _rigidbody = GetComponent<Rigidbody>();
        _spawnPosition = transform.position;
        _loseTargetMultiplier = Mathf.Max(1.1f, _loseTargetMultiplier);
        _patrolRadius = Mathf.Max(0.1f, _patrolRadius);
        _patrolArrivalDistance = Mathf.Max(0.1f, _patrolArrivalDistance);
        if (_animator != null) _animator.applyRootMotion = true;
    }

    private void Start()
    {
        if (_swordBrain != null && _swordBrain.IsConfigured) { _started = true; return; }
        RefreshPlayerReference();

        if (_player == null)
        {
            Debug.LogError($"[EnemyAI] {name} 未找到 Player！请确认角色 Tag 设为 'Player'");
        }
        else
        {
            Debug.Log($"[EnemyAI] {name} 找到 Player: {_player.name}");
        }

        if (_animator == null || _animator.runtimeAnimatorController == null)
        {
            Debug.LogError($"[EnemyAI] {name} 需要已绑定 Controller 的 Animator。", this);
            enabled = false;
            return;
        }

        // 订阅 Enemy 事件
        _enemy.OnStaggered += OnStaggered;
        _enemy.OnKnockdown += OnKnockdown;
        _enemy.OnDeath     += OnDeath;
        _enemy.OnHit       += OnHit;

        // 记录 blend tree 默认状态的 hash，后续从 RunStart 等过渡动画切回时用
        _locomotionHash = HasState("Locomotion")
            ? Animator.StringToHash("Locomotion")
            : _animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        _started = true;

        // 开局播出生动画
        ChangeState(EnemyState.Spawn);
    }

    private void Update()
    {
        if (_swordBrain != null && _swordBrain.IsConfigured) return;
        if (_enemy.IsDead || _animator == null) return;

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
        if (_swordBrain != null && _swordBrain.IsConfigured) return;
        if (_animator == null || _rigidbody == null) return;
        if (_enemy.IsDead) return;

        // 定义 OnAnimatorMove 后由本脚本负责回写，不再让 applyRootMotion 提前返回。
        // 移动由代码给速度、动画负责混合；攻击/受击使用动画根位移。
        bool patrolMoving = _state == EnemyState.Patrol && !_patrolWaiting;
        bool chasing = _state == EnemyState.Walk || _state == EnemyState.RunStart || _state == EnemyState.Run;
        if (patrolMoving || chasing)
        {
            Vector3 target = patrolMoving ? _patrolTarget : (_player != null ? _player.position : transform.position);
            Vector3 direction = target - _rigidbody.position;
            direction.y = 0f;
            float stopDistance = patrolMoving ? _patrolArrivalDistance : _attackRadius;
            float speed = _state == EnemyState.Run || _state == EnemyState.RunStart ? _runSpeed : _walkSpeed;
            float distance = Mathf.Min(Mathf.Max(0f, direction.magnitude - stopDistance), Mathf.Max(0f, speed) * Time.deltaTime);
            _rigidbody.MovePosition(_rigidbody.position + direction.normalized * distance);
            return;
        }

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

        Vector3 delta = _animator.deltaPosition;

        // 地面状态：去掉 Y 轴位移（Death 除外，死亡倒下需要垂直运动）
        if (_state != EnemyState.Death)
            delta.y = 0f;

        _rigidbody.MovePosition(_rigidbody.position + delta);

        // 同时同步旋转
        Quaternion deltaRot = _animator.deltaRotation;
        if (deltaRot != Quaternion.identity)
            _rigidbody.MoveRotation(_rigidbody.rotation * deltaRot);
    }

    // ==================== 

    private void OnEnable()
    {
        if (_swordBrain != null && _swordBrain.IsConfigured) return;
        if (_started && _enemy != null && !_enemy.IsDead)
            ChangeState(EnemyState.Idle);
    }

    private void OnDisable()
    {
        CancelAttackWork();
    }

    private void OnDestroy()
    {
        CancelAttackWork();
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
                else if (_patrolEnabled)
                    ChangeState(EnemyState.Patrol);
                break;

            case EnemyState.Patrol:
                if (dist <= _detectRadius)
                {
                    ChangeState(dist <= _attackRadius ? EnemyState.Idle : EnemyState.Walk);
                    break;
                }
                UpdatePatrol();
                break;

            case EnemyState.Walk:
                if (LostPlayer(dist)) { ReturnToPatrol(); break; }
                FacePlayer();
                if (dist <= _attackRadius)
                    ChangeState(_attackCooldownTimer <= 0f ? EnemyState.Attack : EnemyState.Idle);
                else if (dist < _closeRadius)
                    ChangeState(EnemyState.RunStart);
                break;

            case EnemyState.RunStart:
                if (LostPlayer(dist)) { ReturnToPatrol(); break; }
                FacePlayer();
                if (_stateTimer <= 0f) ChangeState(EnemyState.Run);
                break;

            case EnemyState.Run:
                if (LostPlayer(dist)) { ReturnToPatrol(); break; }
                FacePlayer();
                if (dist <= _attackRadius)
                    ChangeState(EnemyState.RunEnd);
                break;

            case EnemyState.RunEnd:
                if (_stateTimer <= 0f)
                    ChangeState(dist <= _attackRadius && _attackCooldownTimer <= 0f ? EnemyState.Attack : EnemyState.Idle);
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
        // 特殊第五段协程负责整段结束；主循环不可在起跑后抢先进入第六段。
        if (_specialAttackPlaying) return;
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

        if (dist > _attackRadius * 1.5f)
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
        int nextStep = _attackStep + 1;
        string nextClip;
        switch (nextStep)
        {
            case 2:
                nextClip = "Goblin_Ani_Attack_02";
                break;
            case 3:
                nextClip = "Goblin_Ani_Attack_03";
                break;
            case 4:
                nextClip = HasState("Goblin_Ani_Attack_04_01") && UnityEngine.Random.value > 0.5f
                    ? "Goblin_Ani_Attack_04_01" : "Goblin_Ani_Attack_04";
                break;
            case 5:
                nextClip = "Goblin_Ani_Attack_05_Start";
                break;
            case 6:
                bool useShout = dist < _attack06Radius && UnityEngine.Random.value < _attack06Chance && HasState("Goblin_Ani_Attack_06");
                nextStep = useShout ? 6 : 7;
                nextClip = useShout ? "Goblin_Ani_Attack_06" : "Goblin_Ani_Attack_07";
                break;
            case 7:
                nextClip = "Goblin_Ani_Attack_07";
                break;
            default:
                EndCombo();
                return;
        }

        // 素材中没有的攻击段不能凭空排队伤害。当前控制器只有第一段也可正常完成循环。
        if (!HasState(nextClip)) { EndCombo(); return; }
        CancelAttackWork();
        _attackStep = nextStep;
        _inComboWindow = false;
        CrossFade(nextClip, 0.1f);
        _stateTimer = ClipLength(nextClip);
        if (_attackStep == 5)
        {
            _specialAttackPlaying = true;
            _specialAttackRoutine = StartCoroutine(Attack05Routine(_attackGeneration));
        }
        else
            ScheduleDamage();
    }

    /// <summary>05 冲击：起跑 → 撞到 / 未撞到 → 完整</summary>
    private IEnumerator Attack05Routine(int generation)
    {
        // 等待起跑动画播完
        yield return new WaitForSeconds(ClipLength("Goblin_Ani_Attack_05_Start"));
        if (!IsAttackCurrent(generation)) yield break;

        float dist = DistanceToPlayer();
        bool hit = dist < _attackRadius * 1.5f;

        if (hit && HasState("Goblin_Ani_Attack_05"))
        {
            CrossFade("Goblin_Ani_Attack_05", 0.1f);
            ScheduleDamage();
            yield return new WaitForSeconds(ClipLength("Goblin_Ani_Attack_05"));
            if (!IsAttackCurrent(generation)) yield break;
            if (HasState("Goblin_Ani_Attack_05_Full"))
            {
                CrossFade("Goblin_Ani_Attack_05_Full", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_05_Full");
            }
            else _stateTimer = 0f;
        }
        else if (HasState("Goblin_Ani_Attack_05_Miss_2"))
        {
            CrossFade("Goblin_Ani_Attack_05_Miss_2", 0.1f);
            _stateTimer = ClipLength("Goblin_Ani_Attack_05_Miss_2");
        }
        else _stateTimer = 0f;
        _specialAttackPlaying = false;
        _specialAttackRoutine = null;
    }

    /// <summary>
    /// 攻击挥击帧的伤害判定：延迟 _hitDelay 后，若玩家在攻击范围内则造成伤害。
    /// </summary>
    private IEnumerator TryDealDamage(int generation)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, _hitDelay));

        if (!IsAttackCurrent(generation) || _attackHitResolved) yield break;
        _attackHitResolved = true;
        _damageRoutine = null;
        if (!HasLivePlayer()) yield break;

        float dist = DistanceToPlayer();
        if (dist > _attackRadius + .02f) yield break; // 与追击停距、起手范围一致，保留极小物理容差。

        _playerController?.TryTakeDamage(_attackDamage, transform.position);
    }

    private void ScheduleDamage()
    {
        if (_damageRoutine != null) StopCoroutine(_damageRoutine);
        _damageRoutine = StartCoroutine(TryDealDamage(_attackGeneration));
    }

    private bool IsAttackCurrent(int generation)
    {
        return isActiveAndEnabled && _enemy != null && !_enemy.IsDead &&
            _state == EnemyState.Attack && _attackGeneration == generation;
    }

    private void CancelAttackWork()
    {
        ++_attackGeneration;
        if (_damageRoutine != null) StopCoroutine(_damageRoutine);
        if (_specialAttackRoutine != null) StopCoroutine(_specialAttackRoutine);
        _damageRoutine = null;
        _specialAttackRoutine = null;
        _specialAttackPlaying = false;
        _attackHitResolved = false;
        _inComboWindow = false;
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

        GameLog.Log($"[EnemyAI] {name} 状态切换: {_state} → {newState}");

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
                if (_rigidbody != null)
                    _rigidbody.velocity = Vector3.zero;
                CrossFade("Goblin_Ani_Born", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Born");
                break;

            case EnemyState.Idle:
                _targetAnimSpeed = 0f;
                EnsureKinematicOff();
                if (_rigidbody != null) _rigidbody.velocity = Vector3.zero;
                CrossFade(_locomotionHash, 0.1f);
                break;

            case EnemyState.Walk:
                _targetAnimSpeed = 0.5f;
                EnsureKinematicOff();
                if (_rigidbody != null) _rigidbody.velocity = Vector3.zero;
                CrossFade(_locomotionHash, 0.1f);
                break;

            case EnemyState.Patrol:
                _targetAnimSpeed = 0.5f;
                EnsureKinematicOff();
                _patrolWaiting = false;
                if (_returnToPatrol)
                {
                    _patrolTarget = _spawnPosition;
                    _returnToPatrol = false;
                }
                else SelectNextPatrolPoint();
                CrossFade(_locomotionHash, 0.1f);
                break;

            case EnemyState.RunStart:
                _targetAnimSpeed = 1f;
                EnsureKinematicOff();
                if (_rigidbody != null) _rigidbody.velocity = Vector3.zero;
                CrossFade(_locomotionHash, 0.1f); // 无 Run_Start 动画，混合树 Speed=1 自然起跑
                _stateTimer = 0.4f;
                break;

            case EnemyState.Run:
                _targetAnimSpeed = 1f;
                EnsureKinematicOff();
                if (_rigidbody != null) _rigidbody.velocity = Vector3.zero;
                CrossFade(_locomotionHash, 0.05f);
                break;

            case EnemyState.RunEnd:
                _targetAnimSpeed = 0f;
                EnsureKinematicOff();
                if (_rigidbody != null) _rigidbody.velocity = Vector3.zero;
                CrossFade(_locomotionHash, 0.1f); // 无 Run_End 动画，混合树 Speed→0 自然停步
                _stateTimer = 0.4f;
                break;

            case EnemyState.Attack:
                CancelAttackWork();
                _attackStep = 1;
                _targetAnimSpeed = 0f;
                _inComboWindow = false;
                if (!HasState("Goblin_Ani_Attack_01"))
                {
                    WarnMissingState("Goblin_Ani_Attack_01");
                    EndCombo();
                    break;
                }
                // 冻结刚体，防止攻击动画的 root bone 位移导致 mesh 与碰撞体分离
                if (_rigidbody != null)
                {
                    _rigidbody.velocity = Vector3.zero;
                    _rigidbody.isKinematic = true;
                }
                CrossFade("Goblin_Ani_Attack_01", 0.1f);
                _stateTimer = ClipLength("Goblin_Ani_Attack_01");
                ScheduleDamage();
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
        if (st == EnemyState.Attack) CancelAttackWork();
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
            _state == EnemyState.RunStart || _state == EnemyState.RunEnd || _state == EnemyState.Patrol)
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
        if (_swordBrain != null && _swordBrain.IsConfigured)
        { _swordBrain.TryParry(parrierPosition, transform.position - parrierPosition); return; }
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
        if (_animator == null || string.IsNullOrEmpty(clipName)) return;
        string stateName = ResolveState(clipName);
        if (stateName != clipName) WarnMissingState(clipName);
        if (HasState(stateName)) _animator.CrossFadeInFixedTime(stateName, duration, 0);
        else CrossFade(_locomotionHash, duration);
    }

    private bool HasState(string stateName)
    {
        return _animator != null && _animator.runtimeAnimatorController != null &&
            _animator.HasState(0, Animator.StringToHash(stateName));
    }

    private string ResolveState(string requested)
    {
        if (HasState(requested)) return requested;
        if (requested.Contains("Hit_") || requested.Contains("Stun_Hit"))
            return HasState("Goblin_Ani_Debuff_Stun_Start") ? "Goblin_Ani_Debuff_Stun_Start" : "Locomotion";
        return "Locomotion";
    }

    private void WarnMissingState(string stateName)
    {
        if (_missingStates.Add(stateName))
            Debug.LogWarning($"[EnemyAI] {name} 缺少状态 {stateName}；使用现有动画回退。敌人连段仅执行 Controller 中实际存在的攻击段。", this);
    }

    /// <summary>通过 state hash 切换动画（用于切回 blend tree 等默认状态）</summary>
    private void CrossFade(int stateHash, float duration)
    {
        if (_animator == null || !_animator.HasState(0, stateHash)) return;
        _animator.CrossFadeInFixedTime(stateHash, duration);
    }

    /// <summary>获取动画 clip 时长（秒）</summary>
    private float ClipLength(string clipName)
    {
        if (_animator == null) return 0.5f;
        if (_animator.runtimeAnimatorController == null) return 0.5f;

        string resolved = ResolveState(clipName);
        // 当前控制器的状态名与 Motion 名不同，保留经素材核对的别名。
        string motionName = resolved;
        switch (resolved)
        {
            case "Goblin_Ani_Attack_01": motionName = "attack"; break;
            case "Goblin_Ani_Debuff_Stun_Start": motionName = "stunstart"; break;
            case "Goblin_Ani_Debuff_Stun_End": motionName = "stunend"; break;
            case "Goblin_Ani_Death_Hit_Front": motionName = "death"; break;
        }
        foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == resolved || clip.name == motionName)
                return clip.length;
        }
        return 0.5f; // fallback
    }

    // ==================== 移动 ====================

    private void FacePlayer()
    {
        if (!HasLivePlayer()) return;
        FacePosition(_player.position);
    }

    private void FacePosition(Vector3 position)
    {
        Vector3 dir = position - transform.position;
        dir.y = 0f;
        if (dir == Vector3.zero) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        if (_rigidbody != null && !_rigidbody.isKinematic)
            _rigidbody.MoveRotation(Quaternion.Slerp(_rigidbody.rotation, targetRot, _rotationSpeed * Time.deltaTime));
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
    }

    private float DistanceToPlayer()
    {
        if (!HasLivePlayer()) return float.MaxValue;
        Vector3 delta = transform.position - _player.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    private bool HasLivePlayer()
    {
        return _player != null && _player.gameObject.activeInHierarchy &&
            (_playerController == null || _playerController.CurrentHealth > 0f);
    }

    private bool LostPlayer(float distance)
    {
        return !HasLivePlayer() || distance > _detectRadius * _loseTargetMultiplier;
    }

    private void ReturnToPatrol()
    {
        _returnToPatrol = true;
        ChangeState(EnemyState.Idle);
    }

    private void UpdatePatrol()
    {
        if (!_patrolEnabled) { ChangeState(EnemyState.Idle); return; }
        if (_patrolWaiting)
        {
            _patrolWaitTimer -= Time.deltaTime;
            if (_patrolWaitTimer > 0f) return;
            _patrolWaiting = false;
            SelectNextPatrolPoint();
            _targetAnimSpeed = 0.5f;
        }
        Vector3 delta = _patrolTarget - transform.position;
        delta.y = 0f;
        if (delta.magnitude <= _patrolArrivalDistance + 0.02f)
        {
            _patrolWaiting = true;
            _patrolWaitTimer = Mathf.Max(0f, _patrolWaitTime);
            _targetAnimSpeed = 0f;
            return;
        }
        FacePosition(_patrolTarget);
    }

    private void SelectNextPatrolPoint()
    {
        if (_patrolPoints != null && _patrolPoints.Length > 0)
        {
            for (int offset = 0; offset < _patrolPoints.Length; ++offset)
            {
                _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Length;
                if (_patrolPoints[_patrolIndex] == null) continue;
                _patrolTarget = _patrolPoints[_patrolIndex].position;
                return;
            }
        }
        // 开阔演示场地的四点路线。复杂障碍场景应显式放置可通行的巡逻点。
        _patrolIndex = (_patrolIndex + 1) % 4;
        float angle = _patrolIndex * Mathf.PI * 0.5f;
        _patrolTarget = _spawnPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _patrolRadius;
    }

    /// <summary>重新查找玩家引用（玩家在开始界面是隐藏的，StartGame 后再调用）</summary>
    public void RefreshPlayerReference()
    {
        _swordBrain?.RefreshTarget();
        _player = GameObject.FindGameObjectWithTag("Player")?.transform;
        _playerController = _player != null ? _player.GetComponent<ThirdPersonController>() : null;
    }

    public bool TryParry(Vector3 position, Vector3 facing)
    {
        if (_swordBrain != null && _swordBrain.IsConfigured) return _swordBrain.TryParry(position, facing);
        Vector3 toward = transform.position - position; toward.y = 0;
        if (_enemy == null || _enemy.IsDead || _state != EnemyState.Attack || toward.magnitude > _attackRadius + .5f ||
            Vector3.Dot(facing.normalized, toward.normalized) < .3f) return false;
        OnParried(position); return true;
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
        if (_patrolEnabled)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(Application.isPlaying ? _spawnPosition : transform.position, _patrolRadius);
            if (Application.isPlaying && _state == EnemyState.Patrol)
                Gizmos.DrawLine(transform.position, _patrolTarget);
        }
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
    Patrol,     // 巡逻（追加，保留原枚举序号）
    Guard, GuardStart, GuardHit, Recover, Airborne,
}
