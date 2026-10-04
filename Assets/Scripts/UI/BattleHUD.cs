using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 正式战斗 HUD：复用开始菜单 NativeMenuAssets 的字体、配色与切角线框语言，运行时构建 uGUI。
/// 只读取玩家/敌人公开状态，不改战斗逻辑；旧 OnGUI 面板降为 F1 调试层，旧 Slider 血条只停止渲染。
/// </summary>
public sealed class BattleHUD : MonoBehaviour
{
    public static bool StyledActive { get; private set; }
    public static bool DebugVisible { get; private set; }

    public NativeMenuAssets assets;
    public string bossTitle = "大剑守卫";
    public string bossSubtitle = "GREATSWORD";
    public KeyCode debugKey = KeyCode.F1;
    public bool hideLegacyBars = true;

    private enum Face { Display, Condensed, Chinese }
    private sealed class Bar { public RectTransform fill, lag; public Image fillImage, track; public float shown = 1, trail = 1, target = 1, hold; }

    private static readonly Vector2 TopLeft = new Vector2(0, 1), TopRight = new Vector2(1, 1);
    private Color _paper, _ink, _accent, _muted, _rule;
    private CanvasGroup _rootGroup, _comboGroup, _bossGroup, _airGroup, _toastGroup, _footerGroup, _stripeGroup;
    private Bar _hp, _posture, _bossHp, _bossPoise;
    private TMP_Text _hpValue, _comboNumber, _comboStep, _comboWord, _comboState, _bossKicker, _bossName, _bossHint, _airStatus, _toast, _eLabel;
    private RectTransform _comboNumberRect, _comboPlateRect, _stripe, _qRect, _bossRect, _playerRect, _airRect;
    private float _introAge = -1;
    private HudPlate _comboPlate;
    private Image[] _pips;
    private Image _stateDot;
    private HudPlate _hintChip, _qCap, _eCap;
    private TMP_Text _qKey, _eKey, _lmbKey, _lmbLabel;
    private HudPlate _lmbCap;

    private PlayerCombat _player;
    private PlayerAirCombat _air;
    private Enemy _enemy;
    private GreatSwordEnemyBrain _brain;
    private float _searchAt, _comboLinger, _toastUntil, _punch, _flash, _shake, _wordUntil, _stripeAge = 9, _lastHitTime = -9, _lastCombatTime = -9, _qShake;
    private int _hits, _lastRejects;
    private Vector2 _comboBase, _qBase;
    private string _lastReload;
    private bool _legacyHidden, _wasSuppressed;
    private const float HitChainWindow = 2f, FooterIdleDelay = 3f;

    private void Awake()
    {
        if (assets == null)
        {
            Debug.LogWarning("[BattleHUD] 未指定 NativeMenuAssets，使用默认配色与 TMP 默认字体；请执行 Tools/战斗HUD/应用菜单风格 HUD 到当前场景。", this);
            assets = ScriptableObject.CreateInstance<NativeMenuAssets>();
        }
        _paper = assets.paper; _ink = assets.ink; _accent = assets.accent; _muted = assets.muted; _rule = new Color32(187, 197, 184, 255);
        Build();
        // 暂停菜单与结算界面随战斗 HUD 一起挂载，场景无需额外接线。
        if (GetComponent<PauseMenu>() == null) gameObject.AddComponent<PauseMenu>().Init(assets);
        if (GetComponent<ResultScreen>() == null) gameObject.AddComponent<ResultScreen>().Init(assets);
    }

    private void OnEnable() { StyledActive = true; CombatImpact.OnImpact += OnImpact; }
    private void OnDisable() { StyledActive = false; DebugVisible = false; CombatImpact.OnImpact -= OnImpact; }

