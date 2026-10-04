using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 战斗结算界面：替代旧“你赢了/你输了”文字面板。大字 VICTORY./DEFEAT.，统计用时、最高连段、造成与受到伤害，
/// 提供“再来一局 / 返回主菜单”。视觉与主菜单一致，切换场景走转场幕布。
/// </summary>
public sealed class ResultScreen : MonoBehaviour
{
    private NativeMenuAssets _assets;
    private CanvasGroup _group;
    private RectTransform _panel;
    private TMP_Text _title, _subtitle, _ghost;
    private TMP_Text[] _values;
    private Selectable[] _buttons;
    private float _battleStart = -1, _lastHit = -9, _shown, _shownTarget;
    private int _hits, _chain, _maxChain;
    private bool _leaving;
    private const float ChainWindow = 2f;

    private void OnEnable() { GameManager.UseStyledResults = true; GameManager.GameEnded += OnGameEnded; CombatImpact.OnImpact += OnImpact; }
    private void OnDisable() { GameManager.UseStyledResults = false; GameManager.GameEnded -= OnGameEnded; CombatImpact.OnImpact -= OnImpact; }

    public void Init(NativeMenuAssets assets)
    {
        _assets = assets;
        PauseMenu.EnsureEventSystem();
        var canvas = MenuKit.MakeCanvas("Result Canvas", transform, 70);
        _group = canvas.GetComponent<CanvasGroup>(); _group.alpha = 0; _group.blocksRaycasts = _group.interactable = false;
        Transform root = canvas.transform;
        MenuKit.Stretch(MenuKit.Solid("Dim", root, assets.ink.WithAlpha(.5f), true).rectTransform);

        _panel = MenuKit.New("Panel", root); MenuKit.Stretch(_panel);
        MenuKit.Stretch(MenuKit.Solid("Paper", _panel, assets.paper.WithAlpha(.97f), true).rectTransform);
        var floorRect = MenuKit.New("Grid", _panel); MenuKit.Stretch(floorRect);
        var floor = floorRect.gameObject.AddComponent<NativeMenuGraphic>(); floor.pattern = NativeMenuGraphic.Pattern.Floor; floor.color = new Color(.48f, .55f, .48f, .7f); floor.raycastTarget = false;
        _ghost = MenuKit.Text(assets, "Ghost", _panel, "CLEAR", MenuKit.Face.Display, 320, new Color(1, 1, 1, .55f), TextAlignmentOptions.TopRight);
        if (assets.outlineMaterial != null) _ghost.fontSharedMaterial = assets.outlineMaterial;
        MenuKit.Place(_ghost.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -60), new Vector2(1300, 340));
        var stripe = MenuKit.Solid("Stripe", _panel, assets.accent); MenuKit.Place(stripe.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(260, 40), new Vector2(1400, 4));
        stripe.rectTransform.localRotation = Quaternion.Euler(0, 0, 18);

        var kicker = MenuKit.Text(assets, "Kicker", _panel, "BATTLE RESULT · 战斗结算", MenuKit.Face.Condensed, 15, assets.muted); MenuKit.TopLeft(kicker.rectTransform, 120, 150, 600, 24);
        _title = MenuKit.Text(assets, "Title", _panel, "VICTORY.", MenuKit.Face.Display, 160, assets.ink); MenuKit.TopLeft(_title.rectTransform, 112, 176, 1100, 190);
        _subtitle = MenuKit.Text(assets, "Subtitle", _panel, "胜利", MenuKit.Face.Chinese, 18, assets.muted); MenuKit.TopLeft(_subtitle.rectTransform, 120, 370, 600, 30);
        var rule = MenuKit.Solid("Rule", _panel, MenuKit.Rule); MenuKit.TopLeft(rule.rectTransform, 120, 430, 980, 1);

