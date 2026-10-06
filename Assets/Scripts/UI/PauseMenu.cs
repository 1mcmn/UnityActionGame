using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 战斗内 ESC 暂停菜单：继续 / 设置 / 操作说明 / 重新开始 / 返回主菜单 / 退出游戏。
/// 视觉复刻主菜单（纸白侧板、编号按钮、荧光选中、菱形指示），重大操作二次确认，场景切换走转场幕布。
/// </summary>
public sealed class PauseMenu : MonoBehaviour
{
    public const string MenuScenePath = "Assets/Game/Scenes/menu.scene";
    /// <summary>场景中存在暂停菜单时，镜头不再处理 ESC 解锁。</summary>
    public static bool Present { get; private set; }

    private NativeMenuAssets _assets;
    private CanvasGroup _group;
    private RectTransform _panel, _indicator, _dialog;
    private RectTransform _mainPage, _settingsPage, _controlsPage;
    private Selectable[] _mainItems, _settingsItems, _controlsItems, _dialogItems;
    private TMP_Text _dialogTitle, _dialogCaption, _pageTitle, _pageSubtitle;
    private Action _dialogAction, _refreshToggles;
    private float _open, _openTarget;
    private bool _leaving;

    private void OnEnable() => Present = true;
    private void OnDisable() { Present = false; if (GamePause.IsPaused) GamePause.Resume(false); }

    public void Init(NativeMenuAssets assets)
    {
        _assets = assets;
        EnsureEventSystem();
        var canvas = MenuKit.MakeCanvas("PauseMenu Canvas", transform, 60);
        _group = canvas.GetComponent<CanvasGroup>(); _group.alpha = 0; _group.blocksRaycasts = _group.interactable = false;
        Transform root = canvas.transform;

        var dim = MenuKit.Solid("Dim", root, _assets.ink.WithAlpha(.55f), true); MenuKit.Stretch(dim.rectTransform);
        var ghost = MenuKit.Text(_assets, "GhostWord", root, "PAUSE", MenuKit.Face.Display, 300, new Color(1, 1, 1, .5f), TextAlignmentOptions.BottomRight);
        if (_assets.outlineMaterial != null) ghost.fontSharedMaterial = _assets.outlineMaterial;
        MenuKit.Place(ghost.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-60, 40), new Vector2(1200, 320));

