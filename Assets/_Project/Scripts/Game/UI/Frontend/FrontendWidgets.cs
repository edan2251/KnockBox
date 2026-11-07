using Knockbox.Presentation;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    public static class FrontendWidgets
    {
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = new GameObject(SceneObjectName.Normalize(name), typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static Image Image(Transform parent, string name, Rect bounds, Color color, Sprite sprite = null)
        {
            var r = Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var image = r.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Text(Transform parent, string name, Rect bounds, string value, Font font, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter)
        {
            var r = Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var text = r.gameObject.AddComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            // 공급된 제목 폰트의 행 높이가 글자 크기보다 크다. 제목이 통째로 잘리지 않게 한다.
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button Button(Transform parent, string name, Rect bounds, string value, FrontendTheme theme,
            Color fill, Color textColor, Action onClick, int size = 34)
        {
            var image = Image(parent, name, bounds, fill);
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            image.gameObject.AddComponent<FrontendButtonMotion>();
            var colors = b.colors;
            colors.highlightedColor = PaletteAsset.Scaled(PaletteAsset.Neutral, .88f);
            colors.pressedColor = PaletteAsset.Scaled(PaletteAsset.Neutral, .7f);
            colors.fadeDuration = 0.1f;
            b.colors = colors;
            Text(image.transform, "Label", new Rect(10, 4, bounds.width - 20, bounds.height - 8), value, theme.boldFont, size, textColor);
            b.onClick.AddListener(() => onClick?.Invoke());
            return b;
        }
    }

}
