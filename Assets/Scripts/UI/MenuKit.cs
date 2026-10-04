using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战斗内菜单（暂停、结算）的运行时 uGUI 构建工具，复刻主菜单的视觉语言：纸白底、墨色字、荧光选中、编号按钮与细分隔线。
/// 按钮复用主菜单的 NativeMenuButton，获得一致的悬停/选中动效与界面音效。
/// </summary>
public static class MenuKit
{
    public enum Face { Display, Condensed, Chinese }
    public static readonly Color Rule = new Color32(187, 197, 184, 255);

    public static Canvas MakeCanvas(string name, Transform parent, int order)
    {
        var host = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        host.transform.SetParent(parent, false); host.layer = 5;
        var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
        var scaler = host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        return canvas;
    }

    public static RectTransform New(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.layer = 5; obj.transform.SetParent(parent, false); return (RectTransform)obj.transform;
    }
    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size; }
    public static void TopLeft(RectTransform rect, float x, float y, float width, float height) => Place(rect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height));
    public static void Stretch(RectTransform rect, float inset = 0)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset); }

    public static Image Solid(string name, Transform parent, Color color, bool raycast = false)
    { var image = New(name, parent).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image; }

    public static TMP_Text Text(NativeMenuAssets assets, string name, Transform parent, string value, Face face, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = New(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        var font = face == Face.Chinese ? assets.chineseFont : face == Face.Display ? assets.displayFont : assets.condensedFont;
        if (font != null) text.font = font;
        text.text = value; text.fontSize = size; text.color = color; text.alignment = alignment;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false;
        if (face == Face.Chinese && size <= 16) text.fontStyle = FontStyles.Bold;
        return text;
    }

    /// <summary>主菜单式编号按钮：编号、英文主标签、中文副标题、底部细线；primary 为墨色实心（如“继续”）。</summary>
    public static Button MenuButton(NativeMenuAssets assets, Transform parent, string number, string english, string chinese, bool primary, Action onClick)
    {
        var image = Solid(english, parent, primary ? assets.ink : new Color(0, 0, 0, 0), true);
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
        var index = Text(assets, "Number", image.transform, number, Face.Condensed, 11, primary ? assets.accent : assets.muted);
        Anchor(index.rectTransform, .04f, .45f, .12f, .9f);
        var label = Text(assets, "Label", image.transform, english, Face.Condensed, 26, primary ? assets.paper : assets.ink);
        label.fontStyle = FontStyles.Bold; Anchor(label.rectTransform, .14f, .36f, .95f, .98f);
        var sub = Text(assets, "Subtitle", image.transform, chinese, Face.Chinese, 10, primary ? new Color(.75f, .79f, .73f) : assets.muted);
        Anchor(sub.rectTransform, .14f, .04f, .95f, .36f);
        if (!primary) { var line = Solid("Rule", image.transform, Rule); Anchor(line.rectTransform, 0, 0, 1, 0); line.rectTransform.sizeDelta = new Vector2(0, 1); }
        Feedback(assets, button, image, label);
        if (onClick != null) button.onClick.AddListener(() => onClick());
        return button;
    }

    /// <summary>设置行开关：左侧标签，右侧 ON/OFF 芯片，整行可点。</summary>
    public static Button ToggleRow(NativeMenuAssets assets, Transform parent, string english, string chinese, Func<bool> read, Action<bool> write, out Action refresh)
    {
        var image = Solid(english, parent, new Color(0, 0, 0, 0), true);
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
        var label = Text(assets, "Label", image.transform, english, Face.Condensed, 22, assets.ink); Anchor(label.rectTransform, .02f, 0, .6f, 1);
        var sub = Text(assets, "Chinese", image.transform, chinese, Face.Chinese, 10, assets.muted); Anchor(sub.rectTransform, .45f, 0, .75f, 1);
        // 状态用方块颜色表示（墨色＝开），文字保持墨色，避免与按钮悬停换色冲突。
        var chip = Solid("Chip", image.transform, assets.ink); Anchor(chip.rectTransform, .955f, .5f, .955f, .5f); chip.rectTransform.sizeDelta = new Vector2(14, 14);
        var value = Text(assets, "Value", image.transform, "ON", Face.Condensed, 18, assets.ink, TextAlignmentOptions.MidlineRight); Anchor(value.rectTransform, .75f, 0, .93f, 1);
        var line = Solid("Rule", image.transform, Rule); Anchor(line.rectTransform, 0, 0, 1, 0); line.rectTransform.sizeDelta = new Vector2(0, 1);
        refresh = () => { bool on = read(); value.text = on ? "ON" : "OFF"; chip.color = on ? assets.ink : Rule; };
        var update = refresh;
        button.onClick.AddListener(() => { write(!read()); update(); UiSfx.Play(UiSfx.Cue.Confirm); });
        Feedback(assets, button, image, label);
        refresh();
        return button;
    }

    /// <summary>主菜单式滑块：标签、中文、右侧数值，下方墨色填充轨道与荧光手柄。</summary>
    public static Slider SliderRow(NativeMenuAssets assets, Transform parent, string english, string chinese, float min, float max, float value,
        Func<float, string> format, Action<float> onChange)
    {
        var row = New(english, parent);
        var label = Text(assets, "Label", row, english, Face.Condensed, 22, assets.ink); Anchor(label.rectTransform, 0, .5f, .6f, 1);
        var sub = Text(assets, "Chinese", row, chinese, Face.Chinese, 10, assets.muted); Anchor(sub.rectTransform, .45f, .5f, .75f, 1);
        var number = Text(assets, "Value", row, format(value), Face.Condensed, 20, assets.ink, TextAlignmentOptions.MidlineRight); Anchor(number.rectTransform, .75f, .5f, 1, 1);
        var root = New("Slider", row); Anchor(root, 0, 0, 1, .45f);
        var track = Solid("Track", root, new Color(.69f, .74f, .65f), true); Anchor(track.rectTransform, 0, .4f, 1, .6f);
        var fillArea = New("FillArea", root); Anchor(fillArea, 0, .4f, 1, .6f);
        var fill = Solid("Fill", fillArea, assets.ink); Stretch(fill.rectTransform);
        var handleArea = New("HandleArea", root); Anchor(handleArea, 0, .1f, 1, .9f);
        var handle = Solid("Handle", handleArea, assets.accent, true); Anchor(handle.rectTransform, .5f, 0, .5f, 1); handle.rectTransform.sizeDelta = new Vector2(12, 0);
        var border = handle.gameObject.AddComponent<Outline>(); border.effectColor = assets.ink; border.effectDistance = new Vector2(1.5f, -1.5f);
        var slider = root.gameObject.AddComponent<Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
        slider.minValue = min; slider.maxValue = max; slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v => {
            number.text = format(v); onChange(v);
            UiSfx.Play(UiSfx.Cue.Slider, .8f + Mathf.InverseLerp(min, max, v) * .6f);
        });
        return slider;
    }

    /// <summary>键位行：描边键帽 + 说明。</summary>
    public static void KeyRow(NativeMenuAssets assets, Transform parent, string key, string description, float y, float width)
    {
        var row = New(key, parent); TopLeft(row, 0, y, width, 34);
        var cap = New("Cap", row); var plate = cap.gameObject.AddComponent<HudPlate>(); plate.border = 1; plate.color = assets.ink; plate.raycastTarget = false;
        Anchor(cap, 0, .1f, 0, .9f); cap.sizeDelta = new Vector2(Mathf.Max(40, key.Length * 11 + 18), 0); cap.pivot = new Vector2(0, .5f); cap.anchoredPosition = Vector2.zero;
        var keyText = Text(assets, "Key", cap, key, Face.Condensed, 14, assets.ink, TextAlignmentOptions.Center); Stretch(keyText.rectTransform); keyText.fontStyle = FontStyles.Bold;
        var text = Text(assets, "Text", row, description, Face.Chinese, 13, assets.ink); Anchor(text.rectTransform, 0, 0, 1, 1);
        text.rectTransform.offsetMin = new Vector2(150, 0);
        var line = Solid("Rule", row, Rule); Anchor(line.rectTransform, 0, 0, 1, 0); line.rectTransform.sizeDelta = new Vector2(0, 1);
    }

    /// <summary>按顺序设置上下循环的键盘导航。</summary>
    public static void ChainNavigation(params Selectable[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = items[(i + items.Length - 1) % items.Length], selectOnDown = items[(i + 1) % items.Length] };
            if (items[i] is Slider) { nav.selectOnLeft = null; nav.selectOnRight = null; }
            items[i].navigation = nav;
        }
    }

    public static void Anchor(RectTransform rect, float x0, float y0, float x1, float y1)
    { rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.offsetMin = rect.offsetMax = Vector2.zero; }

    private static void Feedback(NativeMenuAssets assets, Button button, Image image, TMP_Text label)
    {
        var feedback = image.gameObject.AddComponent<NativeMenuButton>(); feedback.background = image; feedback.label = label;
        feedback.normal = image.color; feedback.selected = assets.accent; feedback.textNormal = label.color; feedback.textSelected = assets.ink;
    }
}
