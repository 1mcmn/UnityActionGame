using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>编辑器搭建并保存的 uGUI 层级；按实际相机像素适配网页构图。</summary>
public sealed class NativeMenuUI : MonoBehaviour
{
    public NativeMenuAssets assets;
    public Camera menuCamera;
    public Canvas canvas;
    public RectTransform mainPanel, savesPanel, settingsPanel, quitPanel, modalPanel, artPanel, stage;
    public RectTransform blade, stripe, bracket, ghost, orbit, halftone, caption;
    public UnityEngine.UI.Button brand, backSaves, backSettings, backQuit, newSave, continueButton, previous, next, create, cancel, quitConfirm;
    public UnityEngine.UI.Button[] mainButtons;
    public UnityEngine.UI.Slider[] volumes;
    public TMP_Text[] volumeValues;
    public TMP_InputField nameInput;
    public TMP_Text saveStatus, createError, counter, detailKicker, detailTitle, detailCaption, detailDate, detailNote, confirmLabel;
    public CanvasGroup[] details;
    public NativeMenuGraphic wave, floor;
    public NativeMenuGraphic[] ink;
    public CanvasGroup inkGroup, flashGroup;
    public RectTransform flash;
    public NativeCarouselSurface surface;
    [SerializeField] private List<TMP_Text> textItems = new List<TMP_Text>();
    [SerializeField] private List<float> textSizes = new List<float>();
    private RectTransform header, footer;
    private TMP_Text titleCN, titleEN, intro, archiveTitle, archiveSubtitle, soundTitle, soundSubtitle, soundCaption, quitTitle, quitSubtitle;
    private Vector2 viewport;
    public Vector2 Viewport => viewport;
    public float Unit => 1f / Mathf.Max(.01f, canvas.scaleFactor);