        _panel = MenuKit.New("Panel", root);
        _panel.anchorMin = new Vector2(0, 0); _panel.anchorMax = new Vector2(0, 1); _panel.pivot = new Vector2(0, .5f); _panel.sizeDelta = new Vector2(580, 0);
        MenuKit.Stretch(MenuKit.Solid("Paper", _panel, _assets.paper.WithAlpha(.98f), true).rectTransform);
        var edge = MenuKit.Solid("Edge", _panel, _assets.accent); MenuKit.Anchor(edge.rectTransform, 1, 0, 1, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0); edge.rectTransform.anchoredPosition = new Vector2(3, 0);
        var halftone = MenuKit.New("Halftone", _panel); var dots = halftone.gameObject.AddComponent<NativeMenuGraphic>();
        dots.pattern = NativeMenuGraphic.Pattern.Halftone; dots.color = new Color(.28f, .36f, .26f, .12f); dots.raycastTarget = false;
        MenuKit.Place(halftone, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 60), new Vector2(300, 300));

        var kicker = MenuKit.Text(_assets, "Kicker", _panel, "刃间  INTERBLADE", MenuKit.Face.Condensed, 13, _assets.muted); MenuKit.TopLeft(kicker.rectTransform, 64, 52, 400, 20);
        _pageTitle = MenuKit.Text(_assets, "Title", _panel, "PAUSED.", MenuKit.Face.Display, 92, _assets.ink); MenuKit.TopLeft(_pageTitle.rectTransform, 60, 76, 480, 110);
        _pageSubtitle = MenuKit.Text(_assets, "Subtitle", _panel, "暂停", MenuKit.Face.Chinese, 12, _assets.muted); MenuKit.TopLeft(_pageSubtitle.rectTransform, 64, 190, 400, 22);
        var hints = MenuKit.Text(_assets, "Hints", _panel, "↑ ↓ 选择     ENTER 确认     ESC 返回", MenuKit.Face.Chinese, 10, _assets.muted);
        MenuKit.Place(hints.rectTransform, Vector2.zero, Vector2.zero, new Vector2(64, 36), new Vector2(460, 20));

        BuildMain(); BuildSettings(); BuildControls(); BuildDialog(root);
        var dot = MenuKit.Solid("Indicator", _panel, _assets.ink); _indicator = dot.rectTransform;
        _indicator.anchorMin = _indicator.anchorMax = _indicator.pivot = new Vector2(.5f, .5f); _indicator.sizeDelta = new Vector2(9, 9); _indicator.localRotation = Quaternion.Euler(0, 0, 45);
        ShowPage(_mainPage, false);
        _panel.anchoredPosition = new Vector2(-640, 0);
    }

    private RectTransform Page(string name)
    {
        var page = MenuKit.New(name, _panel); page.anchorMin = new Vector2(0, 0); page.anchorMax = new Vector2(0, 1); page.pivot = new Vector2(0, 1);
        page.offsetMin = new Vector2(64, 80); page.offsetMax = new Vector2(64 + 452, -250); return page;
    }

    private void BuildMain()
    {
        _mainPage = Page("MainPage");
        string[,] items = { { "Resume", "继续游戏" }, { "Settings", "设置" }, { "Controls", "操作说明" }, { "Restart", "重新开始本场战斗" }, { "Main Menu", "返回主菜单" }, { "Quit Game", "退出游戏" } };
        Action[] actions = { Close, () => OpenSub(_settingsPage, "SETTINGS.", "设置"), () => OpenSub(_controlsPage, "CONTROLS.", "操作说明"),
            () => Ask("重新开始？", "当前战斗进度将被重置。", Restart), () => Ask("返回主菜单？", "当前战斗进度不会保存。", ToMenu), () => Ask("退出游戏？", "确定要关闭游戏吗？", Quit) };
        _mainItems = new Selectable[items.GetLength(0)];
        for (int i = 0; i < _mainItems.Length; i++)
        {
            var button = MenuKit.MenuButton(_assets, _mainPage, "0" + (i + 1), items[i, 0], items[i, 1], i == 0, actions[i]);
            MenuKit.TopLeft((RectTransform)button.transform, 0, i == 0 ? 0 : 96 + (i - 1) * 78, 452, i == 0 ? 84 : 70);
            _mainItems[i] = button;
        }
        MenuKit.ChainNavigation(_mainItems);
    }

    private void BuildSettings()
    {
        _settingsPage = Page("SettingsPage");
        string[] english = { "Master Volume", "Music", "Sound Effects" }, chinese = { "主音量", "音乐音量", "音效音量" };
        var list = new System.Collections.Generic.List<Selectable>();
        for (int i = 0; i < 3; i++)
        {
            int channel = i;
            var slider = MenuKit.SliderRow(_assets, _settingsPage, english[i], chinese[i], 0, 1, GameSettings.Volume(i), v => Mathf.RoundToInt(v * 100).ToString(), v => GameSettings.SetVolume(channel, v));
            MenuKit.TopLeft((RectTransform)slider.transform.parent, 0, i * 74, 452, 62); list.Add(slider);
        }
        var sensitivity = MenuKit.SliderRow(_assets, _settingsPage, "Mouse Sensitivity", "鼠标灵敏度", .2f, 2f, GameSettings.Sensitivity, v => v.ToString("0.0") + "x", GameSettings.SetSensitivity);
        MenuKit.TopLeft((RectTransform)sensitivity.transform.parent, 0, 3 * 74, 452, 62); list.Add(sensitivity);
        var full = MenuKit.ToggleRow(_assets, _settingsPage, "Fullscreen", "全屏", () => GameSettings.Fullscreen, GameSettings.SetFullscreen, out Action refreshFull);
        MenuKit.TopLeft((RectTransform)full.transform, 0, 4 * 74 + 6, 452, 54); list.Add(full);
        var vsync = MenuKit.ToggleRow(_assets, _settingsPage, "Vertical Sync", "垂直同步", () => GameSettings.VSync, GameSettings.SetVSync, out Action refreshSync);
        MenuKit.TopLeft((RectTransform)vsync.transform, 0, 4 * 74 + 66, 452, 54); list.Add(vsync);
        _refreshToggles = () => { refreshFull(); refreshSync(); };
        var back = MenuKit.MenuButton(_assets, _settingsPage, "←", "Back", "返回 · 设置自动保存", false, BackToMain);
        MenuKit.TopLeft((RectTransform)back.transform, 0, 4 * 74 + 140, 452, 62); list.Add(back);
        _settingsItems = list.ToArray(); MenuKit.ChainNavigation(_settingsItems);
    }

    private void BuildControls()
    {
        _controlsPage = Page("ControlsPage");
        string[,] keys = {
            { "WASD", "移动" }, { "SHIFT", "点按闪避 / 按住奔跑" }, { "SPACE", "跳跃" }, { "LMB", "普通连招 / 空中连斩" }, { "RMB", "弹反 / 格挡" },
            { "Q", "破防后挑飞" }, { "E", "空中追击 / 提前砸地" }, { "F5", "重载已保存的连招配置" }, { "F1", "调试面板" }, { "F8", "性能采样" }, { "ESC", "暂停菜单" } };
        for (int i = 0; i < keys.GetLength(0); i++) MenuKit.KeyRow(_assets, _controlsPage, keys[i, 0], keys[i, 1], i * 40, 452);
        var back = MenuKit.MenuButton(_assets, _controlsPage, "←", "Back", "返回", false, BackToMain);
        MenuKit.TopLeft((RectTransform)back.transform, 0, keys.GetLength(0) * 40 + 20, 452, 62);
        _controlsItems = new Selectable[] { back }; MenuKit.ChainNavigation(_controlsItems);
    }

    private void BuildDialog(Transform root)
    {
        _dialog = MenuKit.New("ConfirmDialog", root); MenuKit.Stretch(_dialog);
        MenuKit.Stretch(MenuKit.Solid("Dim", _dialog, _assets.ink.WithAlpha(.45f), true).rectTransform);
        var card = MenuKit.New("Card", _dialog); MenuKit.Place(card, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(560, 280));
        var plate = card.gameObject.AddComponent<HudPlate>(); plate.color = _assets.paper; plate.cut = 18; plate.raycastTarget = true;
        var edge = MenuKit.Solid("Edge", card, _assets.accent); MenuKit.Anchor(edge.rectTransform, 0, 0, 0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
        _dialogTitle = MenuKit.Text(_assets, "Title", card, "", MenuKit.Face.Chinese, 30, _assets.ink); MenuKit.TopLeft(_dialogTitle.rectTransform, 44, 40, 480, 46);
        _dialogCaption = MenuKit.Text(_assets, "Caption", card, "", MenuKit.Face.Chinese, 13, _assets.muted); MenuKit.TopLeft(_dialogCaption.rectTransform, 44, 96, 480, 24);
        var cancel = MenuKit.MenuButton(_assets, card, "ESC", "Cancel", "取消", false, CloseDialog);
        MenuKit.TopLeft((RectTransform)cancel.transform, 44, 170, 220, 66);
        var confirm = MenuKit.MenuButton(_assets, card, "OK", "Confirm", "确认", true, () => { var action = _dialogAction; CloseDialog(); action?.Invoke(); });
        MenuKit.TopLeft((RectTransform)confirm.transform, 296, 170, 220, 66);
        _dialogItems = new Selectable[] { cancel, confirm };
        cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = confirm, selectOnLeft = confirm };
        confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = cancel, selectOnLeft = cancel };
        _dialog.gameObject.SetActive(false);
    }

    // ───────────────────────── 交互 ─────────────────────────
    private void Update()
    {
        NativeMenuButton.ReadInput();
        if (_leaving || _assets == null) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!GamePause.IsPaused) { if (CanPause()) Open(); }
            else if (_dialog.gameObject.activeSelf) CloseDialog();
            else if (!_mainPage.gameObject.activeSelf) BackToMain();
            else Close();
        }
        float dt = Time.unscaledDeltaTime;
        _open = Mathf.MoveTowards(_open, _openTarget, dt / (_assets.reduceMotion ? .01f : .28f));
        float e = _openTarget > .5f ? 1 - Mathf.Pow(1 - _open, 3) : _open * _open;
        _group.alpha = e; _panel.anchoredPosition = new Vector2(Mathf.Lerp(-640, 0, e), 0);
        UpdateIndicator(dt);
    }

    private bool CanPause()
    {
        if (SceneTransition.Covering) return false;
        var manager = GameManager.Instance;
        if (manager != null && (!manager.HasStarted || manager.IsGameOver)) return false;
        var player = FindObjectOfType<PlayerCombat>();
        return player != null && player.CurrentHealth > 0;
    }

    public void Open()
    {
        GamePause.Pause(); UiSfx.Play(UiSfx.Cue.Open);
        _openTarget = 1; _group.blocksRaycasts = _group.interactable = true;
        _refreshToggles?.Invoke(); ShowPage(_mainPage, false);
    }

    public void Close()
    {
        if (!GamePause.IsPaused) return;
        NativeMenuButton.ResetForPage(_group.transform);
        UiSfx.Play(UiSfx.Cue.Close); _openTarget = 0; _group.blocksRaycasts = _group.interactable = false;
        _dialog.gameObject.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        GamePause.Resume();
    }

    private void OpenSub(RectTransform page, string title, string subtitle) { UiSfx.Play(UiSfx.Cue.Confirm); ShowPage(page, false); _pageTitle.text = title; _pageSubtitle.text = subtitle; }
    private void BackToMain() { UiSfx.Play(UiSfx.Cue.Back); ShowPage(_mainPage, false); }

    private void ShowPage(RectTransform page, bool keepTitle)
    {
        NativeMenuButton.ResetForPage(_group.transform);
        _mainPage.gameObject.SetActive(page == _mainPage); _settingsPage.gameObject.SetActive(page == _settingsPage); _controlsPage.gameObject.SetActive(page == _controlsPage);
        if (page == _mainPage && !keepTitle) { _pageTitle.text = "PAUSED."; _pageSubtitle.text = "暂停"; }
        var items = page == _mainPage ? _mainItems : page == _settingsPage ? _settingsItems : _controlsItems;
        if (EventSystem.current != null && items.Length > 0) EventSystem.current.SetSelectedGameObject(items[0].gameObject);
    }

    private void Ask(string title, string caption, Action action)
    {
        NativeMenuButton.ResetForPage(_group.transform);
        _dialogTitle.text = title; _dialogCaption.text = caption; _dialogAction = action;
        _dialog.gameObject.SetActive(true); UiSfx.Play(UiSfx.Cue.Open);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_dialogItems[0].gameObject);
    }

    private void CloseDialog()
    {
        if (!_dialog.gameObject.activeSelf) return;
        NativeMenuButton.ResetForPage(_group.transform);
        _dialog.gameObject.SetActive(false); _dialogAction = null; UiSfx.Play(UiSfx.Cue.Close);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_mainItems[0].gameObject);
    }

    // 主菜单页的选中指示点：跟随悬停或键盘焦点，位于按钮右侧。
    private void UpdateIndicator(float dt)
    {
        var active = NativeMenuButton.Active != null ? NativeMenuButton.Active.GetComponent<Selectable>() : null;
        bool show = _mainPage.gameObject.activeSelf && !_dialog.gameObject.activeSelf && active != null && Array.IndexOf(_mainItems, active) >= 0;
        _indicator.gameObject.SetActive(show);
        if (!show) return;
        var rect = (RectTransform)active.transform; Rect r = rect.rect;
        Vector3 goal = rect.TransformPoint(new Vector3(r.xMax + 20, r.center.y, 0));
        _indicator.position = Vector3.Lerp(_indicator.position, goal, 1 - Mathf.Exp(-16 * dt));
    }

    // ───────────────────────── 场景切换 ─────────────────────────
    private void Restart()
    {
        _leaving = true;
        SceneTransition.Go(_assets, SceneManager.GetActiveScene().path, "LOADING · 重新开始", StartBattle);
    }

    private void ToMenu()
    {
        _leaving = true;
        SceneTransition.Go(_assets, MenuScenePath, "LOADING · 返回主菜单");
    }

    private void Quit()
    {
        GamePause.Clear(); PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>重新载入的战斗场景跳过旧开始界面，直接开始。</summary>
    public static void StartBattle(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var manager = root.GetComponentInChildren<GameManager>(true);
            if (manager != null) { manager.StartGame(); return; }
        }
    }

    public static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var host = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        host.transform.SetParent(null);
    }
}
