using System;
using UnityEngine;

public enum PlayerState { Idle, Move, Run, Dodge, Attack }

[RequireComponent(typeof(Rigidbody))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("子模块")]
    [SerializeField] private PlayerLocomotion locomotion;
    [SerializeField] private PlayerAnimController animCtrl;
    [SerializeField] private PlayerCombat combat;

    [Header("地面检测")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField] private float groundCheckDistance = 0.2f;
    private Collider characterCollider;
    private bool isGrounded;

    private PlayerState currentState = PlayerState.Idle;
    private Rigidbody rb;
    private Vector3 moveInput;
    private float dodgeTimer;

    // 当前帧的动画融合目标（只读，供 ApplyMovement 使用）
    private float blendTarget;

    // 公开访问器（向后兼容参考代码的外部调用）
    public float CurrentHealth => combat != null ? combat.CurrentHealth : 0f;
    public static event Action<float> OnPlayerDamaged;

    public void TryTakeDamage(float damage)
    {
        if (combat != null)
        {
            combat.TakeDamage(damage);
            OnPlayerDamaged?.Invoke(combat.CurrentHealth);
        }
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

        locomotion.Initialize(rb, Camera.main != null ? Camera.main.transform : null);
        animCtrl.Initialize();
        combat.Initialize();

        // 订阅内部伤害事件 → 转发到本类静态事件
        PlayerCombat.OnPlayerDamaged += (hp) => OnPlayerDamaged?.Invoke(hp);
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
        }

        TickAnimatorBlend();
    }

    private void FixedUpdate()
    {
        // Attack/Dodge：消费 OnAnimatorMove 缓存的根运动 delta，通过刚体管线同步。
        if (currentState == PlayerState.Attack || currentState == PlayerState.Dodge)
        {
            var (deltaPos, deltaRot) = animCtrl.ConsumeRootMotionDelta();
            if (deltaPos != Vector3.zero)
                rb.MovePosition(rb.position + deltaPos);
            if (deltaRot != Quaternion.identity)
                rb.MoveRotation(rb.rotation * deltaRot);
            return;
        }

        bool isRunning = currentState == PlayerState.Run;
        locomotion.ApplyMovement(moveInput, isRunning, blendTarget);
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
                animCtrl.SetSyncRootMotion(true);
                animCtrl.TriggerAttack();
                combat.PerformHitDetection(transform.position);
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
                animCtrl.SetSyncRootMotion(false);
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
        // WASD 松开 → Idle
        if (moveInput.sqrMagnitude < 0.01f) { ChangeState(PlayerState.Idle); return; }

        // 攻击
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

        // Shift 点击 → 前冲
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            ChangeState(PlayerState.Dodge);
            return;
        }

        // blend 加速未完成 → 阻塞 Run 切换（防闪烁）
        if (!locomotion.IsBlendingComplete) return;

        if (Input.GetKey(KeyCode.LeftShift)) { ChangeState(PlayerState.Run); return; }
    }

    private void UpdateRun()
    {
        // WASD 松开 → Idle
        if (moveInput.sqrMagnitude < 0.01f) { ChangeState(PlayerState.Idle); return; }

        // 攻击
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

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

    private void UpdateAttack()
    {
        // 步骤 1：连击窗口（等价参考代码步骤 1）
        if (Input.GetMouseButtonDown(0) && combat.CanCombo)
        {
            combat.TryCombo();
            animCtrl.TriggerNextAttack();
            combat.PerformHitDetection(transform.position);
            return;
        }

        // 步骤 2：挥刀倒计时（等价参考代码步骤 2）
        if (combat.IsAttacking)
        {
            combat.DecrementTimer(Time.deltaTime);
            return;
        }

        // 步骤 3：挥刀结束立刻判断 WASD（等价参考代码步骤 3）
        if (moveInput.sqrMagnitude > 0.01f)
        {
            ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            return;
        }

        // 步骤 4：强制退出保险（等价参考代码步骤 4）
        if (combat.IncrementForceExitTimer(Time.deltaTime))
        {
            combat.ResetForceExitTimer();
            ChangeState(PlayerState.Idle);
            return;
        }

        // 步骤 5：动画已回到 Idle 才退出（等价参考代码 !stateInfo.IsName("Idle")）
        // 注意：不能用 !IsInState("Attack")，因为连击后动画在 Attack2/Attack3，
        //       此时 IsInState("Attack") 为 false 会错误截断连击。
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
}