    public void Build(NativeMenuAssets config, Camera camera, Canvas target)
    {
        assets = config; menuCamera = camera; canvas = target;
        header = Full("Header", transform); footer = Full("Footer", transform);
        brand = Button("Brand", header, "刃间", false, 14, true);
        Text("BrandEnglish", brand.transform, "INTERBLADE", 13, false, .3f, 0, .7f, 1);
        Text("HeaderCaption", header, "一瞬之间，锋芒尽现。", 10, true, .65f, 0, .35f, 1).alignment = TextAlignmentOptions.MidlineRight;
        Text("FooterCaption", footer, "刃间 / 第三人称动作游戏", 9, true, 0, 0, .45f, 1);
        Text("InputGuide", footer, "↑ ↓ 选择     ENTER 确认     ESC 返回", 10, true, .5f, 0, .5f, 1).alignment = TextAlignmentOptions.MidlineRight;
        var topRule = Image("Rule", header, new Color32(196, 203, 195, 255)); Anchors(topRule.rectTransform, 0, 0, 1, 0); topRule.rectTransform.sizeDelta = new Vector2(0, 1);
        var bottomRule = Image("Rule", footer, new Color32(196, 203, 195, 255)); Anchors(bottomRule.rectTransform, 0, 1, 1, 1); bottomRule.rectTransform.sizeDelta = new Vector2(0, 1);

        artPanel = Full("VisualStage", transform); artPanel.SetAsFirstSibling(); artPanel.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        ghost = Text("GhostWord", artPanel, "STRIKE", 200, false).rectTransform;
        if (assets.outlineMaterial != null) ghost.GetComponent<TMP_Text>().fontSharedMaterial = assets.outlineMaterial;
        ghost.GetComponent<TMP_Text>().color = new Color(1, 1, 1, .6f);
        halftone = Graphic("HalftoneField", artPanel, NativeMenuGraphic.Pattern.Halftone, new Color(.28f, .36f, .26f, .19f)).rectTransform;
        orbit = Graphic("Orbit", artPanel, NativeMenuGraphic.Pattern.Orbit, new Color(.66f, .72f, .63f, .55f)).rectTransform; orbit.localEulerAngles = new Vector3(0, 0, 26);
        var picture = New("BladeSculpture", artPanel).gameObject.AddComponent<UnityEngine.UI.RawImage>(); picture.texture = assets.blade; picture.raycastTarget = false; picture.material = assets.heroMaterial;
        blade = picture.rectTransform; blade.localEulerAngles = new Vector3(0, 0, 7);
        stripe = Image("ForegroundStripe", artPanel, assets.accent).rectTransform; stripe.localEulerAngles = new Vector3(0, 0, 25);
        bracket = Graphic("ForegroundBracket", artPanel, NativeMenuGraphic.Pattern.Stroke, new Color(.4f, .51f, .32f, 1)).rectTransform;
        bracket.GetComponent<NativeMenuGraphic>().path = new[] { new Vector2(0, -60), Vector2.zero, new Vector2(12, 0) };
        caption = Text("ArtCaption", artPanel, "刃 / 光 / 瞬间", 9, true).rectTransform;

        mainPanel = Full("MainScreen", transform);
        titleCN = Text("ChineseTitle", mainPanel, "刃间", 180, true); titleCN.fontStyle = FontStyles.Bold;
        titleEN = Text("EnglishTitle", mainPanel, "INTERBLADE  ↗", 32, false);
        intro = Text("Intro", mainPanel, "把下一次交锋，交给自己。", 12, true); intro.color = assets.muted;
        mainButtons = new UnityEngine.UI.Button[3];
        string[] labels = { "Start Game", "Settings", "Quit Game" }, sub = { "选择记录，进入战场", "音量设置", "退出游戏" }, symbols = { "▶", "≡", "↗" };
        for (int i = 0; i < 3; i++)
        {
            mainButtons[i] = Button(labels[i], mainPanel, labels[i], i == 0, 28);
            var label = mainButtons[i].transform.Find("Label").GetComponent<TMP_Text>(); Anchors(label.rectTransform, .15f, .32f, .73f, .94f);
            Text("Number", mainButtons[i].transform, "0" + (i + 1), 10, false, .04f, .38f, .08f, .49f).color = i == 0 ? assets.accent : assets.muted;
            Text("Subtitle", mainButtons[i].transform, sub[i], 9, true, .15f, .08f, .73f, .26f).color = i == 0 ? new Color(.75f, .79f, .73f) : assets.muted;
            Text("Glyph", mainButtons[i].transform, symbols[i], 24, true, .87f, .25f, .1f, .55f).color = i == 0 ? assets.accent : assets.ink;
            if (i > 0) { var line = Image("Rule", mainButtons[i].transform, new Color32(187, 197, 184, 255)); Anchors(line.rectTransform, 0, 0, 1, 0); line.rectTransform.sizeDelta = new Vector2(0, 1); }
        }
        savesPanel = Full("SaveScreen", transform);
        backSaves = Button("Back", savesPanel, "←  BACK", false, 13);
        newSave = Button("NewSave", savesPanel, "+  新建存档", false, 13, true);
        saveStatus = Text("SaveStatus", savesPanel, "本机菜单记录", 10, true); saveStatus.color = assets.muted;
        archiveTitle = Text("ArchiveTitle", savesPanel, "SELECT YOUR STORY.", 76, false);
        archiveSubtitle = Text("ArchiveSubtitle", savesPanel, "选择存档", 11, true); archiveSubtitle.color = assets.muted;
        stage = Full("CarouselViewport", savesPanel);
        var hit = stage.gameObject.AddComponent<UnityEngine.UI.Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        surface = stage.gameObject.AddComponent<NativeCarouselSurface>(); surface.targetGraphic = hit; surface.transition = UnityEngine.UI.Selectable.Transition.None;
        var detailRoot = Full("Details", savesPanel);
        detailKicker = Text("Chapter", detailRoot, "NEW STORY", 10, false);
        detailTitle = Text("Title", detailRoot, "新的故事", 28, true);
        detailCaption = Text("Caption", detailRoot, "从初始战场开始。", 10, true);
        detailDate = Text("Date", detailRoot, "NEW GAME", 20, false);
        detailNote = Text("Note", detailRoot, "本机记录", 9, true);
        details = new CanvasGroup[5]; TMP_Text[] fields = { detailKicker, detailTitle, detailCaption, detailDate, detailNote };
        for (int i = 0; i < fields.Length; i++) details[i] = fields[i].gameObject.AddComponent<CanvasGroup>();
        previous = Button("Previous", savesPanel, "←", false, 20, true); next = Button("Next", savesPanel, "→", false, 20, true);
        counter = Text("Counter", savesPanel, "01 / 01", 13, false); counter.alignment = TextAlignmentOptions.Center;
        continueButton = Button("Continue", savesPanel, "新建存档   ↗", true, 23, true); confirmLabel = continueButton.transform.Find("Label").GetComponent<TMP_Text>();
        Text("ScrollHint", continueButton.transform, "滚轮浏览 · 点击选择   ← → / ENTER", 9, true, 0, - .55f, 1, -.22f).color = assets.muted;
        var inkRoot = Full("ConfirmInk", savesPanel); inkGroup = inkRoot.gameObject.AddComponent<CanvasGroup>(); inkGroup.blocksRaycasts = false; inkGroup.interactable = false; inkGroup.alpha = 0;
        ink = new NativeMenuGraphic[3];
        for (int i = 0; i < 3; i++) { ink[i] = Graphic("Trace" + i, inkRoot, NativeMenuGraphic.Pattern.Stroke, new Color(.15f, .19f, .17f, i == 0 ? .88f : i == 1 ? .58f : .38f)); ink[i].strokeWidth = i == 0 ? 1.15f : i == 1 ? .9f : .8f; }

        settingsPanel = Full("SettingsScreen", transform);
        backSettings = Button("Back", settingsPanel, "←  BACK", false, 13);
        soundTitle = Text("SoundTitle", settingsPanel, "SOUND.", 86, false);
        soundSubtitle = Text("SoundSubtitle", settingsPanel, "音量设置", 11, true); soundSubtitle.color = assets.muted;
        volumes = new UnityEngine.UI.Slider[3]; volumeValues = new TMP_Text[3];
        string[] soundLabels = { "Master Volume", "Music", "Sound Effects" }, chinese = { "主音量", "音乐音量", "音效音量" };
        for (int i = 0; i < 3; i++)
        {
            var row = New("Volume" + i, settingsPanel);
            Text("Label", row, soundLabels[i], 25, false, 0, .55f, .57f, 1);
            Text("Chinese", row, chinese[i], 9, true, .57f, .55f, .3f, 1).color = assets.muted;
            volumeValues[i] = Text("Value", row, "100", 18, false, .88f, .55f, .12f, 1); volumeValues[i].alignment = TextAlignmentOptions.MidlineRight;
            var sliderRoot = New("Slider", row); Anchors(sliderRoot, 0, 0, 1, .4f);
            var track = Image("Track", sliderRoot, new Color(.69f, .74f, .65f)); Anchors(track.rectTransform, 0, .43f, 1, .57f); track.raycastTarget = true;
            var fillArea = New("FillArea", sliderRoot); Anchors(fillArea, 0, .43f, 1, .57f);
            var fill = Image("Fill", fillArea, assets.ink); Anchors(fill.rectTransform, 0, 0, 1, 1);
            var handleArea = New("HandleArea", sliderRoot); Anchors(handleArea, 0, .15f, 1, .85f);
            var handle = Image("Handle", handleArea, assets.accent); Anchors(handle.rectTransform, .5f, 0, .5f, 1); handle.rectTransform.sizeDelta = new Vector2(12, 0); handle.raycastTarget = true;
            var slider = sliderRoot.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.minValue = 0; slider.maxValue = 1;
            volumes[i] = slider;
        }
        wave = Graphic("SoundWave", settingsPanel, NativeMenuGraphic.Pattern.Wave, new Color(.46f, .56f, .4f));
        soundCaption = Text("Caption", settingsPanel, "让每次出剑，清晰可闻。                         自动保存", 10, true); soundCaption.color = assets.muted;

        quitPanel = Full("QuitScreen", transform);
        backQuit = Button("Back", quitPanel, "←  BACK", false, 13);
        quitTitle = Text("QuitTitle", quitPanel, "UNTIL NEXT TIME.", 100, false);
        quitSubtitle = Text("QuitSubtitle", quitPanel, "下次，再见锋芒。", 15, true);
        quitConfirm = Button("QuitConfirm", quitPanel, "Quit Game   ↗", true, 23);

        modalPanel = Full("CreateSaveDialog", transform); var dim = modalPanel.gameObject.AddComponent<UnityEngine.UI.Image>(); dim.color = new Color(.06f, .1f, .08f, .62f); dim.raycastTarget = true;
        var card = Image("Card", modalPanel, assets.paper); card.raycastTarget = true;
        Text("Title", card.transform, "新的故事，从这里开始。", 26, true, .07f, .77f, .86f, .15f);
        Text("Caption", card.transform, "给这张光盘一个名字。", 11, true, .07f, .66f, .86f, .08f).color = assets.muted;
        var inputRoot = Image("Name", card.transform, new Color(.9f, .92f, .88f)); inputRoot.raycastTarget = true; Anchors(inputRoot.rectTransform, .07f, .43f, .93f, .6f);
        var area = New("Text Area", inputRoot.transform); Anchors(area, .03f, 0, .97f, 1);
        var inputText = Text("Text", area, "", 20, true); var placeholder = Text("Placeholder", area, "输入 1～24 个字符", 16, true); placeholder.color = assets.muted;
        nameInput = inputRoot.gameObject.AddComponent<TMP_InputField>(); nameInput.textViewport = area; nameInput.textComponent = inputText; nameInput.placeholder = placeholder; nameInput.characterLimit = 24; nameInput.lineType = TMP_InputField.LineType.SingleLine;
        createError = Text("Error", card.transform, "", 11, true, .07f, .33f, .86f, .09f); createError.color = new Color(.65f, .22f, .16f);
        cancel = Button("Cancel", card.transform, "取消", false, 17, true); Anchors(cancel.GetComponent<RectTransform>(), .07f, .09f, .36f, .25f);
        create = Button("Create", card.transform, "创建   ↗", true, 21, true); Anchors(create.GetComponent<RectTransform>(), .43f, .09f, .93f, .25f);
        flash = Full("ScreenFlash", transform); var flashImage = flash.gameObject.AddComponent<UnityEngine.UI.Image>(); flashImage.color = assets.accent; flashImage.raycastTarget = false;
        flashGroup = flash.gameObject.AddComponent<CanvasGroup>(); flashGroup.blocksRaycasts = false; flashGroup.alpha = 0;
        RepairSymbols(); Layout(1920, 1080);
    }

