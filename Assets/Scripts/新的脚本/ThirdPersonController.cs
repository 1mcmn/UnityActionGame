using System;
using UnityEngine;

public enum PlayerState { Idle, Move, Run, Dodge, Attack, Parry }

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
    [Tooltip("攻击时向敌人突进的速度")]
    [SerializeField] private float _attackLungeSpeed  = 3f;

    [Header("弹刀")]
    [Tooltip("弹刀动画持续时间")]
    [SerializeField] private float _parryDuration     = 0.4f;
    private Collider characterCollider;
    private bool isGrounded;

    private PlayerState currentState = PlayerState.Idle;
    private Rigidbody rb;
    private Vector3 moveInput;
    private float dodgeTimer;
    private float parryTimer;
    private bool  _parryTriggered;

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

    /// <summary>刷新摄像机引用（开始界面进入游戏后调用）</summary>
    public void RefreshCameraReference()
    {
        if (locomotion != null && Camera.main != null)
            locomotion.Initialize(rb, Camera.main.transform);
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
            case PlayerState.Parry: UpdateParry(); break;
        }

        TickAnimatorBlend();
    }

    private void FixedUpdate()
    {
        // Attack/Dodge：消费 OnAnimatorMove 缓存的根运动 delta，通过刚体管线同步。
        if (currentState == PlayerState.Attack || currentState == PlayerState.Dodge || currentState == PlayerState.Parry)
        {
            var (deltaPos, deltaRot) = animCtrl.ConsumeRootMotionDelta();
            if (deltaPos != Vector3.zero)
            {
                // 攻击时防止穿过敌人：检测前方敌人，限制前移距离
                if (currentState == PlayerState.Attack)
                    deltaPos = ClampForwardMotion(deltaPos);

                rb.MovePosition(rb.position + deltaPos);
            }
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
                FaceNearestEnemy();
                StartCoroutine(DelayedHitDetection(_attackHitDelay));
                break;

            case PlayerState.Parry:
                parryTimer = _parryDuration;
                combat.SetInvulnerable(true);
                animCtrl.SetSyncRootMotion(true);
                animCtrl.TriggerParry();
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

            case PlayerState.Parry:
                combat.SetInvulnerable(false);
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
        // WASD 松开 → Idle
        if (moveInput.sqrMagnitude < 0.01f) { ChangeState(PlayerState.Idle); return; }

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

    private void UpdateAttack()
    {
        // 步骤 1：连击窗口（等价参考代码步骤 1）
        if (Input.GetMouseButtonDown(0) && combat.CanCombo)
        {
            combat.TryCombo();
            animCtrl.TriggerNextAttack();
            StartCoroutine(DelayedHitDetection(_attackHitDelay));
            return;
        }

        // 步骤 2：挥刀倒计时（等价参考代码步骤 2）
        if (combat.IsAttacking)
        {
            combat.DecrementTimer(Time.deltaTime);
            return;
        }

        // 步骤 3：挥刀结束 → 等动画回到 BlendTree 再响应 WASD（见步骤 5）
        // 不做切换，继续往下走步骤 4 和步骤 5

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

    /// <summary>找到最近的敌人（通过 combat 的 enemyLayer）</summary>
    private Transform FindNearestEnemy()
    {
        if (combat == null) return null;

        LayerMask mask = combat.EnemyLayer;
        Collider[] cols = Physics.OverlapSphere(transform.position, 4f, mask);
        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (Collider c in cols)
        {
            Vector3 toTarget = c.transform.position - transform.position;
            toTarget.y = 0f;
            float d = toTarget.sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = c.transform;
            }
        }
        return best;
    }
}