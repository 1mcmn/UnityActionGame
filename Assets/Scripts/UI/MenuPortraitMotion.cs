using UnityEngine;

/// <summary>菜单展示模型绕头部中心旋转，与战斗角色和物理移动无关。</summary>
public sealed class MenuPortraitMotion : MonoBehaviour
{
    [SerializeField] private Transform pivot;
    [SerializeField] private Camera portraitCamera;
    [SerializeField, Range(.5f, .9f)] private float portraitScreenX = .77f;
    [SerializeField, Range(0f, 45f)] private float yawLimit = 20f;
    [SerializeField, Range(0f, 25f)] private float pitchLimit = 10f;
    [SerializeField, Min(0.1f)] private float followSpeed = 7f;
    private Quaternion initialRotation;
    private bool focused = true;

    public void Configure(Transform target, Camera camera) { pivot = target; portraitCamera = camera; }
    public void ConfigureFraming(float screenX, float yaw, float pitch)
    { portraitScreenX = screenX; yawLimit = yaw; pitchLimit = pitch; }

    private void Awake()
    {
        if (pivot == null) pivot = transform;
        initialRotation = pivot.localRotation;
    }

    private void OnApplicationFocus(bool value) { focused = value; }

    public Quaternion RotationForPointer(Vector2 position, Vector2 size)
    {
        float x = Mathf.Clamp(position.x / Mathf.Max(1f, size.x) * 2f - 1f, -1f, 1f);
        float y = Mathf.Clamp(position.y / Mathf.Max(1f, size.y) * 2f - 1f, -1f, 1f);
        // 展示相机从模型的 +Z 侧看向脸，屏幕右侧对应模型的 -X。
        return initialRotation * Quaternion.Euler(-y * pitchLimit, -x * yawLimit, 0f);
    }

    private void LateUpdate()
    {
        // 随宽高比调整水平构图，避免窄窗口把头像推到屏幕外。
        if (portraitCamera != null && portraitCamera.orthographic)
        {
            Vector3 position = portraitCamera.transform.position;
            position.x = pivot.position.x + (portraitScreenX - .5f) * 2f *
                portraitCamera.orthographicSize * portraitCamera.aspect;
            portraitCamera.transform.position = position;
        }
        Quaternion target = focused
            ? RotationForPointer(Input.mousePosition, new Vector2(Screen.width, Screen.height))
            : initialRotation;
        pivot.localRotation = Quaternion.Slerp(pivot.localRotation, target,
            1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
    }
}
