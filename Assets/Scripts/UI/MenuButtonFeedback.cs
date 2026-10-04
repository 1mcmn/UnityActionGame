using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [SerializeField] private RectTransform content;
    [SerializeField] private Graphic marker;
    private bool hovering;
    private bool selected;
    private Vector2 origin;

    public void Configure(RectTransform label, Graphic indicator) { content = label; marker = indicator; }
    private void Awake() { origin = content.anchoredPosition; Refresh(); }
    private void Update()
    {
        content.anchoredPosition = Vector2.Lerp(content.anchoredPosition,
            origin + Vector2.right * (hovering || selected ? 12f : 0f),
            1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
    }
    private void Refresh() { if (marker != null) marker.enabled = hovering || selected; }
    public void OnPointerEnter(PointerEventData data) { hovering = true; Refresh(); }
    public void OnPointerExit(PointerEventData data) { hovering = false; Refresh(); }
    public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
    public void OnDeselect(BaseEventData data) { selected = false; Refresh(); }
    private void OnDisable() { hovering = false; selected = false; if (content != null) content.anchoredPosition = origin; Refresh(); }
}