    /// <summary>命中瞬间驱动连段数字：计数、放大、反色闪、抖动；重击额外显示字样与全屏斜线。</summary>
    private void OnImpact(CombatImpact.Info info)
    {
        float now = Time.unscaledTime;
        _lastCombatTime = now;
        if (info.kind == CombatImpact.Kind.Quake) return;
        bool counts = info.CountsAsHit;
        if (counts)
        {
            _hits = now - _lastHitTime <= HitChainWindow ? _hits + 1 : 1;
            _lastHitTime = now;
        }
        _comboLinger = Mathf.Max(_comboLinger, HitChainWindow);
        float weight = Mathf.InverseLerp(1f, 3f, info.strength);
        _punch = Mathf.Max(_punch, counts ? .6f + .4f * weight : .4f);
        _flash = info.kind == CombatImpact.Kind.Guarded ? .4f : 1f;
        _shake = Mathf.Max(_shake, .5f + .5f * weight);
        string word = Word(info.kind);
        if (word == null && counts && _brain != null && _brain.IsDowned) word = "DOWN";
        if (word != null) { _comboWord.text = word; _wordUntil = now + .8f; }
        if (info.Heavy) _stripeAge = 0;
    }

    private static string Word(CombatImpact.Kind kind)
    {
        switch (kind)
        {
            case CombatImpact.Kind.Finisher: return "FINISH";
            case CombatImpact.Kind.Launch: return "LAUNCH";
            case CombatImpact.Kind.Slam: return "SLAM";
            case CombatImpact.Kind.Parry: return "PARRY";
            case CombatImpact.Kind.Guarded: return "GUARD";
            default: return null;
        }
    }

    // ───────────────────────── 构建 ─────────────────────────
    private void Build()
    {
        var canvasObject = new GameObject("BattleHUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        _rootGroup = canvasObject.GetComponent<CanvasGroup>(); _rootGroup.blocksRaycasts = false; _rootGroup.interactable = false; _rootGroup.alpha = 0;
        Transform root = canvasObject.transform;
        BuildStripe(root); BuildPlayer(root); BuildCombo(root); BuildBoss(root); BuildAir(root); BuildFooter(root); BuildToast(root);
    }

    /// <summary>菜单前景斜线的战斗版：重击时从中心横扫一道荧光斜线，置于最底层不遮挡面板。</summary>
    private void BuildStripe(Transform root)
    {
        _stripe = New("ImpactStripe", root); Place(_stripe, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(2800, 0));
        _stripe.localEulerAngles = new Vector3(0, 0, 18);
        _stripeGroup = _stripe.gameObject.AddComponent<CanvasGroup>(); _stripeGroup.alpha = 0;
        var shadow = Solid("Ink", _stripe, _ink.WithAlpha(.55f)); Place(shadow.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -9), new Vector2(2800, 2));
        var glow = Solid("Accent", _stripe, _accent); Place(glow.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(2800, 7));
    }

    private void BuildPlayer(Transform root)
    {
        var plate = Plate("PlayerPlate", root, TopLeft, TopLeft, new Vector2(56, -44), new Vector2(440, 100), _paper.WithAlpha(.92f), 16);
        _playerRect = plate.rectTransform;
        Tick(plate.transform);
        Label("Index", plate.transform, "01", Face.Condensed, 13, _muted, new Vector2(22, -12), new Vector2(30, 24));
        Label("Title", plate.transform, "PLAYER", Face.Display, 22, _ink, new Vector2(48, -8), new Vector2(120, 30));
        Label("Chinese", plate.transform, "玩家", Face.Chinese, 11, _muted, new Vector2(128, -13), new Vector2(60, 22));
        _hpValue = Label("Value", plate.transform, "100 / 100", Face.Condensed, 20, _ink, new Vector2(-26, -9), new Vector2(160, 28), TextAlignmentOptions.MidlineRight, TopRight);
        _hp = MakeBar(plate.transform, new Vector2(22, -48), new Vector2(392, 12), _rule, _ink, _accent, true);
        Label("PostureLabel", plate.transform, "架势", Face.Chinese, 10, _muted, new Vector2(22, -66), new Vector2(40, 20));
        _posture = MakeBar(plate.transform, new Vector2(62, -75), new Vector2(352, 4), _rule, _muted, Color.clear, false);
    }

