using Knockbox.Presentation;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    public sealed class FrontendHover : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Action<Vector2?> Hover;
        public Action Leave;
        public void OnPointerEnter(PointerEventData e) => Hover?.Invoke(e.position);
        public void OnPointerMove(PointerEventData e) => Hover?.Invoke(e.position);
        public void OnSelect(BaseEventData e) => Hover?.Invoke(null);
        public void OnPointerExit(PointerEventData e) => Leave?.Invoke();
        public void OnDeselect(BaseEventData e) => Leave?.Invoke();
    }
}
