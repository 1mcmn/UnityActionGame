using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerLocomotion : MonoBehaviour
{
    [Header("移动速度")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float runSpeedMultiplier = 1.6f;

    [Header("加减速手感")]
    [SerializeField] private float accelSpeed = 2.5f;
    [SerializeField] private float decelSpeed = 6.0f;

    [Header("闪避")]
    [SerializeField] private float dodgeDistance = 10f;
    [SerializeField] private float dodgeDuration = 0.4f;

    private Rigidbody rb;
    private Transform cameraTransform;

    private float currentBlend;
    private float lastTarget;
    public float CurrentBlend => currentBlend;
    public bool IsBlendingComplete => currentBlend < 0.02f;

    public void Initialize(Rigidbody rigidbody, Transform camTransform)
    {
        rb = rigidbody;
        cameraTransform = camTransform;
    }

    /// <summary>
    /// 每帧 Update 中调用，驱动 Movement 浮点值的加速/减速融合。
    /// target: 1.0 = Move, 2.0 = Run, 0 = 停止
    /// </summary>
    public (float blend, float lastTarget) TickBlending(float target, float deltaTime)
    {
        if (target > 0f)
        {
            currentBlend = Mathf.MoveTowards(currentBlend, target, accelSpeed * deltaTime);
            lastTarget = target;
        }
        else
        {
            currentBlend = Mathf.MoveTowards(currentBlend, 0f, decelSpeed * deltaTime);
        }
        return (currentBlend, lastTarget);
    }

    public void ResetBlending()
    {
        currentBlend = 0f;
        lastTarget = 0f;
    }

    /// <summary>
    /// FixedUpdate 中调用。根据当前移动输入施加世界速度。
    /// blendTarget 为当前动画融合目标（0=Idle, 1=Move, 2=Run）。
    /// 当 blendTarget=0 且 blend 未完成时，清零水平速度以匹配动画减速。
    /// </summary>
    public void ApplyMovement(Vector3 moveInput, bool isRunning, float blendTarget)
    {
        if (rb == null) return;

        // 减速至停止：参考代码在 _targetSpeed==0 && currentMovementValue>0.02f 时清零
        if (blendTarget == 0f && !IsBlendingComplete)
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            return;
        }

        float speed = isRunning ? moveSpeed * runSpeedMultiplier : moveSpeed;
        rb.velocity = new Vector3(moveInput.x * speed, rb.velocity.y, moveInput.z * speed);
    }

    /// <summary>
    /// FixedUpdate 中调用。平滑转向移动方向。
    /// </summary>
    public void RotateToward(Vector3 direction)
    {
        if (rb == null || direction.sqrMagnitude < 0.01f) return;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(direction), 12f * Time.fixedDeltaTime));
    }

    /// <summary>
    /// 立即停止水平移动（保留 Y 轴速度用于重力）。
    /// </summary>
    public void Halt()
    {
        if (rb != null) rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    /// <summary>
    /// 闪避：清空水平速度，施加方向冲量（保留 Y 轴速度避免打断重力）。
    /// </summary>
    public void StartDodge(Vector3 direction)
    {
        if (rb == null) return;
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(direction.normalized * dodgeDistance, ForceMode.Impulse);
    }

    /// <summary>
    /// 根据相机朝向计算 WASD 在世界空间的方向。
    /// </summary>
    public Vector3 GetCameraRelativeInput(float h, float v)
    {
        Vector3 input = new Vector3(h, 0f, v).normalized;
        if (cameraTransform == null) return input;

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f; right.y = 0f;
        forward.Normalize(); right.Normalize();
        return (forward * input.z + right * input.x).normalized;
    }
}