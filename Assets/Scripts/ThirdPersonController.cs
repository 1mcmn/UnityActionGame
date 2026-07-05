using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerState
{
    Idle, Move, Attack, Dodge, Run, Jump
}

[RequireComponent(typeof(Rigidbody))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("基础移动")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private LayerMask groundLayer = ~0;

    [Header("跳跃")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    private Collider characterCollider;
    private bool jumpRequested;

    [Header("玩家属性")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    [Header("闪避")]
    [SerializeField] private float dodgeDistance = 5f;
    [SerializeField] private float dodgeDuration = 0.4f;
    private float dodgeTimer;

    [Header("攻击")]
    [SerializeField] private float attackDuration = 0.8f;
    [SerializeField] private float attackDamage = 20f;
    [SerializeField] private float attackRadius = 2f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("动画")]
    [SerializeField] private Animator animator;
    [SerializeField] private string idleAnimParam = "Idle";
    [SerializeField] private string walkAnimParam = "Walk";

    private PlayerState currentState = PlayerState.Idle;
    private float attackTimer;
    private readonly List<Enemy> hitEnemies = new List<Enemy>();

    [Header("音效")]
    [SerializeField] private AudioClip footstepClip;
    [SerializeField] private float footstepInterval = 0.4f;
    private float footstepTimer;

    private Rigidbody rb;
    private Vector3 moveInput;
    private bool isGrounded;
    private bool isInvulnerable = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        characterCollider = GetComponent<Collider>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        currentHealth = maxHealth;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        ChangeState(PlayerState.Idle);
        Time.timeScale = 1f;
        if (animator != null)
            animator.applyRootMotion = false;
    }

    private void Update()
    {
        ReadMovementInput();
        ReadJumpInput();
        TickAttackTimer();

        switch (currentState)
        {
            case PlayerState.Idle: UpdateIdle(); break;
            case PlayerState.Move: UpdateMove(); break;
            case PlayerState.Attack: UpdateAttack(); break;
            case PlayerState.Dodge: UpdateDodge(); break;
            case PlayerState.Run: UpdateRun(); break;
            case PlayerState.Jump: UpdateJump(); break;
        }
    }

    private void FixedUpdate()
    {
        CheckGrounded();

        switch (currentState)
        {
            case PlayerState.Idle: FixedUpdateIdle(); break;
            case PlayerState.Move: FixedUpdateMove(); break;
            case PlayerState.Attack: FixedUpdateAttack(); break;
            case PlayerState.Dodge: FixedUpdateDodge(); break;
            case PlayerState.Run: FixedUpdateRun(); break;
        }
    }

    // 👑 核心修改：抛弃累加模式，直接按帧驱动速度
    private void OnAnimatorMove()
    {
        if (currentState == PlayerState.Attack && animator.applyRootMotion)
        {
            // 直接把动画位移转换成刚体速度
            rb.velocity = animator.deltaPosition / Time.fixedDeltaTime;
        }
    }

    public void TryTakeDamage(float damage)
    {
        if (isInvulnerable) return;
        currentHealth -= damage;
        Debug.Log($"玩家受到 {damage} 点伤害，当前血量 {currentHealth}");
        if (currentHealth <= 0)
        {
            Debug.Log("玩家死亡！");
            return;
        }
        StartCoroutine(HitStopCoroutine(0.1f));
    }

    #region 状态机核心
    private void ChangeState(PlayerState newState)
    {
        if (currentState == newState) return;
        OnExitState(currentState);
        currentState = newState;
        OnEnterState(newState);
        UpdateAnimatorBools();
    }

    private void OnEnterState(PlayerState state)
    {
        if (state == PlayerState.Attack)
        {
            attackTimer = attackDuration;
            if (animator != null)
                animator.applyRootMotion = true;
            PerformAttackHitDetection();
        }

        if (state == PlayerState.Dodge)
        {
            dodgeTimer = dodgeDuration;
            isInvulnerable = true;
            animator.SetTrigger("Evade");
            Vector3 dodgeDir = GetFacingDirection();
            rb.velocity = new Vector3(dodgeDir.x * dodgeDistance, 0, dodgeDir.z * dodgeDistance);
        }
    }

    private void OnExitState(PlayerState state)
    {
        if (state == PlayerState.Attack)
        {
            hitEnemies.Clear();
            if (animator != null)
                animator.applyRootMotion = false;
            rb.WakeUp();
        }

        if (state == PlayerState.Dodge)
            isInvulnerable = false;
    }
    #endregion

    #region 各状态Update逻辑
    private void UpdateIdle()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift)) { ChangeState(PlayerState.Dodge); return; }
        if (Input.GetKey(KeyCode.LeftShift) && moveInput.sqrMagnitude > 0.01f) { ChangeState(PlayerState.Run); return; }
        if (Input.GetMouseButtonDown(0)) { animator.SetTrigger("Attack"); ChangeState(PlayerState.Attack); return; }
        if (moveInput.sqrMagnitude > 0.01f) ChangeState(PlayerState.Move);
    }

    private void UpdateMove()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift)) { ChangeState(PlayerState.Dodge); return; }
        if (Input.GetKey(KeyCode.LeftShift) && moveInput.sqrMagnitude > 0.01f) { ChangeState(PlayerState.Run); return; }
        if (Input.GetMouseButtonDown(0)) { animator.SetTrigger("Attack"); ChangeState(PlayerState.Attack); return; }
        if (moveInput.sqrMagnitude < 0.01f) ChangeState(PlayerState.Idle);
    }

    private void UpdateRun()
    {
        if (animator != null && !animator.GetCurrentAnimatorStateInfo(0).IsName("Run"))
            animator.Play("Run");
        if (Input.GetKeyDown(KeyCode.LeftShift)) { ChangeState(PlayerState.Dodge); return; }
        if (!Input.GetKey(KeyCode.LeftShift)) { ChangeState(PlayerState.Move); return; }
        if (Input.GetMouseButtonDown(0)) { animator.SetTrigger("Attack"); ChangeState(PlayerState.Attack); return; }
        if (moveInput.sqrMagnitude < 0.01f) ChangeState(PlayerState.Idle);
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

    // 👑 核心修正：连击触发必须在 attackTimer 内，且不再重新设定 exitDelay
    private void UpdateAttack()
    {
        // 只有在“挥砍”期间（attackTimer > 0）允许触发连击
        if (Input.GetMouseButtonDown(0) && attackTimer > 0f)
        {
            animator.ResetTrigger("NextAttack");
            attackTimer = attackDuration; // 重置攻击计时器
            animator.SetTrigger("NextAttack");
            PerformAttackHitDetection();
        }

        // 挥砍阶段直接返回
        if (attackTimer > 0f) return;

        // 挥砍结束，进入收刀后摇 (attackExitDelay已移除，完全交由Animator过渡)
        // 此时读取 WASD 输入，在动画播完切回 Idle 的瞬间，立刻切换至 Move/Run
        if (moveInput.sqrMagnitude > 0.01f)
            ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
        else
            ChangeState(PlayerState.Idle);
    }

    private void UpdateJump()
    {
        if (isGrounded)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            ChangeState(new Vector3(h, 0, v).sqrMagnitude > 0.01f ? PlayerState.Move : PlayerState.Idle);
        }
    }
    #endregion

    #region 各状态FixedUpdate逻辑
    private void FixedUpdateIdle()
    {
        ApplyJump();
        HaltHorizontalVelocity();
    }

    private void FixedUpdateMove()
    {
        ApplyJump();
        ApplyMovementVelocity(moveSpeed);
        RotateTowardDirection(moveInput);

        if (isGrounded && moveInput.sqrMagnitude > 0.01f)
        {
            footstepTimer -= Time.fixedDeltaTime;
            if (footstepTimer <= 0f)
            {
                AudioSource.PlayClipAtPoint(footstepClip, transform.position, 0.6f);
                footstepTimer = footstepInterval;
            }
        }
        else
        {
            footstepTimer = 0f;
        }
    }

    // 👑 核心修正：攻击状态下，物理运动完全由 OnAnimatorMove 接管，绝不叠加 moveInput
    private void FixedUpdateAttack()
    {
        // 攻击时禁止转向，否则会在连击时造成旋转漂移
        // 保持逻辑纯净，不影响 OnAnimatorMove 的速度驱动
        rb.angularVelocity = Vector3.zero;
    }

    private void FixedUpdateDodge() { }

    private void FixedUpdateRun()
    {
        ApplyJump();
        ApplyMovementVelocity(moveSpeed * 1.6f);
        RotateTowardDirection(moveInput);
    }
    #endregion

    #region 工具方法
    private void PerformAttackHitDetection()
    {
        hitEnemies.Clear();
        Collider[] colliders = Physics.OverlapSphere(transform.position, attackRadius, enemyLayer);
        foreach (Collider col in colliders)
        {
            Enemy enemy = col.GetComponent<Enemy>();
            if (enemy != null && !hitEnemies.Contains(enemy))
            {
                hitEnemies.Add(enemy);
                enemy.TakeDamage(attackDamage, (enemy.transform.position - transform.position).normalized);
            }
        }

        if (hitEnemies.Count > 0 && CameraShake.Instance != null)
        {
            CameraShake.Instance.Shake(0.1f, 0.3f);
            StartCoroutine(HitStopCoroutine(0.05f));
        }
    }

    private IEnumerator HitStopCoroutine(float duration)
    {
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
    }

    private void UpdateAnimatorBools()
    {
        if (animator == null) return;
        bool isMoving = (currentState == PlayerState.Move || currentState == PlayerState.Run);
        animator.SetBool(walkAnimParam, isMoving);
        animator.SetBool("Run", currentState == PlayerState.Run);
        animator.SetBool(idleAnimParam, currentState == PlayerState.Idle);
    }

    private void TickAttackTimer()
    {
        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;
    }

    private void ReadMovementInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (cameraTransform != null)
        {
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0; right.y = 0;
            forward.Normalize(); right.Normalize();
            moveInput = (forward * v + right * h).normalized;
        }
        else
        {
            moveInput = new Vector3(h, 0f, v).normalized;
        }
    }

    private void ReadJumpInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftControl) && isGrounded
            && (currentState == PlayerState.Idle || currentState == PlayerState.Move || currentState == PlayerState.Run))
        {
            jumpRequested = true;
        }
    }

    private Vector3 GetFacingDirection()
    {
        if (moveInput.sqrMagnitude > 0.01f) return moveInput;
        if (cameraTransform != null)
        {
            Vector3 forward = cameraTransform.forward;
            forward.y = 0;
            return forward.normalized;
        }
        return transform.forward;
    }

    private void ApplyMovementVelocity(float speed)
    {
        Vector3 vel = moveInput * speed;
        vel.y = rb.velocity.y;
        rb.velocity = vel;
    }

    private void HaltHorizontalVelocity()
    {
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    private void ApplyJump()
    {
        if (!jumpRequested) return;
        rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        jumpRequested = false;
        ChangeState(PlayerState.Jump);
    }

    private void CheckGrounded()
    {
        Vector3 center = characterCollider.bounds.center;
        float halfHeight = characterCollider.bounds.extents.y;
        Vector3 origin = center - Vector3.up * (halfHeight - 0.1f);
        isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayer);
    }

    private void RotateTowardDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 12f * Time.fixedDeltaTime));
    }

    private void SnapFacing(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.01f) return;
        rb.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }
    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawLine(transform.position + Vector3.down * 0.1f,
            transform.position + Vector3.down * 0.1f + Vector3.down * 0.3f);

        Gizmos.color = new Color(1f, 0.3f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackRadius);
    }
#endif
}