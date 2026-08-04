using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 游戏总控。管理 "开始界面 → 操控角色" 的切换。
/// 挂在一个始终存在的 GameObject 上（比如空物体 "GameManager"）。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("开始界面")]
    [SerializeField] private Camera _startCamera;
    [SerializeField] private GameObject _startUI;

    [Header("游戏中的摄像机")]
    [SerializeField] private Camera _mainCamera;

    [Header("玩家")]
    [SerializeField] private ThirdPersonController _player;

    [Header("敌人")]
    [SerializeField] private Transform _enemiesContainer;

    [Header("事件（可选）")]
    public UnityEvent OnGameStarted;

    private bool _hasStarted;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnterStartScreen();
    }

    private void Update()
    {
        if (!_hasStarted && Input.GetKeyDown(KeyCode.Space))
            StartGame();
    }

    /// <summary>进入开始界面</summary>
    private void EnterStartScreen()
    {
        _hasStarted = false;

        if (_startCamera != null)
            _startCamera.gameObject.SetActive(true);

        if (_startUI != null)
            _startUI.SetActive(true);

        if (_mainCamera != null)
            _mainCamera.gameObject.SetActive(false);

        if (_player != null)
            _player.gameObject.SetActive(false);

        SetEnemiesActive(false);

        // 显示鼠标（UI 需要用）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>UI 按钮点击时调用（在 Inspector 里绑到 Button 的 OnClick）</summary>
    public void StartGame()
    {
        if (_hasStarted) return;
        _hasStarted = true;

        if (_startCamera != null)
            _startCamera.gameObject.SetActive(false);

        if (_startUI != null)
            _startUI.SetActive(false);

        if (_mainCamera != null)
            _mainCamera.gameObject.SetActive(true);

        if (_player != null)
        {
            _player.gameObject.SetActive(true);
            _player.RefreshCameraReference(); // 修正 Awake 时 Camera.main 为 null 的问题
        }

        SetEnemiesActive(true);

        // 锁定鼠标（第三人称不需要鼠标光标）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        OnGameStarted?.Invoke();
    }

    private void SetEnemiesActive(bool active)
    {
        if (_enemiesContainer == null) return;
        for (int i = 0; i < _enemiesContainer.childCount; i++)
            _enemiesContainer.GetChild(i).gameObject.SetActive(active);
    }
}