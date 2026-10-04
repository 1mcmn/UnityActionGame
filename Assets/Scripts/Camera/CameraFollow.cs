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

    [Header("命中震动")]
    [Tooltip("整体震动倍率，0 = 关闭")]
    [SerializeField] private float shakeScale = 1f;
    [SerializeField] private float shakeAmplitude = .06f;
    [SerializeField] private float shakeDuration = .16f;

    private float yaw;
    private float pitch;
    private Vector3 desiredPosition;
    private Quaternion desiredRotation;
    private bool orientationInitialized;
    private Vector3 smoothedPosition;
    private float framing;
    private Vector3 framingPoint;

    /// <summary>次要取景目标（世界坐标）；挑飞时由浮空模块设置为敌人身体中心，结束后关闭。</summary>
    public bool HasSecondaryFocus { get; set; }
    public Vector3 SecondaryFocus { get; set; }
    private const float IntroDuration = 1.2f;
    private float introTime = IntroDuration;

    /// <summary>菜单进入战斗时由转场幕布调用：镜头从较远处推近；同时立即对齐位置，避免从场景摆放处滑过来。</summary>
    public void PlayIntro() { introTime = 0f; snapNext = true; }
    private bool snapNext;
    private bool smoothedInitialized;
    private float shakeStrength, shakeTime, shakeLength, shakeSeed;

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

    private void OnEnable() => CombatImpact.OnImpact += OnImpact;
    private void OnDisable() => CombatImpact.OnImpact -= OnImpact;

    /// <summary>震动按现实时间衰减，顿帧期间仍然可见；只叠加在平滑后的位置上，不污染跟随。</summary>
    private void OnImpact(CombatImpact.Info info)
    {
        float strength = info.strength * shakeScale;
        if (strength <= 0f) return;
        if (strength >= shakeStrength * Mathf.Clamp01(1f - shakeTime / Mathf.Max(.0001f, shakeLength)))
        { shakeStrength = strength; shakeTime = 0f; shakeLength = shakeDuration * Mathf.Lerp(1f, 1.8f, Mathf.InverseLerp(1f, 3f, strength)); shakeSeed = Random.value * 100f; }
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
        // 双目标取景：注视点从主角向次要目标（如浮空敌人）偏移 35%，两者相距越远镜头拉得越远，保证同框。
        framing = Mathf.Lerp(framing, HasSecondaryFocus ? 1f : 0f, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
        if (HasSecondaryFocus) framingPoint = SecondaryFocus;
        Vector3 anchor = target.position + Vector3.up * 1.5f;
        Vector3 toSecondary = framingPoint - anchor;
        float pull = 1f + Mathf.Clamp(toSecondary.magnitude * framing / 5f, 0f, .7f);
        // 开场推近：揭幕时从 1.6 倍距离缓出到正常位置。
        if (introTime < IntroDuration) { introTime += Time.unscaledDeltaTime; float p = Mathf.Clamp01(introTime / IntroDuration); pull *= Mathf.Lerp(1.6f, 1f, 1f - Mathf.Pow(1f - p, 3f)); }
        desiredPosition = target.position + orbitRotation * (offset * pull);

        Vector3 lookTarget = anchor + toSecondary * (.35f * framing);
        desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);

        float positionT = 1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime);
        if (!smoothedInitialized) { smoothedPosition = transform.position; smoothedInitialized = true; }
        if (snapNext) { snapNext = false; smoothedPosition = desiredPosition; transform.rotation = desiredRotation; }
        smoothedPosition = Vector3.Lerp(smoothedPosition, desiredPosition, positionT);
        transform.position = smoothedPosition + ShakeOffset();
        float rotationT = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
    }

    private Vector3 ShakeOffset()
    {
        if (shakeStrength <= 0f) return Vector3.zero;
        shakeTime += Time.unscaledDeltaTime;
        float t = shakeTime / Mathf.Max(.0001f, shakeLength);
        if (t >= 1f) { shakeStrength = 0f; return Vector3.zero; }
        float fade = (1f - t) * (1f - t), frequency = 38f, time = shakeTime * frequency;
        float x = Mathf.PerlinNoise(shakeSeed, time) * 2f - 1f, y = Mathf.PerlinNoise(shakeSeed + 17f, time) * 2f - 1f;
        return (transform.right * x + transform.up * y * .7f) * (shakeAmplitude * shakeStrength * fade);
    }

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        float sensitivity = GameSettings.Sensitivity;
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivityX * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivityY * sensitivity;
        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void HandleCursorLock()
    {
        // 暂停菜单、结算界面接管鼠标与 ESC；此时不再由镜头抢回锁定。
        if (GamePause.IsPaused || (GameManager.Instance != null && GameManager.Instance.IsGameOver)) return;
        HandleLegacyCursorLock();
    }

    private void HandleLegacyCursorLock()
    {
        if (!PauseMenu.Present && Input.GetKeyDown(KeyCode.Escape))
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
