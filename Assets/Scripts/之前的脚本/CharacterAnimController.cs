using UnityEngine;

[RequireComponent(typeof(Animator), typeof(Rigidbody))] // 👑 核心修复：强制要求必须有刚体
public class CharacterAnimController : MonoBehaviour
{
    // ========== Animator参数哈希 ==========
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int LastMoveSpeedHash = Animator.StringToHash("LastMoveSpeed");
    private static readonly int DoAttackHash = Animator.StringToHash("DoAttack");
    private static readonly int DoDodgeHash = Animator.StringToHash("DoDodge");

    // ========== 组件引用 ==========
    private Animator _animator;
    private Rigidbody rb; // 👑 核心修复：声明刚体变量

    // ========== 可配置参数 ==========
    [Header("移动参数")]
    public float walkSpeed = 2f;
    public float runSpeed = 5f;
    public float speedThreshold = 0.1f;

    // 缓存的输入
    private Vector3 moveInput;
    private float currentSpeed;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>(); // 👑 核心修复：获取刚体组件

        // 防止角色像保龄球一样摔倒
        if (rb != null) rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        // 👑 逻辑分离：读取输入放在 Update 中（响应最快）
        HandleMovementInput();
        HandleActionInput();
    }

    private void FixedUpdate()
    {
        // 👑 逻辑分离：物理移动和旋转放在 FixedUpdate 中（物理引擎专用，解决乱飞问题）
        HandleMovementPhysics();
    }

    // 读取输入并更新 Animator 参数
    private void HandleMovementInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // 强制水平移动（Y轴永远为0，彻底解决低头看地的问题）
        moveInput = new Vector3(h, 0f, v).normalized;

        float maxSpeed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        currentSpeed = moveInput.magnitude * maxSpeed;

        // 驱动动画器参数
        _animator.SetFloat(SpeedHash, currentSpeed);
        if (currentSpeed > speedThreshold)
        {
            _animator.SetFloat(LastMoveSpeedHash, currentSpeed);
        }
    }

    // 执行物理移动与旋转
    private void HandleMovementPhysics()
    {
        if (rb == null) return;

        if (moveInput.magnitude > 0.01f)
        {
            // 👑 使用刚体速度移动，保留 Y 轴速度（不干扰重力）
            rb.velocity = new Vector3(moveInput.x * currentSpeed, rb.velocity.y, moveInput.z * currentSpeed);

            // 执行物理转向
            Quaternion targetRot = Quaternion.LookRotation(moveInput, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 12f * Time.fixedDeltaTime));
        }
        else
        {
            // 松手时，水平速度归零，保留重力
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        }
    }

    // 处理攻击、闪避等一次性动作
    private void HandleActionInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _animator.SetTrigger(DoAttackHash);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            _animator.SetTrigger(DoDodgeHash);
        }
    }
}