using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class NativeMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public UnityEngine.UI.Image background;
    public TMP_Text label;
    public Color normal, selected, textNormal, textSelected;
    private Tween tween;
    private bool hover, focus;
    private bool renderedActive;
    private static bool keyboardMode;
    private static Vector3 lastPointer;
    private static int inputFrame = -1;

    // 保留 EventSystem 的导航焦点，显示状态由当前输入方式决定。
    public static void ReadInput()
    {
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;
        Vector3 pointer = Input.mousePosition;
        if ((pointer - lastPointer).sqrMagnitude > 1f || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) keyboardMode = false;
        else if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
            || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)) keyboardMode = true;
        lastPointer = pointer;
    }

    public static void ResetForPage(Transform root)
    {
        keyboardMode = false; lastPointer = Input.mousePosition; inputFrame = Time.frameCount;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        foreach (var button in root.GetComponentsInChildren<NativeMenuButton>(true)) button.OnDisable();
    }

    private void LateUpdate()
    {
        ReadInput();
        if (renderedActive != IsHighlighted()) Refresh();
    }

    private bool IsHighlighted()
    {
        var selectable = GetComponent<UnityEngine.UI.Selectable>();
        return (selectable == null || selectable.IsInteractable()) && (keyboardMode ? focus : hover);
    }
    private TMP_Text[] secondary;
    private Color[] secondaryColors;
    private MenuWireGraphic[] icons;
    private Color[] iconColors;
    private void CacheDecoration()
    {
        if (secondary != null) return;
        secondary = GetComponentsInChildren<TMP_Text>(true); secondaryColors = new Color[secondary.Length];
        for (int i = 0; i < secondary.Length; i++) secondaryColors[i] = secondary[i].color;
        icons = GetComponentsInChildren<MenuWireGraphic>(true); iconColors = new Color[icons.Length];
        for (int i = 0; i < icons.Length; i++) iconColors[i] = icons[i].color;
    }
    private void Refresh()
    {
        tween.Stop();
        if (!Application.isPlaying || background == null) return;
        bool active = IsHighlighted(); renderedActive = active; Color from = background.color, to = active ? selected : normal;
        Color labelFrom = label != null ? label.color : textNormal, labelTo = active ? textSelected : textNormal;
        if (active) Active = this; else if (Active == this) Active = null;
        CacheDecoration();
        for (int i = 0; i < secondary.Length; i++) if (secondary[i] != label) secondary[i].color = active ? textSelected : secondaryColors[i];
        for (int i = 0; i < icons.Length; i++) icons[i].color = active ? textSelected : iconColors[i];
        // 选中时标签向右滑出一小段，参考 METAPHOR 选中项的位移反馈；单位按画布缩放换算。
        float unit = canvas != null ? 1f / Mathf.Max(.01f, canvas.scaleFactor) : 1f, slideFrom = slide, slideTo = active ? 10f * unit : 0f;
        tween = Tween.Custom(this, 0f, 1f, .2f, (target, p) => {
            target.background.color = Color.Lerp(from, to, p);
            if (target.label != null)
            {
                target.label.color = Color.Lerp(labelFrom, labelTo, p);
                target.slide = Mathf.Lerp(slideFrom, slideTo, p); target.label.rectTransform.anchoredPosition = target.labelOrigin + new Vector2(target.slide, 0);
            }
        }, Ease.OutCubic, useUnscaledTime: true);
    }
    /// <summary>当前悬停或键盘焦点所在的按钮，供菜单指示点跟随。</summary>
    public static NativeMenuButton Active { get; private set; }
    private Canvas canvas;
    private Vector2 labelOrigin;
    private float slide;
    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (label != null) labelOrigin = label.rectTransform.anchoredPosition;
    }
    public void OnPointerEnter(PointerEventData e)
    {
        var selectable = GetComponent<UnityEngine.UI.Selectable>();
        if (!hover && (selectable == null || selectable.IsInteractable())) UiSfx.Play(UiSfx.Cue.Hover);
        keyboardMode = false; hover = true; Refresh();
    }
    public void OnPointerExit(PointerEventData e) { hover = false; Refresh(); }
    public void OnSelect(BaseEventData e) { ReadInput(); if (keyboardMode && !focus && !hover) UiSfx.Play(UiSfx.Cue.Move); focus = true; Refresh(); }
    public void OnDeselect(BaseEventData e) { focus = false; Refresh(); }
    private void OnDisable()
    {
        tween.Stop(); hover = focus = renderedActive = false; if (Active == this) Active = null; if (background != null) background.color = normal;
        if (label != null) { label.color = textNormal; slide = 0; label.rectTransform.anchoredPosition = labelOrigin; }
        if (secondary != null) for (int i = 0; i < secondary.Length; i++) if (secondary[i] != null) secondary[i].color = secondaryColors[i];
        if (icons != null) for (int i = 0; i < icons.Length; i++) if (icons[i] != null) icons[i].color = iconColors[i];
    }
}
