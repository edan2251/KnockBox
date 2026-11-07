using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    public sealed class FrontendButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        bool hover, down;
        [SerializeField] Button button;
        bool pointerHighlight;
        [SerializeField] Text label;
        Color hoverFill, hoverText, restingText;
        void Awake() => button = GetComponent<Button>();
        /// <summary>메인 메뉴만 포인터 위의 버튼에 색을 준다. 클릭 후 선택 포커스에는 색을 남기지 않는다.</summary>
        public void UsePointerHighlight(Color fill, Color text, Color normalText)
        {
            button ??= GetComponent<Button>();
            pointerHighlight = true;
            hoverFill = fill; hoverText = text; restingText = normalText;
            label = GetComponentInChildren<Text>();
            button.transition = Selectable.Transition.None;
            RefreshHighlight();
        }
        void RefreshHighlight()
        {
            if (!pointerHighlight) return;
            bool active = hover && button.IsInteractable();
            button.image.color = active ? hoverFill : PaletteAsset.Transparent;
            label.color = active ? hoverText : restingText;
        }
        void OnDisable() { hover = down = false; transform.localScale = Vector3.one; RefreshHighlight(); }
        void LateUpdate()
        {
            if (pointerHighlight)
            {
                if (!button.IsInteractable()) hover = down = false;
                RefreshHighlight();
            }
            else if (button.transition == Selectable.Transition.None) return; // 카드는 손/획득 시퀀스가 제어한다.
            float target = !button.interactable ? 1 : down ? .94f : hover ? 1.035f : 1;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target, 1 - Mathf.Exp(-22 * Time.unscaledDeltaTime));
        }
        public void OnPointerEnter(PointerEventData e) { hover = true; RefreshHighlight(); }
        public void OnPointerExit(PointerEventData e) { hover = down = false; RefreshHighlight(); }
        public void OnPointerDown(PointerEventData e) => down = true;
        public void OnPointerUp(PointerEventData e) => down = false;
        public void OnSelect(BaseEventData e) { if (!pointerHighlight) hover = true; }
        public void OnDeselect(BaseEventData e) { if (!pointerHighlight) hover = false; }
    }
}