        string[] english = { "TIME", "MAX COMBO", "DAMAGE", "TAKEN" }, chinese = { "战斗用时", "最高连段", "造成伤害", "受到伤害" };
        _values = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            float x = 120 + i * 250;
            _values[i] = MenuKit.Text(assets, "Value" + i, _panel, "0", MenuKit.Face.Display, 58, assets.ink); MenuKit.TopLeft(_values[i].rectTransform, x, 452, 240, 72);
            MenuKit.TopLeft(MenuKit.Text(assets, "Label" + i, _panel, english[i], MenuKit.Face.Condensed, 14, assets.ink).rectTransform, x, 528, 240, 22);
            MenuKit.TopLeft(MenuKit.Text(assets, "Chinese" + i, _panel, chinese[i], MenuKit.Face.Chinese, 10, assets.muted).rectTransform, x, 550, 240, 20);
        }
        var retry = MenuKit.MenuButton(assets, _panel, "01", "Retry", "再来一局", true, Retry);
        MenuKit.TopLeft((RectTransform)retry.transform, 120, 640, 420, 84);
        var menu = MenuKit.MenuButton(assets, _panel, "02", "Main Menu", "返回主菜单", false, ToMenu);
        MenuKit.TopLeft((RectTransform)menu.transform, 120, 736, 420, 70);
        _buttons = new Selectable[] { retry, menu }; MenuKit.ChainNavigation(_buttons);
        var hints = MenuKit.Text(assets, "Hints", _panel, "↑ ↓ 选择     ENTER 确认", MenuKit.Face.Chinese, 10, assets.muted);
        MenuKit.Place(hints.rectTransform, Vector2.zero, Vector2.zero, new Vector2(120, 40), new Vector2(460, 20));
        _panel.gameObject.SetActive(false);
    }

    private void OnImpact(CombatImpact.Info info)
    {
        if (!info.CountsAsHit) return;
        float now = Time.unscaledTime;
        _chain = now - _lastHit <= ChainWindow ? _chain + 1 : 1; _lastHit = now;
        _hits++; _maxChain = Mathf.Max(_maxChain, _chain);
    }

    private void Update()
    {
        if (_battleStart < 0 && GameManager.Instance != null && GameManager.Instance.HasStarted) _battleStart = Time.time;
        float dt = Time.unscaledDeltaTime;
        _shown = Mathf.MoveTowards(_shown, _shownTarget, dt / (_assets != null && _assets.reduceMotion ? .01f : .45f));
        float e = 1 - Mathf.Pow(1 - _shown, 3);
        if (_group != null) { _group.alpha = e; _panel.anchoredPosition = new Vector2(Mathf.Lerp(-220, 0, e), 0); }
    }

    private void OnGameEnded(bool victory)
    {
        if (_assets == null) return;
        StartCoroutine(Show(victory));
    }

    private IEnumerator Show(bool victory)
    {
        // 胜利留出最后一击与敌人倒地的余韵；失败时 GameManager 已延迟播放死亡动画。
        yield return new WaitForSecondsRealtime(victory ? .9f : .2f);
        float duration = _battleStart >= 0 ? Time.time - _battleStart : 0;
        float dealt = 0, taken = 0;
        foreach (var enemy in FindObjectsOfType<Enemy>(true)) dealt += Mathf.Max(0, enemy.MaxHealth - enemy.CurrentHealth);
        var player = FindObjectOfType<PlayerCombat>(true);
        if (player != null) taken = Mathf.Max(0, player.MaxHealth - player.CurrentHealth);

        _title.text = victory ? "VICTORY." : "DEFEAT.";
        _subtitle.text = victory ? "胜利 · 刃落之时，胜负已分。" : "败北 · 下一次交锋，交给自己。";
        _ghost.text = victory ? "CLEAR" : "FALLEN";
        int minutes = Mathf.FloorToInt(duration / 60), seconds = Mathf.FloorToInt(duration % 60);
        _values[0].text = $"{minutes:00}:{seconds:00}";
        _values[1].text = _maxChain.ToString("00");
        _values[2].text = Mathf.RoundToInt(dealt).ToString();
        _values[3].text = Mathf.RoundToInt(taken).ToString();

        _panel.gameObject.SetActive(true); _shownTarget = 1; _group.blocksRaycasts = _group.interactable = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        UiSfx.Play(victory ? UiSfx.Cue.Confirm : UiSfx.Cue.Back);
        if (!victory) UiSfx.Play(UiSfx.Cue.Suppress);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
    }

    private void Retry()
    {
        if (_leaving) return; _leaving = true;
        SceneTransition.Go(_assets, SceneManager.GetActiveScene().path, "LOADING · 再来一局", PauseMenu.StartBattle);
    }

    private void ToMenu()
    {
        if (_leaving) return; _leaving = true;
        SceneTransition.Go(_assets, PauseMenu.MenuScenePath, "LOADING · 返回主菜单");
    }
}
