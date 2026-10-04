using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>开始菜单及轻量设置；不依赖战斗场景中的单例。</summary>
public sealed class MainMenuController : MonoBehaviour
{
    private const string VolumeKey = "Menu.MasterVolume";
    private const string FullscreenKey = "Menu.Fullscreen";
    private const string VSyncKey = "Menu.VSync";
    [SerializeField] private string gameplayScenePath = "Assets/Game/Scenes/DEMO City Crossing.unity";
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Toggle vsyncToggle;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button closeSettingsButton;
    [SerializeField] private CanvasGroup navigationGroup;
    private bool loading;
    private static string pendingGameplayPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLaunchRequest()
    {
        SceneManager.sceneLoaded -= StartLoadedGameplay;
        pendingGameplayPath = null;
    }

    private static void StartLoadedGameplay(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != pendingGameplayPath) return;
        ResetLaunchRequest();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameManager manager = root.GetComponentInChildren<GameManager>(true);
            if (manager != null) { manager.StartGame(); break; }
        }
    }

    public void Configure(GameObject panel, Slider volume, Toggle fullscreen, Toggle vsync,
        TMP_Text status, Button start, Button settings, Button close, CanvasGroup navigation)
    {
        settingsPanel = panel; volumeSlider = volume; fullscreenToggle = fullscreen;
        vsyncToggle = vsync; statusText = status; startButton = start;
        settingsButton = settings; closeSettingsButton = close;
        navigationGroup = navigation;
    }

    private void Awake()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        bool vsync = PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
        fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
        vsyncToggle.SetIsOnWithoutNotify(vsync);
        volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        QualitySettings.vSyncCount = vsync ? 1 : 0;
#if !UNITY_EDITOR
        Screen.fullScreen = fullscreen;
#endif
        settingsPanel.SetActive(false);
    }

    private void Start() { Select(startButton); }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && settingsPanel.activeSelf) CloseSettings();
    }

    private static void Select(Button button)
    {
        if (EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    public void StartGame()
    {
        if (loading) return;
        StartCoroutine(LoadGameplay());
    }

    private IEnumerator LoadGameplay()
    {
        loading = true;
        startButton.interactable = false;
        settingsButton.interactable = false;
        statusText.text = "正在进入战斗…";
        PlayerPrefs.Save();
        yield return null;
        AsyncOperation operation = null;
        pendingGameplayPath = gameplayScenePath;
        SceneManager.sceneLoaded -= StartLoadedGameplay;
        SceneManager.sceneLoaded += StartLoadedGameplay;
        try
        {
#if UNITY_EDITOR
            if (SceneUtility.GetBuildIndexByScenePath(gameplayScenePath) < 0)
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    gameplayScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            else
#endif
                operation = SceneManager.LoadSceneAsync(gameplayScenePath, LoadSceneMode.Single);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[开始菜单] 加载失败：" + exception.Message);
        }
        if (operation == null)
        {
            ResetLaunchRequest();
            statusText.text = "场景加载失败，请检查构建场景列表。";
            loading = false;
            startButton.interactable = true;
            settingsButton.interactable = true;
            yield break;
        }
        while (!operation.isDone) yield return null;
    }

    public void OpenSettings()
    {
        if (loading) return;
        settingsPanel.SetActive(true);
        navigationGroup.interactable = false;
        navigationGroup.blocksRaycasts = false;
        Select(closeSettingsButton);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        navigationGroup.interactable = true;
        navigationGroup.blocksRaycasts = true;
        PlayerPrefs.Save();
        Select(settingsButton);
    }

    public void SetVolume(float value)
    {
        AudioListener.volume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
    }

    public void SetFullscreen(bool value)
    {
        PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
#if !UNITY_EDITOR
        Screen.fullScreen = value;
#endif
    }

    public void SetVSync(bool value)
    {
        QualitySettings.vSyncCount = value ? 1 : 0;
        PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
    }

    public void QuitGame()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnApplicationQuit() { PlayerPrefs.Save(); }
}