    private void BuildCombo(Transform root)
    {
        _comboPlate = Plate("ComboPlate", root, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(56, 70), new Vector2(270, 184), _paper.WithAlpha(.92f), 16);
        var plate = _comboPlate; _comboPlateRect = plate.rectTransform; _comboBase = _comboPlateRect.anchoredPosition;
        _comboGroup = plate.gameObject.AddComponent<CanvasGroup>(); _comboGroup.alpha = 0;
        Tick(plate.transform);
        Label("Title", plate.transform, "COMBO", Face.Condensed, 16, _ink, new Vector2(22, -12), new Vector2(80, 24)).fontStyle = FontStyles.Bold;
        Label("Chinese", plate.transform, "连段", Face.Chinese, 10, _muted, new Vector2(86, -14), new Vector2(60, 20));
        _comboStep = Label("Step", plate.transform, "STEP 01 / 03", Face.Condensed, 13, _muted, new Vector2(-24, -12), new Vector2(110, 24), TextAlignmentOptions.MidlineRight, TopRight);
        Rule(plate.transform, new Vector2(22, -40), 226);
        _comboNumber = Label("Number", plate.transform, "00", Face.Display, 88, _ink, new Vector2(18, -42), new Vector2(130, 96));
        _comboNumberRect = _comboNumber.rectTransform; _comboNumberRect.pivot = new Vector2(.2f, .5f);
        _comboNumberRect.anchoredPosition = new Vector2(18 + 26, -42 - 48);
        _comboWord = Label("Word", plate.transform, "", Face.Display, 26, _ink, new Vector2(146, -56), new Vector2(110, 32));
        Label("Hits", plate.transform, "HITS", Face.Condensed, 18, _muted, new Vector2(148, -92), new Vector2(90, 30)).fontStyle = FontStyles.Bold;
        _pips = new Image[8];
        for (int i = 0; i < _pips.Length; i++)
        {
            _pips[i] = Solid("Pip" + i, plate.transform, _rule);
            Place(_pips[i].rectTransform, TopLeft, TopLeft, new Vector2(22 + i * 26, -140), new Vector2(20, 5));
        }
        var dotFrame = Solid("StateDot", plate.transform, _ink); Place(dotFrame.rectTransform, TopLeft, TopLeft, new Vector2(22, -157), new Vector2(10, 10));
        _stateDot = Solid("Core", dotFrame.transform, _accent); Stretch(_stateDot.rectTransform, 2);
        _comboState = Label("State", plate.transform, "接段窗口关闭", Face.Chinese, 11, _ink, new Vector2(40, -152), new Vector2(190, 20));
    }

    private void BuildBoss(Transform root)
    {
        var anchor = new Vector2(.5f, 0);
        var plate = Plate("BossPlate", root, anchor, anchor, new Vector2(0, 74), new Vector2(880, 92), _paper.WithAlpha(.92f), 16);
        _bossRect = plate.rectTransform;
        _bossGroup = plate.gameObject.AddComponent<CanvasGroup>(); _bossGroup.alpha = 0;
        Tick(plate.transform);
        _bossKicker = Label("Kicker", plate.transform, "BOSS · " + bossSubtitle, Face.Condensed, 13, _muted, new Vector2(24, -10), new Vector2(300, 20));
        _bossName = Label("Name", plate.transform, bossTitle, Face.Chinese, 18, _ink, new Vector2(24, -28), new Vector2(360, 28));
        _bossName.fontStyle = FontStyles.Bold;
        _hintChip = Plate("HintChip", plate.transform, TopRight, TopRight, new Vector2(-24, -14), new Vector2(300, 32), _ink, 10);
        _bossHint = Label("Hint", _hintChip.transform, "", Face.Chinese, 12, _paper, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        Stretch(_bossHint.rectTransform, 0); _bossHint.fontStyle = FontStyles.Bold;
        _bossHp = MakeBar(plate.transform, new Vector2(24, -62), new Vector2(832, 10), _rule, _ink, _accent, true);
        _bossPoise = MakeBar(plate.transform, new Vector2(24, -78), new Vector2(832, 4), _rule, _muted, Color.clear, false);
    }

    private void BuildAir(Transform root)
    {
        var plate = Plate("AirPlate", root, TopRight, TopRight, new Vector2(-56, -44), new Vector2(360, 100), _paper.WithAlpha(.92f), 16);
        _airRect = plate.rectTransform;
        _airGroup = plate.gameObject.AddComponent<CanvasGroup>(); _airGroup.alpha = 0;
        Tick(plate.transform);
        Label("Index", plate.transform, "02", Face.Condensed, 13, _muted, new Vector2(22, -12), new Vector2(30, 24));
        Label("Title", plate.transform, "AIR CHAIN", Face.Display, 22, _ink, new Vector2(48, -8), new Vector2(150, 30));
        Label("Chinese", plate.transform, "浮空追击", Face.Chinese, 11, _muted, new Vector2(160, -13), new Vector2(90, 22));
        _qCap = KeyChip(plate.transform, new Vector2(22, -44), "Q", "挑飞", out _qKey, out _);
        _eCap = KeyChip(plate.transform, new Vector2(124, -44), "E", "追击", out _eKey, out _eLabel);
        _lmbCap = KeyChip(plate.transform, new Vector2(226, -44), "LMB", "连斩", out _lmbKey, out _lmbLabel, 40);
        _qRect = _qCap.rectTransform; _qBase = _qRect.anchoredPosition;
        _airStatus = Label("Status", plate.transform, "", Face.Chinese, 10, _muted, new Vector2(22, -76), new Vector2(290, 20));
    }

    private void BuildFooter(Transform root)
    {
        var strip = Plate("Footer", root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0, 48), _paper.WithAlpha(.9f), 0);
        strip.rectTransform.anchorMax = new Vector2(1, 0);
        _footerGroup = strip.gameObject.AddComponent<CanvasGroup>();
        var line = Solid("Rule", strip.transform, _rule); line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1);
        line.rectTransform.pivot = new Vector2(.5f, 1); line.rectTransform.sizeDelta = new Vector2(-112, 1); line.rectTransform.anchoredPosition = Vector2.zero;
        Label("Caption", strip.transform, "刃间 / 第三人称动作游戏", Face.Chinese, 10, _muted, new Vector2(56, 0), new Vector2(300, 48), TextAlignmentOptions.MidlineLeft, new Vector2(0, .5f));