    public void RepairSymbols()
    {
        FindText(mainPanel, "EnglishTitle").text = "INTERBLADE";
        for (int i = 0; i < mainButtons.Length; i++)
        {
            mainButtons[i].transform.Find("Glyph").GetComponent<TMP_Text>().text = "";
            Icon(mainButtons[i].transform, "ActionIcon", i, i == 0 ? assets.accent : assets.ink);
        }
        foreach (var button in new[] { continueButton, create, quitConfirm })
        {
            button.transform.Find("Label").GetComponent<TMP_Text>().text = button.transform.Find("Label").GetComponent<TMP_Text>().text.Replace("   ↗", "");
            Icon(button.transform, "ActionIcon", 2, assets.accent);
        }
    }
    private static void Icon(Transform parent, string name, int type, Color color)
    {
        if (parent.Find(name) != null) return;
        RectTransform rect = New(name, parent); Anchors(rect, .87f, .3f, .96f, .7f);
        if (type == 1)
        {
            for (int i = 0; i < 3; i++)
            {
                var row = New("Line" + i, rect); Anchors(row, .1f, .2f + i * .3f, .9f, .2f + i * .3f);
                row.gameObject.AddComponent<MenuWireGraphic>().Configure(new[] { Vector2.zero, Vector2.right }, color, 1.5f, 0, false);
            }
            return;
        }
        rect.gameObject.AddComponent<MenuWireGraphic>().Configure(type == 0 ?
            new[] { new Vector2(.2f, .05f), new Vector2(.9f, .5f), new Vector2(.2f, .95f) } :
            new[] { new Vector2(.12f, .1f), new Vector2(.84f, .86f), new Vector2(.84f, .26f), new Vector2(.84f, .86f), new Vector2(.24f, .86f) }, color, 1.5f, 0, type == 0);
    }

