using System;
using UnityEngine;

public enum PlayerState { Idle, Move, Run, Dodge, Attack, Parry, Block, Hit, Knockdown, Dead, Jump, AirAction }
public enum PlayerStateGroup { Locomotion, Action, Disabled }

[RequireComponent(typeof(Rigidbody))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("组件引用")]
    [Tooltip("移动控制模块")]
    [SerializeField] private PlayerLocomotion locomotion;
    [Tooltip("动画控制模块")]
    [SerializeField] private PlayerAnimController animCtrl;
    [Tooltip("战斗模块")]
    [SerializeField] private PlayerCombat combat;

    [Header("地面检测")]
    [Tooltip("地面所在 Layer")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("地面射线检测距离")]
    [SerializeField] private float groundCheckDistance = 0.2f;
    [Header("跳跃（位移统一在 OnAnimatorMove 应用）")]
    [SerializeField, Min(.1f)] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;
    private float _verticalSpeed;
    private float _jumpStartedAt;
    private float _groundCorrection;
    private readonly RaycastHit[] _groundHits = new RaycastHit[16];

    [Header("攻击手感")]
    [Tooltip("攻击命中判定的延迟时间")]
    [SerializeField] private float _attackHitDelay    = 0.15f;
    [Tooltip("攻击时停在敌人前方的距离")]
    [SerializeField] private float _attackStopDist    = 1.2f;

    [Header("弹刀")]
    [Tooltip("弹刀动画持续时间")]
    [SerializeField] private float _parryDuration     = 0.4f;

    [Header("格挡（小弹刀）")]
    [Tooltip("每挡一刀的架势增量")]
    [SerializeField] private float _blockPostureGain = 30f;
    [Tooltip("格挡成功后被击退的距离")]
    [SerializeField] private float _blockKnockbackDistance = 0.4f;
    [Tooltip("击退位移时长（秒）")]
    [SerializeField] private float _blockKnockbackDuration = 0.15f;
    [Tooltip("架势满后倒地时长（秒）")]
    [SerializeField] private float _knockdownDuration = 1.8f;
    private float knockdownTimer;

    [Header("受击")]
    [Tooltip("受击硬直时长（秒，对应受击动画播放期间）")]
    [SerializeField] private float _hitDuration = 0.6f;
    [SerializeField, Min(0f)] private float _hitKnockbackDistance = .35f;

    private Collider characterCollider;
    private bool isGrounded;

    private PlayerState currentState = PlayerState.Idle;
    private Rigidbody rb;
    private PlayerAirCombat _air;
    public bool IsGroundedForActions => isGrounded;
    public void BeginAirAction() { if (currentState != PlayerState.Dead) ChangeState(PlayerState.AirAction); }
    public void EndAirAction(float verticalSpeed) { _verticalSpeed = verticalSpeed; if (currentState == PlayerState.AirAction) ChangeState(ResolveLocomotionState()); }
    private Vector3 moveInput;
    private float dodgeTimer;
    private float dodgeElapsed;
    private float parryTimer;
    private float hitTimer;
    private bool  _parryTriggered;
    private Coroutine _knockbackRoutine;
    private Vector3 _externalMotion;
    private Coroutine _delayedHitRoutine;
    private bool _attackPressedThisFrame;
    private int _enabledFrame;

    // 输入缓冲：防止180°转向时短暂无输入导致状态闪烁
    private float _inputBufferTimer;
    [SerializeField] private float _inputBufferDuration = 0.1f;

    // 预输入缓冲（攻击/弹刀/闪避），窗口内按下的输入会被缓存并延迟触发
    private float _bufferedAttack;
    private float _bufferedParry;
    private float _bufferedDodge;
    private const float INPUT_BUFFER_WINDOW = 0.18f;

    // 脚步声
    private float _stepTimer;
    [SerializeField] private float _walkStepInterval = 0.5f;
    [SerializeField] private float _runStepInterval = 0.3f;

    // 当前帧的移动动画融合目标
    private float blendTarget;

    // 公开访问器（向后兼容外部调用）
    public float CurrentHealth => combat != null ? combat.CurrentHealth : 0f;
    public float MaxHealth => combat != null ? combat.MaxHealth : 0f;

    public bool IsBlocking => currentState == PlayerState.Block || currentState == PlayerState.Parry;
    public PlayerState State => currentState;
    public PlayerStateGroup StateGroup => currentState == PlayerState.Dead ? PlayerStateGroup.Disabled :
        (currentState == PlayerState.Idle || currentState == PlayerState.Move || currentState == PlayerState.Run || currentState == PlayerState.Jump ? PlayerStateGroup.Locomotion : PlayerStateGroup.Action);
    public bool CanReloadCombo => StateGroup == PlayerStateGroup.Locomotion && currentState != PlayerState.Jump;
    public event Action<PlayerState, PlayerState> StateChanged;

    public void TryTakeDamage(float damage, Vector3? attackerPosition = null)
    {
        if (combat == null) return;

        // 格挡拦截：格挡/弹刀期间受到的攻击不扣血，改为架势积累 + 击退反馈
        if (IsBlocking)
        {
            OnBlockedHit(damage, attackerPosition);
            return;
        }

        float before = combat.CurrentHealth;
        combat.TakeDamage(damage, attackerPosition);
        if (combat.CurrentHealth > 0f && combat.CurrentHealth < before)
        {
            Vector3 away = attackerPosition.HasValue ? transform.position - attackerPosition.Value : -transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < .001f) away = -transform.forward;
            if (_knockbackRoutine != null) StopCoroutine(_knockbackRoutine);
            _externalMotion = Vector3.zero;
            _knockbackRoutine = StartCoroutine(KnockbackRoutine(away.normalized, _hitKnockbackDistance, .15f));
        }
    }

    /// <summary>格挡命中：泄力动画 + 小击退 + 架势积累；架势满则倒地</summary>
    private void OnBlockedHit(float damage, Vector3? attackerPosition)
    {
        combat.AddPosture(_blockPostureGain);

        // 泄力动画（攻击倒放，动画机 Deflect 状态）+ 格挡音效
        animCtrl.TriggerDeflect();
        if (SoundManager.Instance != null)
            SoundManager.Instance.Play("sword_hit_03", transform.position);

        // 小击退：朝攻击者的反方向
        Vector3 dir = attackerPosition.HasValue
            ? (transform.position - attackerPosition.Value).normalized
            : -transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
        if (_knockbackRoutine != null) StopCoroutine(_knockbackRoutine);  // 连挡时重启击退，防止多个击退协程叠加漂移
        _knockbackRoutine = StartCoroutine(KnockbackRoutine(dir, _blockKnockbackDistance, _blockKnockbackDuration));

        // 架势满 → 击飞（倒地）
        if (combat.IsPostureBroken)
            ChangeState(PlayerState.Knockdown);
    }

    /// <summary>参数化击退：在 duration 内沿 dir 位移 distance（根运动状态下也稳定）</summary>
    private System.Collections.IEnumerator KnockbackRoutine(Vector3 dir, float distance, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            float dt = Mathf.Min(Time.deltaTime, duration - t);
            t += dt;
            _externalMotion += dir * (distance * dt / Mathf.Max(.001f, duration));
            yield return null;
        }
    }

    /// <summary>刷新摄像机引用（开始界面进入游戏后调用）</summary>
    public void RefreshCameraReference()
    {
        if (locomotion != null && CameraCache.Main != null)
            locomotion.Initialize(rb, CameraCache.Main.transform);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _air = GetComponent<PlayerAirCombat>();
        characterCollider = GetComponent<Collider>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false; // 重力与跳跃统一经 OnAnimatorMove 移动，避免两套竖直位移叠加。
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        if (animCtrl == null) animCtrl = GetComponent<PlayerAnimController>();
        if (combat == null) combat = GetComponent<PlayerCombat>();

        locomotion.Initialize(rb, CameraCache.Main != null ? CameraCache.Main.transform : null);
        animCtrl.Initialize();
        combat.Initialize();

        // 订阅受击/死亡事件
        PlayerCombat.OnPlayerHit -= OnPlayerHit;
        PlayerCombat.OnPlayerHit += OnPlayerHit;
        PlayerCombat.OnPlayerDeath -= OnPlayerDeath;
        PlayerCombat.OnPlayerDeath += OnPlayerDeath;
    }

    private void OnDestroy()
    {
        PlayerCombat.OnPlayerHit -= OnPlayerHit;
        PlayerCombat.OnPlayerDeath -= OnPlayerDeath;
    }

    private void OnEnable() { _enabledFrame = Time.frameCount; }

    private void OnDisable()
    {
        if (_delayedHitRoutine != null) StopCoroutine(_delayedHitRoutine);
        _delayedHitRoutine = null;
        if (_knockbackRoutine != null) StopCoroutine(_knockbackRoutine);
        _knockbackRoutine = null;
        combat?.ResetAllTimers();
        _bufferedAttack = _bufferedDodge = _bufferedParry = 0f;
        _externalMotion = Vector3.zero;
    }

    private void OnPlayerHit(int hitType)
    {
        if (currentState == PlayerState.Dead) return;
        GetComponent<CombatFeedback>()?.Pulse(Color.red);
        hitTimer = _hitDuration;
        ChangeState(PlayerState.Hit);
        animCtrl.TriggerHit(hitType);
    }

    private void OnPlayerDeath()
    {
        combat.SetInvulnerable(true); // 死亡后不再受击
        ChangeState(PlayerState.Dead);
        animCtrl.TriggerDeath();
    }

    private void Update()
    {
        ReadInput();
        TickInputBuffers();
        combat.ApplyPendingReloadIfSafe();
        if (_air != null && _air.TickInput()) { TickAnimatorBlend(); return; }
        // 上层先区分移动/动作/禁用，移动子状态共享空中与跳跃规则。
        if (StateGroup == PlayerStateGroup.Locomotion)
        {
            if (currentState != PlayerState.Jump && isGrounded && Time.frameCount > _enabledFrame + 1 && Input.GetKeyDown(KeyCode.Space))
            {
                _verticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                _jumpStartedAt = Time.time;
                isGrounded = false;
                ChangeState(PlayerState.Jump);
            }
            if (currentState == PlayerState.Jump) UpdateJump();
            else if (currentState == PlayerState.Idle) UpdateIdle();
            else if (currentState == PlayerState.Move) UpdateMove();
            else if (currentState == PlayerState.Run) UpdateRun();
        }
        else if (StateGroup == PlayerStateGroup.Action)
        {
            switch (currentState)
            {
                case PlayerState.Dodge: UpdateDodge(); break;
                case PlayerState.Attack: UpdateAttack(); break;
                case PlayerState.Parry: UpdateParry(); break;
                case PlayerState.Block: UpdateBlock(); break;
                case PlayerState.Hit: UpdateHit(); break;
                case PlayerState.Knockdown: UpdateKnockdown(); break;
            }
        }

        TickAnimatorBlend();
        UpdateFootsteps();
    }

    private void FixedUpdate()
    {
        // 地面校正每个物理步重新测量，避免多个动画步重复使用渲染帧留下的竖直修正量。
        CheckGrounded();
        // 位移仍由同一物理步的OnAnimatorMove应用，这里只处理转向。
        if (currentState == PlayerState.Attack || currentState == PlayerState.Dodge || currentState == PlayerState.Parry
            || currentState == PlayerState.Block || currentState == PlayerState.Hit
            || currentState == PlayerState.Knockdown || currentState == PlayerState.Dead || currentState == PlayerState.AirAction)
            return;

        locomotion.RotateToward(moveInput);
    }

    // ─── 输入 ────────────────────────────────────────
    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 axes = PlayerLocomotion.ResolveMoveAxes(new Vector2(h, v),
            Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow),
            Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow),
            Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow),
            Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow));
        moveInput = locomotion.GetCameraRelativeInput(axes.x, axes.y);

        // 记录按键并写入预输入缓冲
        _attackPressedThisFrame = Input.GetMouseButtonDown(0);
        if (_attackPressedThisFrame) _bufferedAttack = INPUT_BUFFER_WINDOW;
        if (Input.GetMouseButtonDown(1)) _bufferedParry = INPUT_BUFFER_WINDOW;
        if (Input.GetKeyDown(KeyCode.LeftShift)) _bufferedDodge = INPUT_BUFFER_WINDOW;
    }

    /// <summary>每帧递减预输入缓冲计时器</summary>
    private void TickInputBuffers()
    {
        float dt = Time.deltaTime;
        _bufferedAttack = Mathf.Max(0f, _bufferedAttack - dt);
        _bufferedParry = Mathf.Max(0f, _bufferedParry - dt);
        _bufferedDodge = Mathf.Max(0f, _bufferedDodge - dt);
    }

    private bool ConsumeBufferedAttack() => ConsumeBuffered(ref _bufferedAttack);
    private bool ConsumeBufferedParry() => ConsumeBuffered(ref _bufferedParry);
    private bool ConsumeBufferedDodge() => ConsumeBuffered(ref _bufferedDodge);

    private bool ConsumeBuffered(ref float b)
    {
        if (b <= 0f) return false;
        b = 0f;
        return true;
    }

    // ─── 状态机 ──────────────────────────────────────
    private void ChangeState(PlayerState next)
    {
        if (currentState == next) return;
        PlayerState previous = currentState;
        OnExitState(currentState);
        currentState = next;
        bool returningFromAction = previous == PlayerState.Attack || previous == PlayerState.Dodge || previous == PlayerState.Hit || previous == PlayerState.Parry || previous == PlayerState.Block || previous == PlayerState.AirAction || previous == PlayerState.Jump;
        if (returningFromAction && (next == PlayerState.Idle || next == PlayerState.Move || next == PlayerState.Run))
        {
            // 回移动前同步参数，避免刚进入Locomotion时Movement仍为0又自动回Idle。
            float target = next == PlayerState.Run ? 2f : next == PlayerState.Move ? 1f : 0f;
            locomotion.ResetBlending(target);
            animCtrl.SetMovement(target);
            animCtrl.SetLastMoveSpeed(target);
            animCtrl.SetRun(next == PlayerState.Run);
            animCtrl.ReturnToLocomotion(next == PlayerState.Idle);
        }
        OnEnterState(next);
        StateChanged?.Invoke(previous, next);
    }

    private void OnEnterState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.AirAction:
                _bufferedAttack = _bufferedDodge = _bufferedParry = 0; locomotion.Halt();
                break;
            case PlayerState.Dodge:
                dodgeTimer = 0.4f;
                dodgeElapsed = 0f;
                combat.SetInvulnerable(true);
                Vector3 dir = GetDodgeDirection();
                locomotion.StartDodge(dir);
                animCtrl.TriggerDodge();
                break;

            case PlayerState.Attack:
                FaceNearestEnemy();
                if (combat.UsesConfiguredCombo)
                {
                    if (!combat.StartConfiguredAttack()) ChangeState(PlayerState.Idle);
                }
                else
                {
                    combat.StartAttack();
                    animCtrl.TriggerAttack();
                    ScheduleLegacyHit();
                }
                break;

            case PlayerState.Jump:
                animCtrl.TriggerJump();
                break;

            case PlayerState.Parry:
                parryTimer = _parryDuration;
                combat.SetInvulnerable(true);
                animCtrl.TriggerParry();
                break;

            case PlayerState.Block:
                combat.SetInvulnerable(true);   // 格挡期间不扣血（由 TryTakeDamage 拦截）
                animCtrl.SetBlocking(true);     // 切到格挡循环动画
                locomotion.Halt();
                break;

            case PlayerState.Knockdown:
                knockdownTimer = _knockdownDuration;
                animCtrl.TriggerKnockdown();
                locomotion.Halt();
                break;

            case PlayerState.Hit:
                hitTimer = _hitDuration;
                locomotion.Halt();
                break;

            case PlayerState.Dead:
                locomotion.Halt();
                break;
        }
    }

    private void OnExitState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.AirAction:
                if (_air != null && _air.Phase != PlayerAirCombat.AirPhase.None) { _verticalSpeed = _air.VerticalSpeed; _air.Cancel(); }
                break;
            case PlayerState.Dodge:
                combat.SetInvulnerable(false);
                locomotion.ResetBlending();
                break;

            case PlayerState.Attack:
                if (_delayedHitRoutine != null) StopCoroutine(_delayedHitRoutine);
                _delayedHitRoutine = null;
                combat.ResetAllTimers();
                _bufferedAttack = 0f;
                animCtrl.ClearAttackTriggers();
                locomotion.ResetBlending();
                break;

            case PlayerState.Parry:
                combat.SetInvulnerable(false);
                _parryTriggered = false;
                locomotion.ResetBlending();
                break;

            case PlayerState.Block:
                combat.SetInvulnerable(false);
                animCtrl.SetBlocking(false);
                locomotion.ResetBlending();
                break;
        }
    }

    private Vector3 GetDodgeDirection()
    {
        if (moveInput.sqrMagnitude > 0.01f) return moveInput;
        return transform.forward;
    }

    private PlayerState ResolveLocomotionState()
    {
        if (moveInput.sqrMagnitude <= .01f) return PlayerState.Idle;
        return Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move;
    }

    // ─── 各状态 Update（匹配参考代码行为）──────────

    private void UpdateIdle()
    {
        // 攻击始终最高优先级
        if (ConsumeBufferedAttack()) { ChangeState(PlayerState.Attack); return; }

        // 弹刀
        if (ConsumeBufferedParry()) { ChangeState(PlayerState.Parry); return; }
        if (Input.GetMouseButton(1)) { ChangeState(PlayerState.Block); return; }

        // Shift 点击 → 前冲
        if (ConsumeBufferedDodge())
        {
            ChangeState(PlayerState.Dodge);
            return;
        }

        // blend 仍在减速 → 阻塞移动类转换
        if (!locomotion.IsBlendingComplete) return;

        // Shift 按住 + WASD → 奔跑
        if (Input.GetKey(KeyCode.LeftShift) && moveInput.sqrMagnitude > 0.01f)
        {
            ChangeState(PlayerState.Run);
            return;
        }

        // WASD → 行走
        if (moveInput.sqrMagnitude > 0.01f) ChangeState(PlayerState.Move);
    }

    private void UpdateMove()
    {
        // WASD 松开 → 累积缓冲计时，超过阈值才切 Idle（防180°转向闪烁）
        if (moveInput.sqrMagnitude < 0.01f)
        {
            _inputBufferTimer += Time.deltaTime;
            if (_inputBufferTimer >= _inputBufferDuration) { ChangeState(PlayerState.Idle); }
            return;
        }
        _inputBufferTimer = 0f;

        // 攻击
        if (ConsumeBufferedAttack()) { ChangeState(PlayerState.Attack); return; }

        // 弹刀
        if (ConsumeBufferedParry()) { ChangeState(PlayerState.Parry); return; }
        if (Input.GetMouseButton(1)) { ChangeState(PlayerState.Block); return; }

        // Shift 点击 → 前冲
        if (ConsumeBufferedDodge()) { ChangeState(PlayerState.Dodge); return; }

        if (Input.GetKey(KeyCode.LeftShift)) { ChangeState(PlayerState.Run); return; }
    }

    private void UpdateRun()
    {
        // WASD 松开 → 累积缓冲计时，超过阈值才切 Idle（防180°转向闪烁）
        if (moveInput.sqrMagnitude < 0.01f)
        {
            _inputBufferTimer += Time.deltaTime;
            if (_inputBufferTimer >= _inputBufferDuration) { ChangeState(PlayerState.Idle); }
            return;
        }
        _inputBufferTimer = 0f;

        // 攻击
        if (ConsumeBufferedAttack()) { ChangeState(PlayerState.Attack); return; }

        // 弹刀
        if (ConsumeBufferedParry()) { ChangeState(PlayerState.Parry); return; }
        if (Input.GetMouseButton(1)) { ChangeState(PlayerState.Block); return; }

        // Shift 点击 → 前冲
        if (ConsumeBufferedDodge())
        {
            ChangeState(PlayerState.Dodge);
            return;
        }

        if (!Input.GetKey(KeyCode.LeftShift)) { ChangeState(PlayerState.Move); return; }
    }

    private void UpdateDodge()
    {
        dodgeTimer -= Time.deltaTime;
        dodgeElapsed += Time.deltaTime;
        // 无敌窗口维持原0.4秒，动作长度单独跟随动画进度，避免完整动画变成全程无敌。
        if (dodgeTimer <= 0f) combat.SetInvulnerable(false);
        if (animCtrl.DodgePlaybackFinished(dodgeElapsed)) ChangeState(ResolveLocomotionState());
    }

    private void UpdateParry()
    {
        parryTimer -= Time.deltaTime;

        // 弹刀窗口内检测弹反
        if (!_parryTriggered && combat.TryParry(transform.position))
        {
            _parryTriggered = true;
            Debug.Log("[Controller] 弹刀成功！");
        }

        if (parryTimer <= 0f)
        {
            _parryTriggered = false;
            // 按住右键 → 进入持续格挡；松开 → 回到移动/待机
            if (Input.GetMouseButton(1)) { ChangeState(PlayerState.Block); return; }
            if (moveInput.sqrMagnitude > 0.01f)
                ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            else
                ChangeState(PlayerState.Idle);
        }
    }

    private void UpdateBlock()
    {
        // 格挡中：松开右键退出，回到移动/待机
        if (!Input.GetMouseButton(1))
        {
            if (moveInput.sqrMagnitude > 0.01f)
                ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            else
                ChangeState(PlayerState.Idle);
        }
    }

    private void UpdateKnockdown()
    {
        // 倒地期间锁输入，起身后架势清零
        knockdownTimer -= Time.deltaTime;
        if (knockdownTimer <= 0f)
        {
            combat.ResetPosture();
            animCtrl.TriggerGetUp();
            ChangeState(PlayerState.Idle);
        }
    }

    private void UpdateHit()
    {
        // 受击硬直期间不响应任何输入，结束后回到 Idle
        hitTimer -= Time.deltaTime;
        if (hitTimer <= 0f)
            ChangeState(PlayerState.Idle);
    }

    private void UpdateAttack()
    {
        // 接段只读取本帧按下，通用缓冲不能把窗口前的输入带入窗口内。
        _bufferedAttack = 0f;
        if (combat.CanCancelAttack)
        {
            if (ConsumeBufferedDodge()) { ChangeState(PlayerState.Dodge); return; }
            if (ConsumeBufferedParry()) { ChangeState(PlayerState.Parry); return; }
            if (Input.GetMouseButton(1)) { ChangeState(PlayerState.Block); return; }
        }
        if (combat.UsesConfiguredCombo)
        {
            if (_attackPressedThisFrame) combat.TryAdvanceConfiguredCombo();
            if (combat.ConfiguredAttackFinished()) ChangeState(ResolveLocomotionState());
            return;
        }

        if (_attackPressedThisFrame && combat.IsAttacking)
        {
            combat.BufferCombo();
            if (combat.ConsumeBufferedCombo())
            {
                animCtrl.TriggerNextAttack();
                ScheduleLegacyHit();
                return;
            }
        }
        if (combat.IsAttacking) { combat.DecrementTimer(Time.deltaTime); return; }
        combat.DecrementGraceTimer(Time.deltaTime);
        if (combat.IsInGrace) return;
        if (combat.IncrementForceExitTimer(Time.deltaTime) || animCtrl.IsInState("Idle") || animCtrl.IsInState("idle"))
            ChangeState(ResolveLocomotionState());
    }

    private void UpdateJump()
    {
        _bufferedAttack = _bufferedDodge = _bufferedParry = 0f;
        if (Time.time - _jumpStartedAt > .12f && isGrounded && _verticalSpeed <= 0f)
        {
            ChangeState(ResolveLocomotionState());
        }
    }

    /// <summary>由动画模块唯一调用；水平移动、动画根位移与重力在此合并。</summary>
    public Vector3 EvaluateRootMotion(Vector3 animationDelta, float deltaTime)
    {
        if (currentState == PlayerState.AirAction && _air != null)
        { Vector3 airDelta = _air.EvaluateMotion(animationDelta, deltaTime) + _externalMotion; _externalMotion = Vector3.zero; return airDelta; }
        Vector3 delta = Vector3.zero;
        if (StateGroup == PlayerStateGroup.Locomotion)
            delta = locomotion.GetDisplacement(moveInput, currentState == PlayerState.Run || (currentState == PlayerState.Jump && Input.GetKey(KeyCode.LeftShift)), deltaTime);
        else if (currentState == PlayerState.Attack || currentState == PlayerState.Dodge || currentState == PlayerState.Parry)
        {
            delta = animationDelta;
            delta.y = 0f;
            if (currentState == PlayerState.Attack) delta = ClampForwardMotion(delta * combat.AttackRootMotionScale);
        }
        if (isGrounded && _verticalSpeed <= 0f)
        {
            _verticalSpeed = 0f;
            delta.y = _groundCorrection;
        }
        else
        {
            _verticalSpeed += gravity * deltaTime;
            delta.y = _verticalSpeed * deltaTime;
        }
        delta += _externalMotion;
        _externalMotion = Vector3.zero;
        return delta;
    }
    // ─── 动画融合 ─────────────────────────────────────
    private void TickAnimatorBlend()
    {
        blendTarget = 0f;
        if (currentState == PlayerState.Move) blendTarget = 1f;
        else if (currentState == PlayerState.Run) blendTarget = 2f;

        var result = locomotion.TickBlending(blendTarget, Time.deltaTime);
        animCtrl.SetMovement(result.blend);
        animCtrl.SetLastMoveSpeed(result.lastTarget);
        animCtrl.SetRun(currentState == PlayerState.Run);
    }

    /// <summary>移动时按步频播放脚步声（跑步更快）</summary>
    private void UpdateFootsteps()
    {
        bool moving = currentState == PlayerState.Move || currentState == PlayerState.Run;
        if (!moving)
        {
            _stepTimer = 0f;
            return;
        }

        float interval = currentState == PlayerState.Run ? _runStepInterval : _walkStepInterval;
        _stepTimer -= Time.deltaTime;
        if (_stepTimer <= 0f)
        {
            _stepTimer = interval;
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlayByPrefix("foot_step", transform.position, 0.9f, 1.1f);
        }
    }

    // ─── 地面检测 ─────────────────────────────────────
    private void CheckGrounded()
    {
        isGrounded = false;
        _groundCorrection = 0f;
        if (characterCollider == null || _verticalSpeed > .01f) return;
        var bounds = characterCollider.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + .12f, bounds.center.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, .12f + groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);
        float closest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = _groundHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Enemy>() != null || hit.normal.y < .5f || hit.distance >= closest) continue;
            closest = hit.distance;
            isGrounded = true;
            _groundCorrection = .12f - hit.distance;
        }
    }

    // ─── 攻击辅助 ─────────────────────────────────────

    /// <summary>延迟 hit detection（仅碰撞检测+伤害，音效和顿帧由 Animation Event 驱动）</summary>
    private void ScheduleLegacyHit()
    {
        if (_delayedHitRoutine != null) StopCoroutine(_delayedHitRoutine);
        _delayedHitRoutine = StartCoroutine(DelayedHitDetection(_attackHitDelay, combat.AttackToken));
    }

    private System.Collections.IEnumerator DelayedHitDetection(float delay, int token)
    {
        yield return new WaitForSeconds(delay);
        _delayedHitRoutine = null;
        if (currentState == PlayerState.Attack && combat.AttackToken == token && combat.IsComboActive)
            combat.PerformHitDetection();
    }

    /// <summary>攻击时转向最近的敌人</summary>
    private void FaceNearestEnemy()
    {
        Transform nearest = FindNearestEnemy();
        if (nearest == null) return;

        Vector3 dir = (nearest.position - transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
            rb.MoveRotation(Quaternion.LookRotation(dir));
    }

    /// <summary>
    /// 限制 root motion 前移量，防止角色穿过敌人。
    /// 如果前方近距离有敌人，缩短位移使角色停在敌人面前。
    /// </summary>
    private Vector3 ClampForwardMotion(Vector3 delta)
    {
        // 只有向前移动时才检查
        Vector3 forward = delta.normalized;
        float forwardDot = Vector3.Dot(forward, transform.forward);
        if (forwardDot <= 0f) return delta; // 不是向前移动，不拦截

        Transform nearest = FindNearestEnemy();
        if (nearest == null) return delta;

        Vector3 toEnemy = nearest.position - rb.position;
        toEnemy.y = 0f;
        float dist = toEnemy.magnitude;

        // 已经足够近了，阻止继续前移
        if (dist <= _attackStopDist)
            return Vector3.zero;

        // 限制前移量，不要越过敌人
        float maxForward = dist - _attackStopDist;
        float deltaMag = delta.magnitude;
        if (deltaMag > maxForward)
            return delta.normalized * maxForward;

        return delta;
    }

    /// <summary>找到最近的敌人（委托 TargetFinder）</summary>
    private Transform FindNearestEnemy()
    {
        if (combat == null) return null;
        return TargetFinder.FindNearest(transform, combat.EnemyLayer, 4f);
    }
}