        var guide = New("KeyGuide", strip.transform); guide.anchorMin = guide.anchorMax = guide.pivot = new Vector2(1, .5f);
        guide.anchoredPosition = new Vector2(-56, 0); guide.sizeDelta = new Vector2(0, 26);
        var layout = guide.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        guide.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        string[,] keys = { { "WASD", "移动" }, { "SHIFT", "闪避 / 跑" }, { "SPACE", "跳跃" }, { "LMB", "攻击" }, { "RMB", "弹反" }, { "F5", "重载" }, { "F1", "调试" } };
        for (int i = 0; i < keys.GetLength(0); i++)
        {
            var cap = Plate("Key", guide, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, _ink.WithAlpha(.75f), 0); cap.border = 1;
            var capLayout = cap.gameObject.AddComponent<HorizontalLayoutGroup>(); capLayout.padding = new RectOffset(7, 7, 2, 2);
            capLayout.childControlWidth = capLayout.childControlHeight = true; capLayout.childForceExpandWidth = false;
            var key = Label("Text", cap.transform, keys[i, 0], Face.Condensed, 12, _ink, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
            key.fontStyle = FontStyles.Bold;
            var text = Label("Label", guide, keys[i, 1], Face.Chinese, 10, _ink, Vector2.zero, Vector2.zero);
            if (i < keys.GetLength(0) - 1) text.margin = new Vector4(0, 0, 14, 0);
        }
    }

    private void BuildToast(Transform root)
    {
        var chip = Plate("Toast", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -44), new Vector2(0, 40), _ink, 12);
        _toastGroup = chip.gameObject.AddComponent<CanvasGroup>(); _toastGroup.alpha = 0;
        var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.padding = new RectOffset(26, 34, 0, 0);
        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = false; layout.childAlignment = TextAnchor.MiddleCenter;
        chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        _toast = Label("Text", chip.transform, "", Face.Chinese, 13, _accent, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        _toast.fontStyle = FontStyles.Bold;
    }

    // ───────────────────────── 刷新 ─────────────────────────
    private void Start()
    {
        HideLegacyBars();
    }

