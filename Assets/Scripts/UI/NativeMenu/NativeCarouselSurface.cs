using UnityEngine;
using UnityEngine.EventSystems;

public sealed class NativeCarouselSurface : UnityEngine.UI.Selectable, IScrollHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ISubmitHandler
{
    public NativeDiscCarousel carousel;
    private Vector2 start;
    private bool dragged;
    public void OnScroll(PointerEventData e) { if (carousel != null && carousel.InputEnabled) carousel.Wheel(e.scrollDelta.y); }
    public void OnPointerClick(PointerEventData e) { if (carousel != null && carousel.InputEnabled && !dragged && e.button == PointerEventData.InputButton.Left) carousel.Click(e.position); }
    public override void OnPointerDown(PointerEventData e) { base.OnPointerDown(e); dragged = false; start = e.position; }
    public void OnBeginDrag(PointerEventData e) { start = e.position; dragged = false; }
    public void OnDrag(PointerEventData e) { if (!dragged && carousel != null && carousel.InputEnabled && Mathf.Abs(e.position.x - start.x) >= 65) { dragged = true; carousel.Step(e.position.x < start.x ? 1 : -1); } }
    public void OnEndDrag(PointerEventData e) { }
    public override void OnMove(AxisEventData e) { if (carousel != null && carousel.InputEnabled && (e.moveDir == MoveDirection.Left || e.moveDir == MoveDirection.Right)) { carousel.Step(e.moveDir == MoveDirection.Right ? 1 : -1); e.Use(); } else base.OnMove(e); }
    public void OnSubmit(BaseEventData e) { if (carousel != null && carousel.InputEnabled) carousel.ConfirmSelection(); }
}
