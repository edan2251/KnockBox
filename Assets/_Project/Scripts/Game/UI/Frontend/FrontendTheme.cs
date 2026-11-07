using UnityEngine;
using Knockbox.Presentation;

namespace Knockbox.Game.UI
{
    /// <summary>R5 시안의 편집 가능한 글자·색·구도. 외형은 실제 FighterView에서 가져온다.</summary>
    [CreateAssetMenu(menuName = "Knockbox/Frontend Theme", fileName = "FrontendTheme")]
    public sealed class FrontendTheme : ScriptableObject
    {
        [Header("Creation defaults (authored UI: edit its Text / RectTransform)")]
        [Tooltip("Existing Main UI uses the font saved on each Text. These font defaults only create new procedural fixtures.")]
        public Font titleFont;
        public Font textFont;
        public Font boldFont;
        public Color background = PaletteAsset.FrontendDefaults.Background;
        public Color ink = PaletteAsset.FrontendDefaults.Ink;
        public Color white = PaletteAsset.Neutral;
        public Color card = PaletteAsset.FrontendDefaults.Card;
        [Header("UI palette")]
        public Color floor = PaletteAsset.FrontendDefaults.Floor;
        public Color input = PaletteAsset.FrontendDefaults.Input;
        public Color placeholder = PaletteAsset.FrontendDefaults.Placeholder;
        public Color button = PaletteAsset.FrontendDefaults.Button;
        public Color status = PaletteAsset.FrontendDefaults.Status;
        public Color back = PaletteAsset.FrontendDefaults.Back;
        public Color hint = PaletteAsset.FrontendDefaults.Hint;
        public Color arrow = PaletteAsset.FrontendDefaults.Arrow;
        public Color inventory = PaletteAsset.FrontendDefaults.Inventory;
        public Color emptyScore = PaletteAsset.FrontendDefaults.EmptyScore;
        public Color duelLeft = PaletteAsset.FrontendDefaults.Left;
        public Color duelRight = PaletteAsset.FrontendDefaults.Right;
        public Color selectedLeft = PaletteAsset.FrontendDefaults.SelectedLeft;
        public Color selectedRight = PaletteAsset.FrontendDefaults.SelectedRight;
        public Color inactiveLeft = PaletteAsset.FrontendDefaults.InactiveLeft;
        public Color inactiveRight = PaletteAsset.FrontendDefaults.InactiveRight;
        public string logo = "KNOCKBOX";
        public string createRoom = "방 만들기";
        public string joinRoom = "코드로 참가";
        public string practice = "연습";
        [Tooltip("생성 기본값. 기존 Main 초상은 실제 프리팹의 RectTransform을 편집하세요. VS/카드/결과는 같은 초상을 사용합니다.")]
        public Vector2 portraitSize = new Vector2(883.2f, 1026f);
        public Vector2 portraitOffset = new Vector2(-211.2f, 324f);
        public float portraitAngle = 7f;
        public float transitionSeconds = 0.28f;

        public static FrontendTheme Load()
        {
            var theme = Resources.Load<FrontendTheme>("UI/FrontendTheme");
            if (theme == null)
            {
                theme = CreateInstance<FrontendTheme>();
                theme.hideFlags = HideFlags.DontSave;
            }
            theme.titleFont ??= Resources.Load<Font>("UI/HSJiptokki_Black_");
            theme.textFont ??= Resources.Load<Font>("UI/Paperlogy_4Regular");
            theme.boldFont ??= Resources.Load<Font>("UI/Paperlogy_7Bold");
            return theme;
        }
    }
}
