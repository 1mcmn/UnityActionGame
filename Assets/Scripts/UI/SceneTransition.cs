using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 菜单进入战斗的跨场景转场幕布：斜向纸白色带扫入盖住画面，显示品牌与真实载入进度；
/// 战斗场景就绪后斜向扫开，同时请求镜头推近。视觉语言与菜单内页面转场一致。
/// </summary>
public sealed class SceneTransition : MonoBehaviour
{
    public static SceneTransition Instance { get; private set; }
    /// <summary>幕布是否仍遮挡画面；战斗 HUD 据此推迟入场动画。</summary>
    public static bool Covering => Instance != null && Instance._covering;
    public bool Covered { get; private set; }

    private NativeMenuAssets _assets;
    private RectTransform _canvasRect, _band, _content;
    private CanvasGroup _contentGroup;
    private RectTransform _progressFill;
    private TMP_Text _caption;
    private bool _covering, _revealing;
    private float _progress, _shownProgress;

    public static SceneTransition Begin(NativeMenuAssets assets, string caption = null)
    {
        if (Instance != null) return Instance;
        var host = new GameObject("SceneTransition");
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<SceneTransition>();
        Instance._assets = assets;
        Instance.Build();
        if (!string.IsNullOrEmpty(caption)) Instance._caption.text = caption;
        Instance.StartCoroutine(Instance.CoverRoutine());
        return Instance;
    }

    public void SetProgress(float value) => _progress = Mathf.Clamp01(value);

    /// <summary>幕布盖住后异步载入场景；载入完成先执行 onLoaded（如开始战斗），再揭幕。用于重新开始与返回主菜单。</summary>
    public static void Go(NativeMenuAssets assets, string scenePath, string caption, System.Action<Scene> onLoaded = null)
    {
        var transition = Begin(assets, caption);
        transition.StartCoroutine(transition.LoadRoutine(scenePath, onLoaded));
    }

