using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerLocomotion : MonoBehaviour
{
    [Header("移动速度")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float runSpeedMultiplier = 1.6f;

    [Header("加减速手感")]
    [SerializeField] private float accelSpeed = 6.0f;
    [SerializeField] private float decelSpeed = 6.0f;

    private Rigidbody rb;
    private Transform cameraTransform;
    private CameraFollow cameraFollow;
    private Vector3 lastPlanarForward = Vector3.forward;
    private Vector3 lastInputDirection, lastPhysicalPosition, lastPhysicalDirection;
    private bool hasPhysicalPosition;

    private float currentBlend;
    private float lastTarget;
    public float CurrentBlend => currentBlend;
    public bool IsBlendingComplete => currentBlend < 0.02f;

    public void Initialize(Rigidbody rigidbody, Transform camTransform)//存好刚体和摄像机
    {
        rb = rigidbody;
        cameraTransform = camTransform;
        cameraFollow = camTransform != null ? camTransform.GetComponent<CameraFollow>() : null;
        if (camTransform != null) lastPlanarForward = Quaternion.Euler(0f, camTransform.eulerAngles.y, 0f) * Vector3.forward;
        hasPhysicalPosition = rb != null;
        if (hasPhysicalPosition) lastPhysicalPosition = rb.position;
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

    public void ResetBlending(float initialValue = 0f)
    {
        currentBlend = Mathf.Max(0f, initialValue);
        lastTarget = currentBlend;
    }

    /// <summary>仅计算位移；唯一应用点在 PlayerAnimController.OnAnimatorMove。</summary>
    public Vector3 GetDisplacement(Vector3 moveInput, bool isRunning, float deltaTime)
    {
        float speed = isRunning ? moveSpeed * runSpeedMultiplier : moveSpeed;
        return Vector3.ClampMagnitude(moveInput, 1f) * speed * deltaTime;
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
    /// 闪避起手清理物理残留；闪避位移由动画根位移提供。
    /// </summary>
    public void StartDodge(Vector3 direction)
    {
        if (rb == null) return;
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 根据相机朝向计算 WASD 在世界空间的方向。
    /// </summary>
    public Vector3 GetCameraRelativeInput(float h, float v)
    {
        Vector3 input = new Vector3(h, 0f, v).normalized;
        if (cameraTransform == null) return lastInputDirection = input;

        Vector3 forward = cameraFollow != null && cameraFollow.isActiveAndEnabled ? cameraFollow.PlanarForward : cameraTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > .0001f) lastPlanarForward = forward.normalized;
        forward = lastPlanarForward;
        // 水平右向必须由前向重新构造，独立投影相机right会在倾斜/侧滚时形成斜基底。
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        return lastInputDirection = (forward * input.z + right * input.x).normalized;
    }

    /// <summary>键盘方向键正在使用时优先采用键盘，避免同名手柄轴混入横向漂移。</summary>
    public static Vector2 ResolveMoveAxes(Vector2 axisInput, bool left, bool right, bool forward, bool back)
    {
        if (left || right || forward || back)
            return new Vector2((right ? 1f : 0f) - (left ? 1f : 0f), (forward ? 1f : 0f) - (back ? 1f : 0f));
        return Vector2.ClampMagnitude(axisInput, 1f);
    }

    private void LateUpdate()
    {
        if (rb == null) return;
        Vector3 delta = rb.position - lastPhysicalPosition;
        delta.y = 0f;
        if (hasPhysicalPosition && delta.sqrMagnitude > .000001f) lastPhysicalDirection = delta.normalized;
        lastPhysicalPosition = rb.position;
        hasPhysicalPosition = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Vector3 origin = transform.position + Vector3.up * .25f;
        // 蓝：物理根朝向；绿：当前输入方向；红：最近一次实际水平位移。
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(origin, origin + transform.forward * 1.5f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + lastInputDirection * 1.3f);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + lastPhysicalDirection * 1.1f);
    }
}
