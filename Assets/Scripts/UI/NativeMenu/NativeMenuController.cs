using System;
using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>原生菜单状态、焦点、名称记录与实际战斗入口。</summary>
public sealed class NativeMenuController : MonoBehaviour
{
    public NativeMenuUI ui;
    public NativeMenuAssets assets;
    public NativeDiscCarousel carousel;
    public NativeMenuAudio menuAudio;
    [SerializeField] private string gameplayScenePath = "Assets/Game/Scenes/DEMO City Crossing.unity";
    public string ScreenName { get; private set; } = "main";
    public NativeMenuSaves Saves { get; private set; }
    public bool Creating => ui.modalPanel.gameObject.activeSelf;
    public bool Loading { get; private set; }
    private Tween transition, detailsMotion;
    private int lastMain, shownDetails = -1;
    private Vector2 pointer, lastSize;
    private readonly Vector2[] artOrigins = new Vector2[6], detailOrigins = new Vector2[5];
    private bool layoutReady;
    private float nextFloorFrame;
    private static string pendingScene;
    private Material heroInstance;

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (ui == null || assets == null || carousel == null || menuAudio == null) { Debug.LogError("原生菜单接线缺失。", this); enabled = false; return; }
        ui.mainButtons[0].onClick.AddListener(() => Open("saves")); ui.mainButtons[1].onClick.AddListener(() => Open("settings")); ui.mainButtons[2].onClick.AddListener(() => Open("quit"));
        ui.brand.onClick.AddListener(Back); ui.backSaves.onClick.AddListener(Back); ui.backSettings.onClick.AddListener(Back); ui.backQuit.onClick.AddListener(Back);
        ui.newSave.onClick.AddListener(OpenCreate); ui.continueButton.onClick.AddListener(EnterBattle);
        ui.previous.onClick.AddListener(() => carousel.Step(-1)); ui.next.onClick.AddListener(() => carousel.Step(1));
        ui.cancel.onClick.AddListener(CancelCreate); ui.create.onClick.AddListener(CreateSave); ui.quitConfirm.onClick.AddListener(Quit);
        ui.nameInput.onSubmit.AddListener(_ => CreateSave());
        for (int i = 0; i < ui.volumes.Length; i++)
        {
            int channel = i; ui.volumes[i].SetValueWithoutNotify(menuAudio.Get(i)); ui.volumeValues[i].text = Mathf.RoundToInt(menuAudio.Get(i) * 100).ToString();
            ui.volumes[i].onValueChanged.AddListener(value => { menuAudio.Set(channel, value); ui.volumeValues[channel].text = Mathf.RoundToInt(value * 100).ToString(); ui.wave.amplitude = menuAudio.Get(0); ui.wave.SetVerticesDirty(); UiSfx.Play(UiSfx.Cue.Slider, .8f + value * .6f); Punch(ui.volumeValues[channel].transform); });
        }
        ui.surface.carousel = carousel; carousel.SelectionChanged += ChangedSelection; carousel.Activated += EnterBattle;
        heroInstance = new Material(assets.heroMaterial); ui.blade.GetComponent<UnityEngine.UI.RawImage>().material = heroInstance;
        Saves = new NativeMenuSaves(); ui.saveStatus.text = Saves.Status;
        BuildIndicator(); BuildDiscPath();
        var scrollHint = ui.continueButton.transform.Find("ScrollHint"); if (scrollHint != null) scrollHint.GetComponent<TMPro.TMP_Text>().text = "滚轮 / 两侧光盘切换 · 点击中间光盘进入   ← → / ENTER";
        Resize(); carousel.Rebuild(Saves.Records, 0); Show("main");
        ui.wave.amplitude = menuAudio.Get(0); ui.wave.SetVerticesDirty();
    }
    private void Update()
    {
        Vector2 size = new Vector2(ui.menuCamera.pixelWidth, ui.menuCamera.pixelHeight);
        if (size != lastSize) Resize();
        if (Input.GetKeyDown(KeyCode.Escape) && !Loading) { if (Creating) CancelCreate(); else if (ScreenName != "main") Back(); }
        if (Input.GetKeyDown(KeyCode.Tab)) CycleFocus(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
        UpdateIndicator();
        if (!layoutReady || assets.reduceMotion) return;
        Vector2 target = new Vector2(Input.mousePosition.x / Mathf.Max(1, size.x) * 2 - 1, Input.mousePosition.y / Mathf.Max(1, size.y) * 2 - 1);
        if (target.x < -1 || target.x > 1 || target.y < -1 || target.y > 1 || !Application.isFocused) target = Vector2.zero;
        pointer = Vector2.Lerp(pointer, target, 1 - Mathf.Exp(-5 * Time.unscaledDeltaTime));
        RectTransform[] layers = { ui.blade, ui.ghost, ui.stripe, ui.bracket, ui.caption, ui.halftone };
        float[] depth = { 19, -10, -20, -26, -15, -5 };
        for (int i = 0; i < layers.Length; i++) layers[i].anchoredPosition = artOrigins[i] + new Vector2(pointer.x, pointer.y * .6f) * depth[i] * ui.Unit;
        ui.blade.localRotation = Quaternion.Euler(-pointer.y * 7, -pointer.x * 11, 7);
        heroInstance.SetFloat("_StyleAngle", pointer.x);
        var dots = ui.halftone.GetComponent<NativeMenuGraphic>(); Color color = dots.color; color.a = .19f + pointer.x * .06f; dots.color = color;
        if (ui.floor != null && Time.unscaledTime >= nextFloorFrame) { nextFloorFrame = Time.unscaledTime + .033f; ui.floor.pointer = new Vector2(pointer.x, -pointer.y); ui.floor.SetVerticesDirty(); }
    }
    private void Start()
    {
        if (EventSystem.current != null && ScreenName == "main") EventSystem.current.SetSelectedGameObject(ui.mainButtons[lastMain].gameObject);
    }
    public void Resize()
    {
        if (ui == null || ui.menuCamera == null) return;
        Canvas.ForceUpdateCanvases(); lastSize = new Vector2(ui.menuCamera.pixelWidth, ui.menuCamera.pixelHeight);
        if (lastSize.x <= 0 || lastSize.y <= 0) return;
        ui.Layout(lastSize.x, lastSize.y);
        ui.menuCamera.transform.position = new Vector3(0, 0, -1100 * ui.Unit); ui.menuCamera.transform.rotation = Quaternion.identity;
        ui.menuCamera.fieldOfView = 2 * Mathf.Atan(lastSize.y / 2200) * Mathf.Rad2Deg;
        ui.menuCamera.nearClipPlane = .1f; ui.menuCamera.farClipPlane = 10000;
        Canvas.ForceUpdateCanvases();
        RectTransform[] layers = { ui.blade, ui.ghost, ui.stripe, ui.bracket, ui.caption, ui.halftone };
        for (int i = 0; i < layers.Length; i++) artOrigins[i] = layers[i].anchoredPosition;
        for (int i = 0; i < ui.details.Length; i++) detailOrigins[i] = ((RectTransform)ui.details[i].transform).anchoredPosition;
        carousel.ClearInk(); detailsMotion.Stop(); RestoreDetails(); layoutReady = true; carousel.Draw();
    }
    public void Open(string screen)
    {
        if (Loading || Creating || transition.isAlive) return;
        lastMain = screen == "settings" ? 1 : screen == "quit" ? 2 : 0; Navigate(screen);
    }
    public void Back() { if (!Loading && !Creating && !transition.isAlive) Navigate("main"); }
    private void Navigate(string screen)
    {
        bool back = screen == "main";
        UiSfx.Play(back ? UiSfx.Cue.Back : UiSfx.Cue.Confirm); carousel.InputEnabled = false;
        if (assets.reduceMotion) { Show(screen); return; }
        UiSfx.Play(back ? UiSfx.Cue.TransitionBack : UiSfx.Cue.Transition);
        ui.flashGroup.alpha = 1; ui.flash.localRotation = Quaternion.Euler(0, 0, -14);
        float width = ui.Viewport.x * ui.Unit; ui.flash.anchoredPosition = new Vector2(-width, 0);
        transition = Tween.Custom(this, -width, width * .2f, .2f, (target, x) => target.ui.flash.anchoredPosition = new Vector2(x, 0), Ease.InCubic, useUnscaledTime: true)
            .OnComplete(this, target => {
                target.Show(screen);
                target.transition = Tween.Custom(target, width * .2f, width * 1.8f, .28f, (t, x) => t.ui.flash.anchoredPosition = new Vector2(x, 0), Ease.OutQuart, useUnscaledTime: true)
                    .OnComplete(target, t => t.ui.flashGroup.alpha = 0);
            });
    }
    public void Show(string screen)
    {
        ScreenName = screen; ui.mainPanel.gameObject.SetActive(screen == "main"); ui.savesPanel.gameObject.SetActive(screen == "saves");
        ui.settingsPanel.gameObject.SetActive(screen == "settings"); ui.quitPanel.gameObject.SetActive(screen == "quit"); ui.modalPanel.gameObject.SetActive(false);
        ui.artPanel.gameObject.SetActive(screen != "saves");
        var artGroup = ui.artPanel.GetComponent<CanvasGroup>(); if (artGroup != null) artGroup.alpha = screen == "main" ? 1 : .12f;
        carousel.gameObject.SetActive(screen == "saves"); carousel.InputEnabled = screen == "saves" && !Loading;
        if (carousel.path != null) carousel.path.gameObject.SetActive(screen == "saves");
        detailsMotion.Stop(); ApplyDetails(carousel.Selection); RestoreDetails();
        UnityEngine.UI.Selectable focus = screen == "main" ? ui.mainButtons[lastMain] : screen == "saves" ? ui.surface : screen == "settings" ? (UnityEngine.UI.Selectable)ui.backSettings : ui.backQuit;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(focus.gameObject);
        carousel.Draw();
    }
    private void ChangedSelection(int selection, int direction)
    {
        ui.counter.text = (selection + 1).ToString("00") + " / " + carousel.Count.ToString("00");
        ui.previous.interactable = ui.next.interactable = carousel.Count > 1;
        ui.confirmLabel.text = selection >= Saves.Records.Count ? "新建存档" : "进入战场";
        detailsMotion.Stop();
        if (assets.reduceMotion || shownDetails == selection || !ui.savesPanel.gameObject.activeSelf)
        { ApplyDetails(selection); RestoreDetails(); return; }
        float exitEnd = assets.textExit + .018f * 4, enterEnd = exitEnd + assets.textEnter + .045f * 4;
        bool switched = false;
        detailsMotion = Tween.Custom(this, 0f, enterEnd, enterEnd, (target, time) => {
            if (time >= exitEnd && !switched) { target.ApplyDetails(selection); switched = true; }
            for (int i = 0; i < target.ui.details.Length; i++)
            {
                var group = target.ui.details[i]; float alpha, y;
                if (!switched) { float p = Mathf.Clamp01((time - i * .018f) / target.assets.textExit); alpha = 1 - p * p * p; y = -direction * 12 * p; }
                else { float p = Mathf.Clamp01((time - exitEnd - i * .045f) / target.assets.textEnter); float e = 1 - Mathf.Pow(1 - p, 4); alpha = e; y = direction * 16 * (1 - e); }
                group.alpha = alpha; ((RectTransform)group.transform).anchoredPosition = target.detailOrigins[i] + new Vector2(0, y * target.ui.Unit);
            }
        }, Ease.Linear, useUnscaledTime: true).OnComplete(this, target => target.RestoreDetails());
    }
    private void ApplyDetails(int selection)
    {
        shownDetails = selection;
        if (Saves == null || selection >= Saves.Records.Count)
        {
            ui.detailKicker.text = "NEW STORY"; ui.detailTitle.text = "新的故事"; ui.detailCaption.text = "创建一张光盘，从初始战场开始。"; ui.detailDate.text = "NEW GAME"; ui.detailNote.text = "每个名字，一次出发。"; return;
        }
        NativeMenuSave record = Saves.Records[selection]; ui.detailKicker.text = "CHAPTER 01"; ui.detailTitle.text = record.name;
        ui.detailCaption.text = "初始战场 · 从入口进入";
        ui.detailDate.text = DateTime.TryParse(record.createdAt, out DateTime date) ? date.ToLocalTime().ToString("yyyy.MM.dd") : "";
        ui.detailNote.text = "创建日期 / 本机记录";
    }
    private void RestoreDetails()
    {
        for (int i = 0; i < ui.details.Length; i++) { ui.details[i].alpha = 1; ((RectTransform)ui.details[i].transform).anchoredPosition = detailOrigins[i]; }
    }
    public void OpenCreate()
    {
        if (Loading || Creating || ScreenName != "saves") return;
        carousel.InputEnabled = false; carousel.StopMotion(); ui.modalPanel.gameObject.SetActive(true); ui.createError.text = "";
        ui.nameInput.SetTextWithoutNotify(""); ui.nameInput.ActivateInputField(); EventSystem.current.SetSelectedGameObject(ui.nameInput.gameObject); UiSfx.Play(UiSfx.Cue.Open);
    }
    public void CancelCreate()
    {
        UiSfx.Play(UiSfx.Cue.Close); ui.modalPanel.gameObject.SetActive(false); carousel.InputEnabled = ScreenName == "saves" && !Loading;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(ui.newSave.gameObject);
    }
    public void CreateSave()
    {
        if (!Creating || Loading) return;
        try
        {
            Saves.Create(ui.nameInput.text); ui.saveStatus.text = Saves.Status; ui.modalPanel.gameObject.SetActive(false); carousel.InputEnabled = true;
            carousel.Rebuild(Saves.Records, Saves.Records.Count - 1); carousel.ConfirmSelection(); EventSystem.current.SetSelectedGameObject(ui.surface.gameObject);
        }
        catch (ArgumentException error) { UiSfx.Play(UiSfx.Cue.Error); Punch(ui.createError.transform); ui.createError.text = error.Message; ui.nameInput.ActivateInputField(); }
    }
    public void EnterBattle()
    {
        if (Loading || Creating || ScreenName != "saves") return;
        if (carousel.Selection >= Saves.Records.Count) { OpenCreate(); return; }
        StartCoroutine(LoadBattle());
    }
    // 进入战斗：光盘推进 → 幕布扫入（显示真实载入进度）→ 异步载入 → 战斗场景就绪后揭幕（见 BeginLoadedBattle）。
    private IEnumerator LoadBattle()
    {
        Loading = true; ui.continueButton.interactable = ui.newSave.interactable = false; UiSfx.Play(UiSfx.Cue.Start);
        carousel.ConfirmSelection(); carousel.InputEnabled = false; ui.saveStatus.text = "正在加载战场…";
        while (carousel.Moving) yield return null;
        var panel = ui.savesPanel.GetComponent<CanvasGroup>(); if (panel == null) panel = ui.savesPanel.gameObject.AddComponent<CanvasGroup>();
        if (!assets.reduceMotion)
        {
            carousel.Launch(.7f);
            for (float t = 0; t < .45f; t += Time.unscaledDeltaTime) { panel.alpha = 1 - Mathf.Clamp01(t / .3f); yield return null; }
        }
        var cover = SceneTransition.Begin(assets);
        while (!cover.Covered) yield return null;
        PlayerPrefs.Save(); AsyncOperation operation = null;
        try
        {
            pendingScene = gameplayScenePath; SceneManager.sceneLoaded -= BeginLoadedBattle; SceneManager.sceneLoaded += BeginLoadedBattle;
#if UNITY_EDITOR
            if (SceneUtility.GetBuildIndexByScenePath(gameplayScenePath) < 0)
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(gameplayScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            else
#endif
                operation = SceneManager.LoadSceneAsync(gameplayScenePath, LoadSceneMode.Single);
        }
        catch (Exception error) { Debug.LogError("[原生菜单] 战场加载失败：" + error.Message, this); }
        if (operation != null)
        {
            // 菜单场景卸载后协程随之结束，最终进度由揭幕时补满。
            while (!operation.isDone) { cover.SetProgress(operation.progress / .9f); yield return null; }
            yield break;
        }
        SceneManager.sceneLoaded -= BeginLoadedBattle; pendingScene = null; Loading = false;
        cover.Abort(); carousel.ResetLaunch(); panel.alpha = 1;
        ui.saveStatus.text = "加载失败，请检查构建场景。"; ui.continueButton.interactable = ui.newSave.interactable = true; carousel.InputEnabled = true;
    }
    private static void BeginLoadedBattle(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != pendingScene) return;
        SceneManager.sceneLoaded -= BeginLoadedBattle; pendingScene = null;
        foreach (var root in scene.GetRootGameObjects())
        { var manager = root.GetComponentInChildren<GameManager>(true); if (manager != null) { manager.StartGame(); if (SceneTransition.Instance != null) SceneTransition.Instance.Reveal(); return; } }
        Debug.LogError("[原生菜单] 战斗场景缺少 GameManager。");
        if (SceneTransition.Instance != null) SceneTransition.Instance.Reveal();
    }
    private void CycleFocus(int direction)
    {
        if (Loading || EventSystem.current == null) return;
        UnityEngine.UI.Selectable[] items = Creating ? new UnityEngine.UI.Selectable[] { ui.nameInput, ui.cancel, ui.create } : ScreenName == "main" ? ui.mainButtons :
            ScreenName == "saves" ? new UnityEngine.UI.Selectable[] { ui.backSaves, ui.newSave, ui.surface, ui.previous, ui.next, ui.continueButton } :
            ScreenName == "settings" ? new UnityEngine.UI.Selectable[] { ui.backSettings, ui.volumes[0], ui.volumes[1], ui.volumes[2] } : new UnityEngine.UI.Selectable[] { ui.backQuit, ui.quitConfirm };
        int current = Array.FindIndex(items, item => item.gameObject == EventSystem.current.currentSelectedGameObject);
        for (int n = 1; n <= items.Length; n++) { int index = (current + direction * n + items.Length * 2) % items.Length; if (items[index].IsInteractable()) { EventSystem.current.SetSelectedGameObject(items[index].gameObject); break; } }
    }
    public void Quit()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    private void OnDisable() { transition.Stop(); detailsMotion.Stop(); if (carousel != null) carousel.StopMotion(); }

    /// <summary>数值或提示更新时的轻微放大回弹，与音效同拍。</summary>
    private void Punch(Transform target)
    {
        if (assets.reduceMotion || target == null) return;
        Tween.Custom(target, 1.18f, 1f, .2f, (t, s) => t.localScale = new Vector3(s, s, 1), Ease.OutCubic, useUnscaledTime: true);
    }

    // 主菜单选中指示点：参考 METAPHOR 选中项右侧的小圆点，随悬停/键盘焦点滑动到当前项，换项时放大一下。
    private RectTransform indicator;
    private UnityEngine.UI.Selectable indicatorTarget;
    private float indicatorPunch;
    private void BuildIndicator()
    {
        var dot = new GameObject("SelectionIndicator", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        indicator = (RectTransform)dot.transform; indicator.SetParent(ui.mainPanel, false); dot.layer = ui.mainPanel.gameObject.layer;
        var image = dot.GetComponent<UnityEngine.UI.Image>(); image.color = assets.ink; image.raycastTarget = false;
        indicator.anchorMin = indicator.anchorMax = indicator.pivot = new Vector2(.5f, .5f);
        indicator.sizeDelta = new Vector2(9, 9) * ui.Unit; indicator.localRotation = Quaternion.Euler(0, 0, 45);
    }
    private void UpdateIndicator()
    {
        if (indicator == null || ScreenName != "main") return;
        var active = NativeMenuButton.Active != null ? NativeMenuButton.Active.GetComponent<UnityEngine.UI.Selectable>() : null;
        if (Array.IndexOf(ui.mainButtons, active) < 0) active = ui.mainButtons[lastMain];
        if (active != indicatorTarget) { indicatorTarget = active; indicatorPunch = 1; }
        var rect = (RectTransform)active.transform; Rect r = rect.rect;
        Vector3 goal = rect.TransformPoint(new Vector3(r.xMax + 18 * ui.Unit, r.center.y, 0));
        float k = 1 - Mathf.Exp(-(assets.reduceMotion ? 60 : 16) * Time.unscaledDeltaTime);
        indicator.position = Vector3.Lerp(indicator.position, goal, k);
        indicatorPunch = Mathf.Max(0, indicatorPunch - Time.unscaledDeltaTime * 4);
        float s = 1 + .6f * indicatorPunch * indicatorPunch; indicator.localScale = new Vector3(s, s, 1);
        indicator.sizeDelta = new Vector2(9, 9) * ui.Unit;
    }

    // 存档页光盘之间的连线与选中光盘的旋转虚线环，画在背景画布上，从光盘背后穿过。
    private void BuildDiscPath()
    {
        if (ui.floor == null) return;
        var host = new GameObject("DiscPath", typeof(RectTransform), typeof(CanvasRenderer), typeof(NativeDiscPath));
        var rect = (RectTransform)host.transform; rect.SetParent(ui.floor.transform.parent, false); host.layer = ui.floor.gameObject.layer;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var path = host.GetComponent<NativeDiscPath>(); path.raycastTarget = false; path.color = assets.ink;
        path.eventCamera = ui.menuCamera; carousel.path = path; host.SetActive(false);
    }
    private void OnDestroy() { if (carousel != null) { carousel.SelectionChanged -= ChangedSelection; carousel.Activated -= EnterBattle; } if (heroInstance != null) Destroy(heroInstance); PlayerPrefs.Save(); }
}
