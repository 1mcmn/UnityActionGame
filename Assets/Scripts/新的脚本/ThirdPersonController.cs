using UnityEngine;

public enum PlayerState { Idle, Move, Run, Dodge, Attack }

[RequireComponent(typeof(Rigidbody))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("控制与物理")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;

    [Header("加减速手感参数")]
    [SerializeField] private float accelSpeed = 2.5f;
    [SerializeField] private float decelSpeed = 6.0f;
    private float currentMovementValue = 0f;

    [Header("自动奔跑")]
    [SerializeField] private float autoRunDelay = 3f;
    private float autoRunTimer = 0f;
    private float _targetSpeed = 0f;

    [Header("闪避参数")]
    [SerializeField] private float dodgeDistance = 5f;
    [SerializeField] private float dodgeDuration = 0.4f;
    private float dodgeTimer = 0f;
    private bool isBackDodge = false;

    [Header("攻击参数")]
    [SerializeField] private float attackDuration = 0.45f;
    [SerializeField] private float attackDamage = 20f;
    [SerializeField] private float attackRadius = 2.5f;
    [SerializeField] private LayerMask enemyLayer;
    private float attackTimer;
    private float attackExitDelay = 0f; // 👑 收刀保护后摇
    private int comboStep = 0;

    private PlayerState currentState = PlayerState.Idle;
    private Rigidbody rb;
    private Vector3 moveInput;
    private float lastTargetSpeed = 0f;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Start() => ChangeState(PlayerState.Idle);

    private void Update()
    {
        Debug.Log($"==== 当前 State 是: {currentState} ===="); // 👑 加上这行
        ReadInput();
      

        switch (currentState)
        {
            case PlayerState.Idle: UpdateIdle(); break;
            case PlayerState.Move: UpdateMove(); break;
            case PlayerState.Run: UpdateRun(); break;
            case PlayerState.Dodge: UpdateDodge(); break;
            case PlayerState.Attack: UpdateAttack(); break;
        }

        UpdateAnimatorParams();
    }

    private void ChangeState(PlayerState newState)
    {
        if (currentState == newState) return;
        OnExitState(currentState);
        currentState = newState;
        OnEnterState(newState);
    }

    private void OnEnterState(PlayerState state)
    {
        if (state == PlayerState.Dodge)
        {
            dodgeTimer = dodgeDuration;
            Vector3 dodgeDirection = transform.forward;
            if (isBackDodge) dodgeDirection = -transform.forward;
            else if (moveInput.sqrMagnitude > 0.01f) dodgeDirection = moveInput.normalized;

            rb.velocity = Vector3.zero;
            rb.AddForce(dodgeDirection * dodgeDistance, ForceMode.Impulse);
            animator.SetTrigger("Dodge");
        }

        if (state == PlayerState.Attack)
        {
            comboStep = 1;
            attackTimer = attackDuration;
            attackExitDelay = 0.15f; // 👑 防止快速连点时动画没跟上
            animator.SetTrigger("Attack");
            PerformAttackHitDetection();
        }
    }

    private void OnExitState(PlayerState state)
    {
        if (state == PlayerState.Dodge)
        {
            currentMovementValue = 0f;
            lastTargetSpeed = 0f;
        }

        // 👑 新增：攻击结束时，把缓冲值绝对清零，确保下一刀能瞬发
        if (state == PlayerState.Attack)
        {
            currentMovementValue = 0f;
            lastTargetSpeed = 0f;
        }
    }

    // ===== 各状态 Update 逻辑 =====
    private void UpdateIdle()
    {
        // 👑 核心修复：攻击判定必须放在整个方法的第一行，绝对防御任何缓冲锁的拦截！
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log(">>> 检测到鼠标左键按下！正在尝试进入 Attack 状态");
            ChangeState(PlayerState.Attack);
            return;
        }

        // 只有没按攻击键，才能被下面这个急停缓冲锁拦截
        if (currentMovementValue > 0.02f) return;

        // 下面的正常待机逻辑
        if (Input.GetKeyDown(KeyCode.LeftShift)) { isBackDodge = true; ChangeState(PlayerState.Dodge); return; }
        if (moveInput.sqrMagnitude > 0.01f) ChangeState(PlayerState.Move);
    }

    private void UpdateMove()
    {
        if (moveInput.sqrMagnitude < 0.01f) { ChangeState(PlayerState.Idle); return; }
        if (Input.GetKeyDown(KeyCode.LeftShift)) { isBackDodge = false; ChangeState(PlayerState.Dodge); return; }
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

        if (currentMovementValue > 0.02f) return;
        if (Input.GetKey(KeyCode.LeftShift)) ChangeState(PlayerState.Run);
    }

    private void UpdateRun()
    {
        if (moveInput.sqrMagnitude < 0.01f) { ChangeState(PlayerState.Idle); return; }
        if (Input.GetKeyDown(KeyCode.LeftShift)) { isBackDodge = false; ChangeState(PlayerState.Dodge); return; }
        if (Input.GetMouseButtonDown(0)) { ChangeState(PlayerState.Attack); return; }

        if (currentMovementValue > 0.02f) return;
        if (!Input.GetKey(KeyCode.LeftShift)) ChangeState(PlayerState.Move);
    }

    private void UpdateDodge()
    {
        dodgeTimer -= Time.deltaTime;
        if (dodgeTimer <= 0f)
        {
            if (moveInput.sqrMagnitude > 0.01f)
                ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            else ChangeState(PlayerState.Idle);
        }
    }

    // 👑 修复后的攻击状态：删掉错误的哈希判断，加入后摇保护
    private void UpdateAttack()
    {
        // 1. 连击逻辑
        if (Input.GetMouseButtonDown(0) && attackTimer > 0f)
        {
            attackTimer = attackDuration;
            animator.ResetTrigger("NextAttack");
            animator.SetTrigger("NextAttack");
            PerformAttackHitDetection();
            return;
        }

        // 2. 挥刀倒计时
        if (attackTimer > 0f)
        {
            attackTimer -= Time.deltaTime;
            return;
        }

        // 3. 👑 核心：挥刀结束立刻判断 WASD
        if (moveInput.sqrMagnitude > 0.01f)
        {
            ChangeState(Input.GetKey(KeyCode.LeftShift) ? PlayerState.Run : PlayerState.Move);
            return;
        }

        // 4. 👑 新增：强制退出保险！如果 0.3 秒后还没回到 Idle，就强行切出去！
        attackExitDelay += Time.deltaTime;
        if (attackExitDelay >= 0.3f)
        {
            attackExitDelay = 0f;
            ChangeState(PlayerState.Idle);
            return;
        }

        // 5. 如果我们还是靠动画状态自己退（如果你已经把动画修好了，这行能正常跑）
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (!stateInfo.IsName("Idle"))
        {
            return;
        }
        attackExitDelay = 0f;
        ChangeState(PlayerState.Idle);
    }
    private void FixedUpdate()
    {
        if (currentState == PlayerState.Dodge)
        {
            rb.angularVelocity = Vector3.zero;
            return;
        }

        if (_targetSpeed == 0f && currentMovementValue > 0.02f)
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            return;
        }

        float speed = currentState == PlayerState.Move ? moveSpeed : moveSpeed * 1.6f;
        Vector3 velocity = rb.velocity;
        velocity.x = moveInput.x * speed;
        velocity.z = moveInput.z * speed;
        rb.velocity = velocity;

        if (moveInput.sqrMagnitude > 0.01f)
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(moveInput), 12f * Time.fixedDeltaTime));
    }

    private void UpdateAnimatorParams()
    {
        float targetSpeed = 0f;
        if (currentState == PlayerState.Move) targetSpeed = 1.0f;
        else if (currentState == PlayerState.Run) targetSpeed = 2.0f;
        _targetSpeed = targetSpeed;

        if (targetSpeed > 0f)
        {
            currentMovementValue = Mathf.MoveTowards(currentMovementValue, targetSpeed, accelSpeed * Time.deltaTime);
            lastTargetSpeed = targetSpeed;
        }
        else
        {
            currentMovementValue = Mathf.MoveTowards(currentMovementValue, 0f, decelSpeed * Time.deltaTime);
        }

        animator.SetFloat("Movement", currentMovementValue);
        animator.SetFloat("LastMoveSpeed", lastTargetSpeed);
    }

    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v).normalized;

        if (cameraTransform != null)
        {
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();
            moveInput = (forward * inputDir.z + right * inputDir.x).normalized;
        }
        else
        {
            moveInput = inputDir;
        }
    }

    private void PerformAttackHitDetection()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, attackRadius, enemyLayer);
        foreach (Collider col in colliders)
        {
            Enemy enemy = col.GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(attackDamage);
                Debug.Log($"攻击命中敌人！造成 {attackDamage} 点伤害");
            }
        }
    }
}