    private void Update()
    {
        if (!GamePause.IsPaused && Input.GetKeyDown(debugKey)) { DebugVisible = !DebugVisible; UiSfx.Play(DebugVisible ? UiSfx.Cue.Open : UiSfx.Cue.Close); }
        float dt = Time.unscaledDeltaTime;
        if (Time.unscaledTime >= _searchAt) { _searchAt = Time.unscaledTime + .5f; Search(); }

        bool playerVisible = _player != null && _player.isActiveAndEnabled && _player.CurrentHealth > 0;
        _rootGroup.alpha = Approach(_rootGroup.alpha, playerVisible ? 1 : 0, dt, 8);
        UpdateIntro(playerVisible, dt);
        if (_player == null) return;

        UpdatePlayer(dt); UpdateCombo(dt); UpdateBoss(dt); UpdateAir(dt); UpdateToast(dt); UpdateFooter(dt); UpdateStripe(dt);
    }

    /// <summary>入场动画：转场幕布揭开后，玩家板、浮空板、Boss 板依次错开 0.08 秒滑入。</summary>
    private void UpdateIntro(bool playerVisible, float dt)
    {
        if (_introAge < 0 && playerVisible && !SceneTransition.Covering) _introAge = 0;
        else if (_introAge >= 0) _introAge += dt;
        _playerRect.anchoredPosition = new Vector2(56 - 520 * (1 - Intro(0)), -44);
        _airRect.anchoredPosition = new Vector2(-56 + 440 * (1 - Intro(1)), -44);
    }

    private float Intro(int order)
    {
        if (_introAge < 0) return 0;
        float p = Mathf.Clamp01((_introAge - order * .08f) / .5f);
        return 1 - Mathf.Pow(1 - p, 3);
    }

    /// <summary>战斗中隐藏页脚，停手一段时间后淡回；Boss 板随之下移，不留空隙。</summary>
    private void UpdateFooter(float dt)
    {
        if (_player.IsAttacking || _player.IsComboActive || (_air != null && _air.Phase != PlayerAirCombat.AirPhase.None))
            _lastCombatTime = Time.unscaledTime;
        bool idle = Time.unscaledTime - _lastCombatTime > FooterIdleDelay;
        _footerGroup.alpha = Approach(_footerGroup.alpha, idle ? 1 : 0, dt, idle ? 4 : 12);
        _bossRect.anchoredPosition = new Vector2(0, Mathf.Lerp(28, 74, _footerGroup.alpha) - 140 * (1 - Intro(2)));
    }

    private void UpdateStripe(float dt)
    {
        _stripeAge += dt;
        float grow = Mathf.Clamp01(_stripeAge / .1f), fade = Mathf.Clamp01((_stripeAge - .1f) / .28f);
        _stripe.localScale = new Vector3(1 - (1 - grow) * (1 - grow) * (1 - grow), 1, 1);
        _stripeGroup.alpha = 1 - fade;
    }

    private void Search()
    {
        if (_player == null || !_player.isActiveAndEnabled)
        {
            var found = FindObjectOfType<PlayerCombat>();
            if (found != _player) { _player = found; _air = found != null ? found.GetComponent<PlayerAirCombat>() : null; _lastReload = found != null ? found.ReloadStatus : null; }
        }
        if (_enemy != null && _enemy.isActiveAndEnabled && !_enemy.IsDead) return;
        _enemy = null; _brain = null;
        foreach (var brain in FindObjectsOfType<GreatSwordEnemyBrain>())
        {
            var enemy = brain.GetComponent<Enemy>(); if (enemy == null) enemy = brain.GetComponentInParent<Enemy>();
            if (enemy != null && !enemy.IsDead) { _enemy = enemy; _brain = brain; return; }
        }
        foreach (var enemy in FindObjectsOfType<Enemy>())
            if (!enemy.IsDead) { _enemy = enemy; _brain = enemy.GetComponent<GreatSwordEnemyBrain>(); return; }
        if (!_legacyHidden) HideLegacyBars();
    }

    private void UpdatePlayer(float dt)
    {
        float max = _player.MaxHealth, current = Mathf.Max(0, _player.CurrentHealth);
        SetBar(_hp, max > 0 ? current / max : 0, dt);
        _hpValue.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        SetBar(_posture, _player.MaxPosture > 0 ? _player.Posture / _player.MaxPosture : 0, dt);
        _posture.fillImage.color = _player.IsPostureBroken ? _accent : _muted;
        _posture.track.color = _player.IsPostureBroken ? _ink : _rule;
    }