    // 可重载场景后重新取得纯布局缓存，引用对象本身已存入场景。
    private void Cache()
    {
        header = (RectTransform)transform.Find("Header"); footer = (RectTransform)transform.Find("Footer");
        titleCN = FindText(mainPanel, "ChineseTitle"); titleEN = FindText(mainPanel, "EnglishTitle"); intro = FindText(mainPanel, "Intro");
        archiveTitle = FindText(savesPanel, "ArchiveTitle"); archiveSubtitle = FindText(savesPanel, "ArchiveSubtitle");
        soundTitle = FindText(settingsPanel, "SoundTitle"); soundSubtitle = FindText(settingsPanel, "SoundSubtitle"); soundCaption = FindText(settingsPanel, "Caption");
        quitTitle = FindText(quitPanel, "QuitTitle"); quitSubtitle = FindText(quitPanel, "QuitSubtitle");
    }
    public void Layout(float width, float height)
    {
        Cache(); viewport = new Vector2(width, height); float u = Unit;
        artPanel.offsetMin = new Vector2(0, 58 * u); artPanel.offsetMax = new Vector2(0, -75 * u);
        for (int i = 0; i < textItems.Count; i++) if (textItems[i] != null) textItems[i].fontSize = textSizes[i] * u;
        float pad = Mathf.Clamp(width * .05f, 22, 96), fontSize = Mathf.Clamp(width * .118f, 120, 180);
        float compact = Mathf.Min(1, Mathf.Max(220, height - 160) / (fontSize * 1.28f + 366));
        float top = 75 + Mathf.Max(8, (height - 133 - (fontSize * 1.28f + 366) * compact) * .5f);
        fontSize *= compact;
        Place(header, pad, 0, width - pad * 2, 75); Place(footer, pad, height - 58, width - pad * 2, 58); Place(brand.GetComponent<RectTransform>(), 0, 10, 330, 54);
        Place(titleCN.rectTransform, pad - 8, top, 460, fontSize * 1.28f); titleCN.fontSize = fontSize * u;
        Place(titleEN.rectTransform, pad, top + fontSize * 1.28f, 430, 42 * compact); titleEN.fontSize = 32 * compact * u;
        Place(intro.rectTransform, pad, top + fontSize * 1.28f + 62 * compact, 500, 24 * compact);
        float actionsY = top + fontSize * 1.28f + 114 * compact, actionsW = Mathf.Min(width < 1100 ? 310 : width > 1700 ? 410 : 365, width - pad * 2);
        for (int i = 0; i < 3; i++) Place(mainButtons[i].GetComponent<RectTransform>(), pad, actionsY + (i == 0 ? 0 : 94 + (i - 1) * 82) * compact, actionsW, (i == 0 ? 88 : 76) * compact);
        float artSize = Mathf.Min(width * .65f, 920), centerY = height * .52f;
        Place(blade, width * .39f, centerY - artSize * .5f, artSize, artSize);
        Place(ghost, width * .45f, height * .18f, width * .52f, 240); ghost.GetComponent<TMP_Text>().fontSize = Mathf.Min(width * .13f, 220) * u;
        Place(halftone, width * .51f, centerY - artSize * .32f, artSize * .7f, artSize * .7f);
        Place(orbit, width * .42f, centerY - artSize * .15f, artSize * .88f, artSize * .3f);
        Place(stripe, width * .52f, height * .6f, width * .37f, 3); Place(bracket, width * .51f, height * .26f, 12, 60);
        Place(caption, width * .7f, height * .84f, 240, 30);
        bool shortWindow = height < 750;
        float heading = shortWindow ? 135 : 153, stageTop = shortWindow ? 210 : Mathf.Max(238, height * .25f);
        Place(backSaves.GetComponent<RectTransform>(), pad, 86, 120, 40); Place(newSave.GetComponent<RectTransform>(), width - pad - 140, 86, 140, 40); Place(saveStatus.rectTransform, width - pad - 410, 90, 250, 30);
        Place(archiveTitle.rectTransform, pad, heading, width - pad * 2, shortWindow ? 58 : 92); archiveTitle.fontSize = Mathf.Clamp(width * .053f, 44, shortWindow ? 60 : 82) * u;
        Place(archiveSubtitle.rectTransform, pad, heading + (shortWindow ? 62 : 96), 300, 24);
        float stageH = Mathf.Max(130, Mathf.Min(460, height * .42f, height - stageTop - 58 - (shortWindow ? 142 : 170)));
        Place(stage, 0, stageTop, width, stageH);
        float detailY = stageTop + stageH + 10, dx = pad + (width > 1200 ? width * .045f : 0), controlW = 280, cx = width - pad - controlW;
        Place(detailKicker.rectTransform, dx, detailY, 430, 20); Place(detailTitle.rectTransform, dx, detailY + 24, Mathf.Max(220, cx - dx - 30), 40);
        Place(detailCaption.rectTransform, dx, detailY + 69, 430, 23); Place(detailDate.rectTransform, dx, detailY + 97, 300, 27); Place(detailNote.rectTransform, dx + 230, detailY + 101, 350, 22);
        Place(previous.GetComponent<RectTransform>(), cx, detailY + 9, 35, 32); Place(next.GetComponent<RectTransform>(), cx + controlW - 35, detailY + 9, 35, 32); Place(counter.rectTransform, cx + 40, detailY + 9, controlW - 80, 32);
        Place(continueButton.GetComponent<RectTransform>(), cx, detailY + 51, controlW, 54);
        float soundY = Mathf.Max(140, (height - 595) * .5f);
        Place(backSettings.GetComponent<RectTransform>(), pad, soundY - 52, 120, 40); Place(soundTitle.rectTransform, pad, soundY, 540, 108); Place(soundSubtitle.rectTransform, pad, soundY + 109, 450, 25);
        for (int i = 0; i < 3; i++) Place((RectTransform)volumes[i].transform.parent, pad, soundY + 172 + i * 92, 475, 63);
        Place(wave.rectTransform, pad, soundY + 465, 475, 42); Place(soundCaption.rectTransform, pad, soundY + 518, 580, 25);
        Place(backQuit.GetComponent<RectTransform>(), pad, top - 48, 120, 40); Place(quitTitle.rectTransform, pad, top, 900, 150); Place(quitSubtitle.rectTransform, pad, top + 180, 660, 50); Place(quitConfirm.GetComponent<RectTransform>(), pad, top + 270, 280, 54);
        Place((RectTransform)modalPanel.Find("Card"), (width - Mathf.Min(520, width - 44)) * .5f, (height - 350) * .5f, Mathf.Min(520, width - 44), 350);
        flash.anchorMin = new Vector2(0, 0); flash.anchorMax = new Vector2(0, 1); flash.pivot = new Vector2(.5f, .5f); flash.sizeDelta = new Vector2(width * .85f * u, height * .3f * u);
    }
    public void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        if (rect.parent == artPanel) y -= 75;
        rect.anchoredPosition = new Vector2(x, -y) * Unit; rect.sizeDelta = new Vector2(width, height) * Unit;
    }
    private TMP_Text Text(string name, Transform parent, string value, float size, bool chinese, float x = 0, float y = 0, float w = 1, float h = 1)
    {
        var rect = New(name, parent); Anchors(rect, x, y, x + w, y + h);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = chinese ? assets.chineseFont : size >= 32 ? assets.displayFont : assets.condensedFont;
        text.text = value; text.fontSize = size; text.color = assets.ink; text.alignment = TextAlignmentOptions.MidlineLeft; text.enableWordWrapping = false;
        text.overflowMode = chinese ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        if (chinese && size <= 16) text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false; textItems.Add(text); textSizes.Add(size); return text;
    }
    private UnityEngine.UI.Button Button(string name, Transform parent, string label, bool dark, float size, bool chinese = false)
    {
        var image = Image(name, parent, dark ? assets.ink : Color.clear); image.raycastTarget = true;
        var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.transition = UnityEngine.UI.Selectable.Transition.None;
        var text = Text("Label", image.transform, label, size, chinese, .055f, 0, .89f, 1); text.color = dark ? assets.paper : assets.ink;
        var feedback = image.gameObject.AddComponent<NativeMenuButton>(); feedback.background = image; feedback.label = text;
        feedback.normal = image.color; feedback.selected = assets.accent; feedback.textNormal = text.color; feedback.textSelected = assets.ink; return button;
    }
    public static NativeMenuGraphic Graphic(string name, Transform parent, NativeMenuGraphic.Pattern kind, Color tint)
    {
        var rect = Full(name, parent); var graphic = rect.gameObject.AddComponent<NativeMenuGraphic>(); graphic.pattern = kind; graphic.color = tint; graphic.raycastTarget = false; return graphic;
    }
    private static UnityEngine.UI.Image Image(string name, Transform parent, Color tint)
    { var image = New(name, parent).gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = tint; image.raycastTarget = false; return image; }
    private static TMP_Text FindText(Transform parent, string name) => parent.Find(name).GetComponent<TMP_Text>();
    public static RectTransform New(string name, Transform parent) { var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false); obj.layer = parent.gameObject.layer; return (RectTransform)obj.transform; }
    public static RectTransform Full(string name, Transform parent) { var rect = New(name, parent); Anchors(rect, 0, 0, 1, 1); return rect; }
    public static void Anchors(RectTransform rect, float x0, float y0, float x1, float y1) { rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
