using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Header("关键偏移设置")]
    [Tooltip("调整 Y 轴和 Z 轴来改变角色在屏幕上的位置")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 5f, -6f);

    [SerializeField] private float mouseSensitivityX = 3f;
    [SerializeField] private float mouseSensitivityY = 2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;
    [SerializeField] private float positionSmoothSpeed = 8f;
    [SerializeField] private float rotationSmoothSpeed = 20f;
    [SerializeField] private bool lockCursorOnStart = true;

    private float yaw;
    private float pitch;
    private Vector3 desiredPosition;
    private Quaternion desiredRotation;
    private bool orientationInitialized;

    /// <summary>以相机目标朝向计算输入基准，避免跟随平滑的暂时转角让直行路线弯曲。</summary>
    public Vector3 PlanarForward
    {
        get
        {
            Vector3 forward = orientationInitialized
                ? -(Quaternion.Euler(pitch, yaw, 0f) * offset) : transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > .0001f ? forward.normalized : Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }
    }

    private void Start()
    {
        if (target == null) return;
        yaw = transform.eulerAngles.y;
        pitch = 15f;
        orientationInitialized = true;
        if (lockCursorOnStart)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        HandleCursorLock();
        HandleMouseLook();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        desiredPosition = target.position + orbitRotation * offset;

        Vector3 lookTarget = target.position + Vector3.up * 1.5f;
        desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);

        float positionT = 1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);
        float rotationT = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
    }

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivityX;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivityY;
        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void HandleCursorLock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