    private void UpdateCombo(float dt)
    {
        bool active = _player.IsComboActive;
        if (active) _comboLinger = Mathf.Max(_comboLinger, .8f); else _comboLinger -= dt;
        bool chain = Time.unscaledTime - _lastHitTime <= HitChainWindow;
        _comboGroup.alpha = Approach(_comboGroup.alpha, _comboLinger > 0 || chain ? 1 : 0, dt, _comboLinger > 0 ? 16 : 5);

        // 命中冲击：数字回弹（OutBack 感）、面板反色闪、位置抖动，全部按现实时间衰减，顿帧期间也在动。
        _punch = Mathf.Max(0, _punch - dt * 4.5f);
        _flash = Mathf.Max(0, _flash - dt * 7f);
        _shake = Mathf.Max(0, _shake - dt * 5f);
        float scale = 1 + .55f * _punch * _punch - .08f * Mathf.Sin(_punch * Mathf.PI);
        _comboNumberRect.localScale = new Vector3(scale, scale, 1);
        float jitter = 9f * _shake * _shake;
        _comboPlateRect.anchoredPosition = _comboBase + new Vector2(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter) * .6f);
        _comboPlate.color = Color.Lerp(_paper.WithAlpha(.92f), _ink, _flash);
        _comboNumber.color = Color.Lerp(_ink, _accent, _flash);
        _comboWord.color = Color.Lerp(_ink, _accent, _flash);
        _comboWord.alpha = Time.unscaledTime < _wordUntil ? Mathf.Clamp01((_wordUntil - Time.unscaledTime) / .2f) : 0;
        // 链已过期但新一轮连段已起手：数字归零等待首个命中；仅淡出时保留上次计数。
        _comboNumber.text = Mathf.Min(chain || !active ? _hits : 0, 99).ToString("00");

