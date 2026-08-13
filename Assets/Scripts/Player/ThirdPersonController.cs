using System;
using UnityEngine;

public enum PlayerState { Idle, Move, Run, Dodge, Attack, Parry, Hit, Dead }

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

    [Header("攻击手感")]
    [Tooltip("攻击命中判定的延迟时间")]
    [SerializeField] private float _attackHitDelay    = 0.15f;
    [Tooltip("攻击时停在敌人前方的距离")]
    [SerializeField] private float _attackStopDist    = 1.2f;

    [Header("弹刀")]
    [Tooltip("弹刀动画持续时间")]
    [SerializeField] private float _parryDuration     = 0.4f;

    [Header("受击")]
    [Tooltip("受击硬直时长（秒，对应受击动画播放期间）")]
    [SerializeField] private float _hitDuration = 0.6f;

    private Collider characterCollider;
    private bool isGrounded;

    private PlayerState currentState = PlayerState.Idle;
    private Rigidbody rb;
    private Vector3 moveInput;
    private float dodgeTimer;
    private float parryTimer;
    private float hitTimer;
    private bool  _parryTriggered;

    // 输入缓冲：防止180°转向时短暂无输入导致状态闪烁
    private float _inputBufferTimer;
    [SerializeField] private float _inputBufferDuration = 0.1f;

    // 当前帧的动画融合目标（只读，供 ApplyMovement 使用）
    private float blendTarget;

    // 公开访问器（向后兼容外部调用）
    public float CurrentHealth => combat != null ? combat.CurrentHealth : 0f;
    public float MaxHealth => combat != null ? combat.MaxHealth : 0f;

    public void TryTakeDamage(float damage, Vector3? attackerPosition = null)
    {
        if (combat != null)
            combat.TakeDamage(damage, attackerPosition);
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
        characterCollider = GetComponent<Collider>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

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

    private void OnPlayerHit(int hitType)
    {
        if (currentState == PlayerState.Dead) return;
        animCtrl.TriggerHit(hitType);
        ChangeState(PlayerState.Hit);
    }

    private void OnPlayerDeath()
    {
        combat.SetInvulnerable(true); // 死亡后不再受击
        animCtrl.TriggerDeath();
        ChangeState(PlayerState.Dead);
    }

    private void Update()
    {
        ReadInput();
        CheckGrounded();

        switch (currentState)
        {
            case PlayerState.Idle: UpdateIdle(); break;
            case PlayerState.Move: UpdateMove(); break;
            case PlayerState.Run: UpdateRun(); break;
            case PlayerState.Dodge: UpdateDodge(); break;
            case PlayerState.Attack: UpdateAttack(); break;
            case PlayerState.Parry: UpdateParry(); break;
            case PlayerState.Hit: UpdateHit(); break;
            case PlayerState.Dead: break;
        }

        TickAnimatorBlend();
    }

    private void FixedUpdate()
    {
        // 所有状态的位移由 OnAnimatorMove（根运动）驱动，这里只处理转向
        if (currentState == PlayerState.Attack || currentState == PlayerState.Dodge || currentState == PlayerState.Parry
            || currentState == PlayerState.Hit || currentState == PlayerState.Dead)
            return;

        locomotion.RotateToward(moveInput);
    }

    // ─── 输入 ────────────────────────────────────────
    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        moveInput = locomotion.GetCameraRelativeInput(h, v);
    }

    // ─── 状态机 ──────────────────────────────────────
    private void ChangeState(PlayerState next)
    {
        if (currentState == next) return;
        OnExitState(currentState);
        currentState = next;
        OnEnterState(next);
    }

    private void OnEnterState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Dodge:
                dodgeTimer = 0.4f;
                combat.SetInvulnerable(true);
                Vector3 dir = GetDodgeDirection();
                locomotion.StartDodge(dir);
                animCtrl.TriggerDodge();
                break;

            case PlayerState.Attack:
                combat.StartAttack();
                animCtrl.TriggerAttack();
                FaceNearestEnemy();
                StartCoroutine(DelayedHitDetection(_attackHitDelay));
                break;

            case PlayerState.Parry:
                parryTimer = _parryDuration;
                combat.SetInvulnerable(true);
                animCtrl.TriggerParry();
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
            case PlayerState.Dodge:
                combat.SetInvulnerable(false);
                locomotion.ResetBlending();
                break;

            case PlayerState.Attack:
                combat.ResetAllTimers();
                locomotion.ResetBlending();
                break;

            case PlayerState.Parry:
                combat.SetInvulnerable(false);
                locomotion.ResetBlending();
                break;
        }
    }

    private Vector3 GetDodgeDirection()
    {
        if (moveInput.sqrMagnitude > 0.01f) return moveInput;
        return transform.forward;
    }

    // ─── 各状态 Update（匹配参考代码行为）──────────

    private void UpdateIdle()
    {
        // 攻击始终最高优先级
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

        // 弹刀
        if (Input.GetMouseButtonDown(1)) { ChangeState(PlayerState.Parry); return; }

        // Shift 点击 → 前冲
        if (Input.GetKeyDown(KeyCode.LeftShift))
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
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

        // 弹刀
        if (Input.GetMouseButtonDown(1)) { ChangeState(PlayerState.Parry); return; }

        // Shift 点击 → 前冲
        if (Input.GetKeyDown(KeyCode.LeftShift)) { ChangeState(PlayerState.Dodge); return; }

        // blend 加速未完成 → 阻塞 Run 切换（防瞬移）
        if (!locomotion.IsBlendingComplete) return;

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
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

        // 弹刀
        if (Input.GetMouseButtonDown(1)) { ChangeState(PlayerState.Parry); return; }

        // Shift 点击 → 前冲
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            ChangeState(PlayerState.Dodge);
            return;
        }

        // blend 减速未完成 → 阻塞 Move 切换
        if (!locomotion.IsBlendingComplete) return;

        if (!Input.GetKey(KeyCode.LeftShift)) { ChangeState(PlayerState.Move); return; }
    }

    private void UpdateDodge()
    {
        dodgeTimer -= Time.deltaTime;
        if (dodgeTimer <= 0f)
        {
            if (moveInput.sqrMagnitude > 0.01f)
                ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            else
                ChangeState(PlayerState.Idle);
        }
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
            if (moveInput.sqrMagnitude > 0.01f)
                ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            else
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
        // 步骤 1：缓冲连击输入（按下即缓存，窗口内自动接上）
        if (Input.GetMouseButtonDown(0))
        {
            combat.BufferCombo();
        }

        // 步骤 2：窗口内消费缓冲的连击
        if (combat.ConsumeBufferedCombo())
        {
            animCtrl.TriggerNextAttack();
            StartCoroutine(DelayedHitDetection(_attackHitDelay));
            return;
        }

        // 步骤 3：挥刀倒计时
        if (combat.IsAttacking)
        {
            combat.DecrementTimer(Time.deltaTime);
            return;
        }

        // 步骤 4：强制退出保险
        if (combat.IncrementForceExitTimer(Time.deltaTime))
        {
            combat.ResetForceExitTimer();
            ChangeState(PlayerState.Idle);
            return;
        }

        // 步骤 5：动画已回到 Idle 才退出
        if (animCtrl.IsInState("Idle"))
        {
            combat.ResetForceExitTimer();
            ChangeState(PlayerState.Idle);
        }
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

    // ─── 地面检测 ─────────────────────────────────────
    private void CheckGrounded()
    {
        if (characterCollider == null) { isGrounded = true; return; }
        Vector3 center = characterCollider.bounds.center;
        float half = characterCollider.bounds.extents.y;
        Vector3 origin = center - Vector3.up * (half - 0.1f);
        isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayer);
    }

    // ─── 攻击辅助 ─────────────────────────────────────

    /// <summary>延迟 hit detection（仅碰撞检测+伤害，音效和顿帧由 Animation Event 驱动）</summary>
    private System.Collections.IEnumerator DelayedHitDetection(float delay)
    {
        yield return new WaitForSeconds(delay);
        combat.PerformHitDetection();
        // 以下两行配合 Animation Event 使用时可删：
        // combat.PlayHitSfx();
        // combat.TriggerHitStop();
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