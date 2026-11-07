using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 선택 화면(CR-003 개정, CP22): 경기 시작의 초기 폼 선택과 세트 패자 선택.
    /// 선택지는 고르는 쪽 화면 절반에만 보이고, 고른 뒤에는 결과 대신 READY만 보인다(비공개 근사, D-046).
    /// 각 줄은 [키] 이름과 코드로 그린 모양(가위 삼각형, 바위 원, 보 사각형, 증강 고리).
    /// </summary>
    public sealed class PickPanelView : MonoBehaviour
    {
        const float RowGap = 0.9f;
        const float IconSize = 0.45f;

        static readonly string[][] KeyNames =
        {
            new[] { "SPACE", "F", "SHIFT", "E" },
            new[] { "NUM0", "NUM1", "NUM2", "NUM3" },
        };

        readonly SpriteRenderer[,] icons = new SpriteRenderer[2, MatchState.MaxOptions];
        readonly TextMesh[,] labels = new TextMesh[2, MatchState.MaxOptions];
        readonly TextMesh[] status = new TextMesh[2];
        readonly string[,] shownLabels = new string[2, MatchState.MaxOptions];
        readonly string[] shownStatus = new string[2];
        PaletteAsset palette;
        ShapeLibrary shapes;
        float halfWidth = ArenaDef.ClassicColumns * 0.5f;

        /// <summary>증강 번호 → 화면 이름(CP23이 채운다).</summary>
        public System.Func<int, string> AugmentName { get; set; } = id => $"AUG {id}";

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapeLibrary)
        {
            palette = paletteAsset;
            shapes = shapeLibrary;
            for (int f = 0; f < 2; f++)
            {
                status[f] = CreateText($"Status{f}", 72, 0.12f, palette.flash);
                for (int i = 0; i < MatchState.MaxOptions; i++)
                {
                    icons[f, i] = ShapeLibrary.Create(transform, $"Icon{f}_{i}", shapes.Square, SortingLayers.UI, 6, palette.FighterBody(f));
                    labels[f, i] = CreateText($"Label{f}_{i}", 56, 0.1f, palette.flash);
                    labels[f, i].anchor = TextAnchor.MiddleLeft;
                }
            }
            Hide();
        }

        public void SetArena(ArenaDef arena) => halfWidth = arena.HalfWidth;

        TextMesh CreateText(string objectName, int fontSize, float characterSize, Color color)
        {
            var go = new GameObject(SceneObjectName.Normalize(objectName));
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = text.font.material;
            mr.sortingLayerName = SortingLayers.UI;
            mr.sortingOrder = 7;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Left;
            text.fontStyle = FontStyle.Bold;
            text.color = PaletteAsset.ForText(color);
            text.text = "";
            return text;
        }

        void Hide()
        {
            for (int f = 0; f < 2; f++)
            {
                SetStatus(f, "");
                for (int i = 0; i < MatchState.MaxOptions; i++)
                    SetRow(f, i, false, PickOption.None);
            }
        }

        void SetStatus(int f, string text)
        {
            if (string.Equals(shownStatus[f], text))
                return;
            shownStatus[f] = text;
            status[f].text = text;
        }

        void SetRow(int f, int i, bool visible, int option)
        {
            icons[f, i].enabled = visible;
            string text = visible ? $"[{KeyNames[f][i]}]  {OptionName(option)}" : "";
            if (!string.Equals(shownLabels[f, i], text))
            {
                shownLabels[f, i] = text;
                labels[f, i].text = text;
            }
            if (!visible)
                return;
            icons[f, i].sprite = PickOption.IsForm(option)
                ? PickOption.FormOf(option) == FighterForm.Scissors ? shapes.Triangle : PickOption.FormOf(option) == FighterForm.Rock ? shapes.Circle : shapes.Square
                : shapes.Ring;
            icons[f, i].color = PickOption.IsForm(option) ? palette.FighterBody(f) : palette.energy;
        }

        string OptionName(int option)
        {
            if (PickOption.IsForm(option))
                return PickOption.FormOf(option) == FighterForm.Scissors ? "SCISSORS" : PickOption.FormOf(option) == FighterForm.Rock ? "ROCK" : "PAPER";
            return PickOption.IsAugment(option) ? AugmentName(PickOption.AugmentOf(option)) : "";
        }

        public void Render(in SimState state)
        {
            ref readonly var m = ref state.Match;
            if (m.Phase != MatchPhase.FormSelect && m.Phase != MatchPhase.SetPick)
            {
                Hide();
                return;
            }
            for (int f = 0; f < 2; f++)
            {
                // 각자 화면 절반의 가운데(P1 왼쪽, P2 오른쪽).
                float x = (f == 0 ? -1f : 1f) * halfWidth * 0.5f;
                status[f].transform.localPosition = new Vector3(x, 2.4f, 0f);
                bool choosing;
                int count;
                if (m.Phase == MatchPhase.FormSelect)
                {
                    choosing = (f == 0 ? m.FormChoice0 : m.FormChoice1) < 0;
                    count = Forms.PlayableCount;
                    SetStatus(f, choosing ? "PICK FORM" : "READY");
                }
                else
                {
                    choosing = m.Picker == f && m.PickedAt < 0;
                    count = m.OptionCount;
                    SetStatus(f, m.Picker == f ? (choosing ? "PICK" : "READY") : "WAIT");
                }
                for (int i = 0; i < MatchState.MaxOptions; i++)
                {
                    bool visible = choosing && i < count;
                    int option = m.Phase == MatchPhase.FormSelect ? PickOption.Form((FighterForm)i) : m.GetOption(i);
                    SetRow(f, i, visible, option);
                    if (!visible)
                        continue;
                    float y = 1.2f - i * RowGap;
                    icons[f, i].transform.localPosition = new Vector3(x - 2.6f, y, 0f);
                    ShapeLibrary.SetSize(icons[f, i], IconSize, IconSize);
                    labels[f, i].transform.localPosition = new Vector3(x - 2.1f, y, 0f);
                }
            }
        }
    }
}