        int step = _player.CurrentStepIndex, count = Mathf.Clamp(_player.StepCount, 1, _pips.Length);
        _comboStep.text = $"STEP {(active ? step + 1 : 0):00} / {count:00}";
        for (int i = 0; i < _pips.Length; i++)
        {
            _pips[i].enabled = i < count;
            _pips[i].color = !active ? _rule : i < step ? _ink : i == step ? _accent : _rule;
        }
        bool window = _player.ComboInputOpen, hit = _player.HitWindowOpen;
        _comboState.text = window ? "接段窗口开启 · 左键" : hit ? "攻击判定中" : "接段窗口关闭";
        _comboState.color = Color.Lerp(_ink, _paper, _flash);
        _stateDot.color = window ? _accent : hit ? _paper : _ink;
    }

    private void UpdateBoss(float dt)
    {
        bool visible = _enemy != null && _enemy.isActiveAndEnabled;
        _bossGroup.alpha = Approach(_bossGroup.alpha, visible ? 1 : 0, dt, 6);
        if (!visible) return;
        SetBar(_bossHp, _enemy.MaxHealth > 0 ? _enemy.CurrentHealth / _enemy.MaxHealth : 0, dt);
        // 压制期内架势条改为剩余压制时间（倒地追打会延长），与参考的“压制期架势条变色”对应。
        bool suppress = _brain != null && _brain.IsBroken;
        if (suppress && !_wasSuppressed) UiSfx.Play(UiSfx.Cue.Suppress);
        _wasSuppressed = suppress;
        SetBar(_bossPoise, suppress ? _brain.SuppressRemaining01 : _enemy.MaxPoise > 0 ? _enemy.CurrentPoise / _enemy.MaxPoise : 0, dt);
        bool broken = _brain != null && _brain.IsBroken;
        _bossPoise.fillImage.color = broken ? _accent : _muted;
        _bossPoise.track.color = broken ? _ink : _rule;
        _bossKicker.text = _brain == null ? "ENEMY" : broken ? "SUPPRESS · 压制期" : "BOSS · " + bossSubtitle;
        _bossKicker.color = broken ? _ink : _muted;
        _bossName.text = _brain != null ? bossTitle : "敌人";
        _hintChip.gameObject.SetActive(_brain != null);
        if (_brain == null) return;
        bool urgent = _brain.CanLaunch || _brain.CanParryNow;
        _bossHint.text = _brain.ActionHint;
        _hintChip.color = urgent ? _accent : _ink;
        _bossHint.color = urgent ? _ink : _paper;
    }

    private void UpdateAir(float dt)
    {
        bool visible = _air != null && _air.isActiveAndEnabled;
        _airGroup.alpha = Approach(_airGroup.alpha, visible ? 1 : 0, dt, 6);
        if (!visible) return;
        // Q 被拒绝时键帽横向抖动；E 在挑飞中已预约时也点亮，提示“会自动接上”。
        if (_air.RejectCount != _lastRejects) { _lastRejects = _air.RejectCount; _qShake = 1; UiSfx.Play(UiSfx.Cue.Reject); }
        _qShake = Mathf.Max(0, _qShake - dt * 4f);
        _qRect.anchoredPosition = _qBase + new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 6f * _qShake, 0);
        SetKey(_qCap, _qKey, _brain != null && _brain.CanLaunch);
        if (_qShake > 0) { _qCap.border = 0; _qCap.color = Color.Lerp(_ink.WithAlpha(.75f), _ink, _qShake); _qKey.color = Color.Lerp(_ink, _paper, _qShake); }
        // E：挑飞中=预约追击，追击/连斩中=预约砸地；左键：连斩中可接下一段。
        bool strike = _air.InStrike || _air.Phase == PlayerAirCombat.AirPhase.Chase;
        SetKey(_eCap, _eKey, _air.CanFollow || _air.ChaseQueued || _air.SlamQueued || (strike && !(_air.InStrike && _air.StrikeIndex >= _air.StrikeCount - 1)));
        _eLabel.text = _air.ChaseQueued || _air.SlamQueued ? "已预约" : strike ? "砸地" : "追击";
        bool canChain = _air.InStrike && _air.StrikeIndex < _air.StrikeCount - 1;
        SetKey(_lmbCap, _lmbKey, canChain);
        _lmbLabel.text = _air.InStrike ? $"连斩 {_air.StrikeIndex + 1}/{_air.StrikeCount}" : "连斩";
        _airStatus.text = _air.Status;
    }

    private void UpdateToast(float dt)
    {
        string status = _player.ReloadStatus;
        if (status != _lastReload)
        {
            _lastReload = status;
            bool loaded = status != null && status.StartsWith("已加载");
            _toast.text = loaded ? $"连招配置已加载  ·  v{_player.LoadedRevision}" : status;
            _toast.color = loaded ? _accent : _paper;
            _toastUntil = Time.unscaledTime + 2.5f;
            UiSfx.Play(loaded ? UiSfx.Cue.Toast : UiSfx.Cue.Error);
        }
        _toastGroup.alpha = Approach(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1 : 0, dt, 8);
    }

    private void HideLegacyBars()
    {
        if (!hideLegacyBars) return;
        foreach (var bar in FindObjectsOfType<HealthBarUI>(true)) DisableCanvas(bar);
        foreach (var bar in FindObjectsOfType<BossHealthBarUI>(true)) DisableCanvas(bar);
        _legacyHidden = true;
    }

    private static void DisableCanvas(Component component)
    {
        var canvases = component.GetComponentsInParent<Canvas>(true);
        if (canvases.Length > 0) canvases[canvases.Length - 1].enabled = false;
    }

    // ───────────────────────── 工具 ─────────────────────────
    private static float Approach(float value, float target, float dt, float speed) => Mathf.Lerp(value, target, 1 - Mathf.Exp(-speed * dt));

    private static void SetBar(Bar bar, float ratio, float dt)
    {
        ratio = Mathf.Clamp01(ratio);
        if (ratio < bar.target - .0001f) bar.hold = .45f;
        bar.target = ratio;
        bar.shown = Approach(bar.shown, ratio, dt, 20);
        if (bar.trail <= ratio) bar.trail = ratio;
        else if ((bar.hold -= dt) <= 0) bar.trail = Mathf.MoveTowards(bar.trail, ratio, .8f * dt);
        bar.fill.anchorMax = new Vector2(bar.shown, 1);
        if (bar.lag != null) bar.lag.anchorMax = new Vector2(bar.trail, 1);
    }

    private void SetKey(HudPlate cap, TMP_Text key, bool ready)
    {
        cap.border = ready ? 0 : 1; cap.color = ready ? _accent : _ink.WithAlpha(.75f); cap.SetVerticesDirty();
        key.color = _ink;
    }

    private Bar MakeBar(Transform parent, Vector2 position, Vector2 size, Color track, Color fill, Color lag, bool endTick)
    {
        var bar = new Bar();
        bar.track = Solid("Bar", parent, track); Place(bar.track.rectTransform, TopLeft, TopLeft, position, size);
        if (lag.a > 0) { var trail = Solid("Lag", bar.track.transform, lag); bar.lag = trail.rectTransform; Stretch(bar.lag, 0); }
        bar.fillImage = Solid("Fill", bar.track.transform, fill); bar.fill = bar.fillImage.rectTransform; Stretch(bar.fill, 0);
        if (endTick)
        {
            var tick = Solid("EndTick", bar.fill, _accent); var rect = tick.rectTransform;
            rect.anchorMin = new Vector2(1, 0); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, .5f); rect.sizeDelta = new Vector2(3, 0); rect.anchoredPosition = Vector2.zero;
        }
        return bar;
    }

    private HudPlate KeyChip(Transform parent, Vector2 position, string key, string label, out TMP_Text keyText, out TMP_Text labelText, float capWidth = 28)
    {
        var cap = Plate("Key" + key, parent, TopLeft, TopLeft, position, new Vector2(capWidth, 24), _ink.WithAlpha(.75f), 0); cap.border = 1;
        keyText = Label("Text", cap.transform, key, Face.Condensed, 14, _ink, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        Stretch(keyText.rectTransform, 0); keyText.fontStyle = FontStyles.Bold;
        labelText = Label("Label", parent, label, Face.Chinese, 12, _ink, position + new Vector2(capWidth + 8, 0), new Vector2(90, 24));
        labelText.fontStyle = FontStyles.Bold;
        return cap;
    }

    private void Tick(Transform plate)
    {
        var tick = Solid("AccentTick", plate, _ink); var rect = tick.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, .5f); rect.sizeDelta = new Vector2(4, 0); rect.anchoredPosition = Vector2.zero;
        var glow = Solid("Accent", tick.transform, _accent); var g = glow.rectTransform;
        g.anchorMin = new Vector2(0, 1); g.anchorMax = new Vector2(1, 1); g.pivot = new Vector2(.5f, 1); g.sizeDelta = new Vector2(0, 18); g.anchoredPosition = Vector2.zero;
    }

    private void Rule(Transform parent, Vector2 position, float width)
    {
        var line = Solid("Rule", parent, _rule); Place(line.rectTransform, TopLeft, TopLeft, position, new Vector2(width, 1));
    }

    private HudPlate Plate(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color, float cut)
    {
        var plate = New(name, parent).gameObject.AddComponent<HudPlate>(); plate.color = color; plate.cut = cut; plate.raycastTarget = false;
        Place(plate.rectTransform, anchor, pivot, position, size); return plate;
    }

    private static Image Solid(string name, Transform parent, Color color)
    {
        var image = New(name, parent).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }

    private TMP_Text Label(string name, Transform parent, string value, Face face, float size, Color color, Vector2 position, Vector2 box,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, Vector2? anchor = null)
    {
        var rect = New(name, parent); Vector2 a = anchor ?? TopLeft; Place(rect, a, a, position, box);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        var font = face == Face.Chinese ? assets.chineseFont : face == Face.Display ? assets.displayFont : assets.condensedFont;
        if (font != null) text.font = font;
        text.text = value; text.fontSize = size; text.color = color; text.alignment = alignment;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        return text;
    }

    private static RectTransform New(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.layer = 5; obj.transform.SetParent(parent, false); return (RectTransform)obj.transform;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
    }
}

internal static class BattleHUDColorExtensions
{
    public static Color WithAlpha(this Color color, float alpha) { color.a = alpha; return color; }
}
