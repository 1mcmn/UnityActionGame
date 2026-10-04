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
        bool active = hover || focus; Color from = background.color, to = active ? selected : normal;
        CacheDecoration();
        for (int i = 0; i < secondary.Length; i++) if (secondary[i] != label) secondary[i].color = active ? textSelected : secondaryColors[i];
        for (int i = 0; i < icons.Length; i++) icons[i].color = active ? textSelected : iconColors[i];
        tween = Tween.Custom(this, 0f, 1f, .2f, (target, p) => {
            target.background.color = Color.Lerp(from, to, p);
            if (target.label != null) target.label.color = Color.Lerp(active ? target.textNormal : target.textSelected, active ? target.textSelected : target.textNormal, p);
        }, Ease.OutCubic, useUnscaledTime: true);
    }
    public void OnPointerEnter(PointerEventData e) { hover = true; Refresh(); }
    public void OnPointerExit(PointerEventData e) { hover = false; Refresh(); }
    public void OnSelect(BaseEventData e) { focus = true; Refresh(); }
    public void OnDeselect(BaseEventData e) { focus = false; Refresh(); }
    private void OnDisable()
    {
        tween.Stop(); hover = focus = false; if (background != null) background.color = normal; if (label != null) label.color = textNormal;
        if (secondary != null) for (int i = 0; i < secondary.Length; i++) if (secondary[i] != null) secondary[i].color = secondaryColors[i];
        if (icons != null) for (int i = 0; i < icons.Length; i++) if (icons[i] != null) icons[i].color = iconColors[i];
    }
}