    private IEnumerator LoadRoutine(string path, System.Action<Scene> onLoaded)
    {
        while (!Covered) yield return null;
        GamePause.Clear();
        void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != path) return;
            SceneManager.sceneLoaded -= Loaded;
            try { onLoaded?.Invoke(scene); } catch (System.Exception error) { Debug.LogException(error); }
            Reveal();
        }
        SceneManager.sceneLoaded += Loaded;
        AsyncOperation operation = null;
        try
        {
#if UNITY_EDITOR
            if (SceneUtility.GetBuildIndexByScenePath(path) < 0)
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            else
#endif
                operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        }
        catch (System.Exception error) { Debug.LogError("[转场] 场景载入失败：" + error.Message); }
        if (operation == null) { SceneManager.sceneLoaded -= Loaded; Abort(); yield break; }
        while (!operation.isDone) { SetProgress(operation.progress / .9f); yield return null; }
    }

    /// <summary>战斗场景就绪后调用：等两帧让镜头与角色完成初始化，再扫开幕布。</summary>
    public void Reveal()
    {
        if (_revealing) return;
        _revealing = true; StartCoroutine(RevealRoutine());
    }

    private void Build()
    {
        var canvasObject = new GameObject("TransitionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        _canvasRect = (RectTransform)canvasObject.transform;

        // 斜向色带：足够大以在旋转后覆盖全屏，前缘带一道荧光细线，与菜单前景斜线呼应。
        _band = New("Band", _canvasRect); Center(_band, new Vector2(4200, 3200));
        _band.localRotation = Quaternion.Euler(0, 0, -14);
        Solid(_band, _assets.paper);
        var edge = New("Edge", _band); edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1); edge.pivot = new Vector2(0, .5f);
        edge.sizeDelta = new Vector2(10, 0); edge.anchoredPosition = Vector2.zero; Solid(edge, _assets.accent);
        var shade = New("Shade", _band); shade.anchorMin = new Vector2(1, 0); shade.anchorMax = new Vector2(1, 1); shade.pivot = new Vector2(0, .5f);
        shade.sizeDelta = new Vector2(3, 0); shade.anchoredPosition = new Vector2(14, 0); Solid(shade, _assets.ink);

        _content = New("Content", _canvasRect); Center(_content, new Vector2(700, 260));
        _contentGroup = _content.gameObject.AddComponent<CanvasGroup>(); _contentGroup.alpha = 0;
        var title = Text("Title", _content, "刃间", _assets.chineseFont, 92, _assets.ink, new Vector2(0, 70), new Vector2(700, 120)); title.fontStyle = FontStyles.Bold;
        Text("English", _content, "INTERBLADE", _assets.displayFont, 26, _assets.ink, new Vector2(0, -6), new Vector2(700, 36));
        var track = New("Track", _content); Center(track, new Vector2(360, 3)); track.anchoredPosition = new Vector2(0, -58);
        Solid(track, new Color32(187, 197, 184, 255));
        _progressFill = New("Fill", track); _progressFill.anchorMin = Vector2.zero; _progressFill.anchorMax = new Vector2(0, 1);
        _progressFill.pivot = new Vector2(0, .5f); _progressFill.offsetMin = _progressFill.offsetMax = Vector2.zero; Solid(_progressFill, _assets.ink);
        _caption = Text("Caption", _content, "LOADING · 进入战场", _assets.condensedFont, 15, _assets.muted, new Vector2(0, -86), new Vector2(700, 24));

        _band.anchoredPosition = new Vector2(-Sweep(), 0);
    }

    // 色带从左侧外扫到正中所需距离（随屏幕宽度变化）。
    private float Sweep() => _canvasRect.rect.width * .5f + 2600f;

    private IEnumerator CoverRoutine()
    {
        _covering = true;
        UiSfx.Play(UiSfx.Cue.Transition);
        float from = -Sweep(), duration = _assets.reduceMotion ? .01f : .42f;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration; p = p * p * p;
            _band.anchoredPosition = new Vector2(Mathf.Lerp(from, 0, p), 0); yield return null;
        }
        _band.anchoredPosition = Vector2.zero; Covered = true;
        for (float t = 0; t < .2f; t += Time.unscaledDeltaTime) { _contentGroup.alpha = t / .2f; yield return null; }
        _contentGroup.alpha = 1;
    }

    private void Update()
    {
        _shownProgress = Mathf.MoveTowards(_shownProgress, _progress, Time.unscaledDeltaTime * 2.5f);
        if (_progressFill != null) _progressFill.anchorMax = new Vector2(_shownProgress, 1);
    }

    private IEnumerator RevealRoutine()
    {
        while (!Covered) yield return null;
        _progress = 1; _caption.text = "READY · 战场就绪";
        yield return null; yield return null;
        while (_shownProgress < .999f) yield return null;
        for (float t = 0; t < .15f; t += Time.unscaledDeltaTime) { _contentGroup.alpha = 1 - t / .15f; yield return null; }
        _contentGroup.alpha = 0;
        var follow = CameraCache.Main != null ? CameraCache.Main.GetComponent<CameraFollow>() : null;
        if (follow != null) follow.PlayIntro();
        UiSfx.Play(UiSfx.Cue.Transition);
        float to = Sweep(), duration = _assets.reduceMotion ? .01f : .55f;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration; p = 1 - Mathf.Pow(1 - p, 4);
            _band.anchoredPosition = new Vector2(Mathf.Lerp(0, to, p), 0);
            if (p > .35f) _covering = false;
            yield return null;
        }
        _covering = false; Instance = null; Destroy(gameObject);
    }

    /// <summary>载入失败时撤回幕布，回到菜单。</summary>
    public void Abort()
    {
        StopAllCoroutines(); _covering = false; Instance = null; Destroy(gameObject);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private static RectTransform New(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.layer = 5; obj.transform.SetParent(parent, false); return (RectTransform)obj.transform;
    }
    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero;
    }
    private static void Solid(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
    }
    private static TMP_Text Text(string name, RectTransform parent, string value, TMP_FontAsset font, float size, Color color, Vector2 position, Vector2 box)
    {
        var rect = New(name, parent); Center(rect, box); rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); if (font != null) text.font = font;
        text.text = value; text.fontSize = size; text.color = color; text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false; text.raycastTarget = false; return text;
    }
}
