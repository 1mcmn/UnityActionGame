using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 游戏总控。管理 "开始界面 → 操控角色 → 胜负结算 → 重启" 的完整闭环。
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

    [Header("结算 UI")]
    [SerializeField] private GameObject _endPanel;
    [SerializeField] private Text _endText;
    [SerializeField] private Button _restartButton;

    [Header("敌人状态条（屏幕下方）")]
    [SerializeField] private BossHealthBarUI _bossHealthBar;

    [Header("死亡表现")]
    [Tooltip("玩家死亡动画播放多久后弹出结算 UI（秒）")]
    [SerializeField] private float _deathDelay = 2f;

    [Header("事件（可选）")]
    public UnityEvent OnGameStarted;

    private bool _hasStarted;
    private bool _gameEnded;
    private int _aliveEnemyCount;

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
        _gameEnded = false;

        if (_startCamera != null)
            _startCamera.gameObject.SetActive(true);

        if (_startUI != null)
            _startUI.SetActive(true);

        if (_mainCamera != null)
            _mainCamera.gameObject.SetActive(false);

        if (_player != null)
            _player.gameObject.SetActive(false);

        SetEnemiesActive(false);

        if (_endPanel != null)
            _endPanel.SetActive(false);

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

        // 玩家已激活、血量已初始化，刷新血条显示
        Object.FindObjectOfType<HealthBarUI>()?.Refresh();

        SetEnemiesActive(true);
        RegisterEnemies();

        // 订阅玩家死亡事件（先退订避免重复）
        PlayerCombat.OnPlayerDeath -= OnPlayerDeath;
        PlayerCombat.OnPlayerDeath += OnPlayerDeath;

        // 锁定鼠标（第三人称不需要鼠标光标）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        OnGameStarted?.Invoke();
    }

    /// <summary>统计并订阅所有敌人的死亡事件</summary>
    private void RegisterEnemies()
    {
        _aliveEnemyCount = 0;
        if (_enemiesContainer == null) return;

        Enemy firstEnemy = null;

        for (int i = 0; i < _enemiesContainer.childCount; i++)
        {
            Enemy e = _enemiesContainer.GetChild(i).GetComponent<Enemy>();
            if (e != null)
            {
                e.OnDeath -= OnEnemyDeath;
                e.OnDeath += OnEnemyDeath;
                _aliveEnemyCount++;

                // 玩家此时已激活，刷新敌人对玩家的引用
                e.GetComponent<EnemyAI>()?.RefreshPlayerReference();

                if (firstEnemy == null) firstEnemy = e;
            }
        }

        // 屏幕下方的敌人状态条绑定第一个敌人
        if (_bossHealthBar != null)
            _bossHealthBar.Bind(firstEnemy);
    }

    private void OnPlayerDeath()
    {
        if (_gameEnded) return;
        _gameEnded = true; // 先标记，防止重复触发

        // 延迟弹结算，让死亡动画先播放
        StartCoroutine(DelayedEndGame(false));
    }

    private void OnEnemyDeath()
    {
        if (_gameEnded) return;
        _aliveEnemyCount--;
        if (_aliveEnemyCount <= 0)
            EndGame(true);
    }

    private System.Collections.IEnumerator DelayedEndGame(bool victory)
    {
        yield return new WaitForSeconds(_deathDelay);
        EndGame(victory);
    }

    /// <summary>游戏结束：胜利或失败</summary>
    private void EndGame(bool victory)
    {
        _gameEnded = true;

        // 停止玩家
        if (_player != null)
            _player.gameObject.SetActive(false);

        // 解锁鼠标
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_endPanel != null)
        {
            _endPanel.SetActive(true);
            if (_endText != null)
                _endText.text = victory ? "你赢了！" : "你输了！";
        }
    }

    /// <summary>重新开始（结算按钮 OnClick 绑定）</summary>
    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>退出游戏（可选）</summary>
    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetEnemiesActive(bool active)
    {
        if (_enemiesContainer == null) return;
        for (int i = 0; i < _enemiesContainer.childCount; i++)
            _enemiesContainer.GetChild(i).gameObject.SetActive(active);
    }
}
