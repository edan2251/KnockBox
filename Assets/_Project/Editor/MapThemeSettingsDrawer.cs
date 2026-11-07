using Knockbox.Presentation;
using UnityEditor;
using UnityEngine;

namespace Knockbox.EditorTools
{
    [CustomPropertyDrawer(typeof(MapThemeSettings))]
    public sealed class MapThemeSettingsDrawer : PropertyDrawer
    {
        const float Gap = 3f;
        static float Row => EditorGUIUtility.singleLineHeight + Gap;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var mode = (MapThemeMode)property.FindPropertyRelative("mode").enumValueIndex;
            return Row * (mode == MapThemeMode.Custom ? 9 : mode == MapThemeMode.Preset ? 5 : 3);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(row, "맵 색상", EditorStyles.boldLabel); row.y += Row;
            Draw(ref row, property, "mode", "설정 방식");
            var mode = (MapThemeMode)property.FindPropertyRelative("mode").enumValueIndex;
            if (mode == MapThemeMode.Legacy)
                EditorGUI.LabelField(row, "기존 맵 테마 색상을 그대로 사용합니다.", EditorStyles.miniLabel);
            else
            {
                ArenaTheme theme;
                if (mode == MapThemeMode.Preset)
                {
                    Draw(ref row, property, "preset", "색상 프리셋");
                    theme = PaletteAsset.MapThemeDefaults.Preset((MapThemePreset)property.FindPropertyRelative("preset").enumValueIndex);
                    if (GUI.Button(row, "이 색상으로 직접 편집"))
                    {
                        property.FindPropertyRelative("gradientTop").colorValue = theme.gradientTop;
                        property.FindPropertyRelative("gradientBottom").colorValue = theme.gradientBottom;
                        property.FindPropertyRelative("terrain").colorValue = theme.terrain;
                        property.FindPropertyRelative("prop").colorValue = theme.prop;
                        property.FindPropertyRelative("highlight").colorValue = theme.highlight;
                        property.FindPropertyRelative("gradientCurve").floatValue = theme.gradientCurve;
                        property.FindPropertyRelative("mode").enumValueIndex = (int)MapThemeMode.Custom;
                    }
                    row.y += Row;
                }
                else
                {
                    ColorField(ref row, property, "gradientTop", "배경 위쪽");
                    ColorField(ref row, property, "gradientBottom", "배경 아래쪽");
                    ColorField(ref row, property, "terrain", "땅");
                    ColorField(ref row, property, "prop", "움직이는 구조물");
                    ColorField(ref row, property, "highlight", "강조·반짝임");
                    Draw(ref row, property, "gradientCurve", "그라데이션 곡선", "1은 고른 혼합입니다. 값이 크면 아래쪽 색이 더 넓게 남습니다. 실제 경기에서는 광원과 그림자가 함께 적용됩니다.");
                    theme = new ArenaTheme
                    {
                        backgroundGradient = true,
                        gradientTop = property.FindPropertyRelative("gradientTop").colorValue,
                        gradientBottom = property.FindPropertyRelative("gradientBottom").colorValue,
                        gradientCurve = property.FindPropertyRelative("gradientCurve").floatValue
                    };
                }
                // A compact palette strip helps tune endpoints without entering Play.
                const int strips = 48;
                for (int i = 0; i < strips; i++)
                {
                    var piece = new Rect(row.x + row.width * i / strips, row.y, row.width / strips + 1, row.height);
                    EditorGUI.DrawRect(piece, theme.BackgroundAt((float)i / (strips - 1)));
                }
            }
            EditorGUI.EndProperty();
        }

        static void Draw(ref Rect row, SerializedProperty parent, string field, string label, string tooltip = null)
        { EditorGUI.PropertyField(row, parent.FindPropertyRelative(field), new GUIContent(label, tooltip)); row.y += Row; }
        static void ColorField(ref Rect row, SerializedProperty parent, string field, string label)
        {
            var property = parent.FindPropertyRelative(field);
            property.colorValue = EditorGUI.ColorField(row, new GUIContent(label), property.colorValue, true, false, false);
            row.y += Row;
        }
    }
}
