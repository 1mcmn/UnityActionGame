using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Header("关键偏移设置")]
    [Tooltip("调整 Y 轴和 Z 轴来改变角色在屏幕上的位置")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 5f, -6f); // Y抬高，Z拉远，角色就会在屏幕下方

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

    private void Start()
    {
        if (target == null) return;
        yaw = 0f;
        pitch = 15f; // 初始带一点俯视角度
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

        // 👑 核心修复：根据鼠标角度，计算出环绕角色的正确偏移！
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        desiredPosition = target.position + orbitRotation * offset;

        // 让摄像机看向角色的腰部偏上一点（这样角色就会在屏幕最下方）
        Vector3 lookTarget = target.position + Vector3.up * 1.5f;
        desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);

        // 平滑插值
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