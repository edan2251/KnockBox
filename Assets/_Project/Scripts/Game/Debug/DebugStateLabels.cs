using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 디버그 글자: 왼쪽 위에 틱과 경기 단계, 왼쪽 아래에 맵·모드·P2 입력원, 파이터 위에 행동 상태와 남은 경직·히트스탑.
    /// 캡처에도 찍히도록 월드 공간 TextMesh로 그린다. 판정 원과 몸 상자는 F3 HitboxOverlay가 그린다.
    /// </summary>
    public sealed class DebugStateLabels : MonoBehaviour
    {
        const int SortingOrder = 100;
        const float CharacterSize = 0.12f;
        const int FontSize = 48;

        TextMesh header;
        TextMesh footer;
        TextMesh label0;
        TextMesh label1;

        /// <param name="color">파이터 위 상태 이름과 위 틱 줄(어두운 배경 위)</param>
        /// <param name="frameColor">아래 설정 줄(바닥 테두리 지형 위)</param>
        public void Setup(Color color, Color frameColor)
        {
            header = Create("Header", color, TextAnchor.UpperLeft);
            header.transform.localPosition = new Vector3(-15.8f, 8.85f, 0f);
            footer = Create("Footer", frameColor, TextAnchor.LowerLeft);
            footer.transform.localPosition = new Vector3(-15.8f, -8.95f, 0f);
            label0 = Create("P1", color, TextAnchor.LowerCenter);
            label1 = Create("P2", color, TextAnchor.LowerCenter);
        }

        TextMesh Create(string objectName, Color color, TextAnchor anchor)
        {
            var go = new GameObject(SceneObjectName.Normalize(objectName));
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            go.GetComponent<MeshRenderer>().sortingLayerName = Knockbox.Presentation.SortingLayers.UI;
            go.GetComponent<MeshRenderer>().sortingOrder = SortingOrder;
            text.fontSize = FontSize;
            text.characterSize = CharacterSize;
            text.anchor = anchor;
            text.color = Knockbox.Presentation.PaletteAsset.ForText(color);
            return text;
        }

        int shownTick = -1;
        int shownKey0 = -1;
        int shownKey1 = -1;
        string shownFooter;

        /// <summary>값이 바뀔 때만 글자를 새로 만든다(디버그 글자라도 매 프레임 할당은 피한다). footer는 참조가 바뀔 때만.</summary>
        public void Render(in SimState state, Vector3 fighter0, Vector3 fighter1, string footerText)
        {
            if (state.Tick != shownTick)
            {
                shownTick = state.Tick;
                header.text = $"tick {state.Tick}  {state.Match.Phase}  round {state.Match.Round}";
            }
            if (!ReferenceEquals(footerText, shownFooter))
            {
                shownFooter = footerText;
                footer.text = footerText;
            }
            UpdateLabel(label0, "P1", state.Fighter0, ref shownKey0);
            UpdateLabel(label1, "P2", state.Fighter1, ref shownKey1);
            // 가까이 붙으면 오른쪽 파이터의 글자를 한 줄 위로 올려 겹치지 않게 한다.
            bool close = Mathf.Abs(fighter0.x - fighter1.x) < StackDistance && Mathf.Abs(fighter0.y - fighter1.y) < 1f;
            Place(label0, fighter0, close && fighter0.x > fighter1.x ? LineHeight : 0f);
            Place(label1, fighter1, close && fighter1.x >= fighter0.x ? LineHeight : 0f);
        }

        const float StackDistance = 3.2f;
        // 글자 한 줄 높이(48 × 0.12 / 10 ≈ 0.58)보다 조금 크게.
        const float LineHeight = 0.6f;

        public void SetFighterLabels(bool visible)
        {
            label0.gameObject.SetActive(visible);
            label1.gameObject.SetActive(visible);
        }

        /// <summary>위 틱 줄, 아래 설정 줄, 파이터 위 상태 글자를 각각 켜고 끈다(바뀔 때만).</summary>
        public void SetVisibility(bool headerVisible, bool footerVisible, bool fightersVisible)
        {
            if (header.gameObject.activeSelf != headerVisible)
                header.gameObject.SetActive(headerVisible);
            if (footer.gameObject.activeSelf != footerVisible)
                footer.gameObject.SetActive(footerVisible);
            if (label0.gameObject.activeSelf != fightersVisible)
                SetFighterLabels(fightersVisible);
        }

        static void UpdateLabel(TextMesh label, string name, in FighterState f, ref int shownKey)
        {
            int key = (int)f.Action | (f.StunTicks << 8) | (f.HitstopTicks << 20);
            if (key == shownKey)
                return;
            shownKey = key;
            string text = $"{name} {f.Action}";
            if (f.StunTicks > 0)
                text += $" {f.StunTicks}";
            if (f.HitstopTicks > 0)
                text += $" hs{f.HitstopTicks}";
            label.text = text;
        }

        static void Place(TextMesh label, Vector3 position, float raise)
        {
            // 화면 가장자리에서 잘리지 않게 가로 위치만 안쪽으로 당긴다. 체력 바 위에 둔다.
            label.transform.localPosition = new Vector3(Mathf.Clamp(position.x, -14.5f, 14.5f), position.y + 1.3f + raise, 0f);
        }
    }
}
