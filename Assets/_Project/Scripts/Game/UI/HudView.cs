using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// HUD(08-visual-direction.md 6절): 머리 위 작은 체력 바(폭 1.2, 높이 0.12, 깎인 부분은 hit 빨강으로 0.4초 남음),
    /// 화면 왼쪽 위의 작은 점수 점 두 줄(P1 골드 줄, P2 퍼플 줄, 빈 점 uiEmpty), 가운데 큰 문구(flash, 영문만 — D-016)와
    /// MatchEnd의 안내 문구. 월드 공간 스프라이트와 TextMesh라 카메라 캡처에도 찍힌다. 문구는 바뀔 때만 새로 만든다.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        const float BarWidth = 1.2f;
        const float BarHeight = 0.12f;
        const float BarOffsetY = 1.05f;
        /// <summary>에너지 줄(CR-003 4.4): 체력 바 바로 아래의 얇은 민트 줄. 다음 공격 비용보다 적으면 흐린 색.</summary>
        const float EnergyHeight = 0.05f;
        const float EnergyGap = 0.04f;
        const float TrailHold = 0.4f;
        const float TrailSpeed = 2.5f;
        const float SlotSize = 0.24f;
        const float SlotGap = 0.12f;
        /// <summary>점수 점 첫 줄(P1) 위치: 경기장 왼쪽 위 안쪽(테두리 벽 아래). SetArena로 맵에 맞춘다.</summary>
        Vector2 SlotOrigin = SlotOriginFor(ArenaDef.ClassicColumns * 0.5f, ArenaDef.ClassicRows * 0.5f);
        const int MaxSlots = 9;
        const float BannerY = 1.6f;
        const float SubY = -0.6f;
        /// <summary>카운트다운 끝의 이 틱 동안 GO(06-match-and-tools.md 1절: 앞 60틱 READY, 뒤 30틱 GO).</summary>
        public const int GoTicks = 30;

        [System.Serializable] struct Bar
        {
            public SpriteRenderer Back;
            public SpriteRenderer Trail;
            public SpriteRenderer Fill;
            public float Shown;
            public float Trailing;
            public float Hold;
        }

        [SerializeField] Bar[] bars = new Bar[2];
        [SerializeField] SpriteRenderer[] energyBack = new SpriteRenderer[2];
        [SerializeField] SpriteRenderer[] energyFill = new SpriteRenderer[2];
        [SerializeField] SpriteRenderer[] formIcons = new SpriteRenderer[2];
        [SerializeField] SpriteRenderer[] formOutlines = new SpriteRenderer[2];
        // 증강 발동 표시(CP23): 머리 위에 이름을 잠깐(AugmentShowSeconds) 띄운다. 평소에는 표시하지 않는다.
        const float AugmentShowSeconds = 0.7f;
        [SerializeField] TextMesh[] augmentTexts = new TextMesh[2];
        [SerializeField] SpriteRenderer[] augmentRings = new SpriteRenderer[2];
        readonly float[] augmentTimers = new float[2];
        ShapeLibrary shapeLibrary;
        [SerializeField] SpriteRenderer[] slots0 = new SpriteRenderer[MaxSlots];
        [SerializeField] SpriteRenderer[] slots1 = new SpriteRenderer[MaxSlots];
        PaletteAsset palette;
        [SerializeField] TextMesh banner;
        [SerializeField] TextMesh bannerShadow;
        [SerializeField] TextMesh sub;
        string shownBanner;
        string shownSub;
        float bannerAge;

        /// <summary>지금 가운데 문구(없으면 빈 문자열). 테스트·캡처 확인용.</summary>
        public string Banner => shownBanner ?? "";

        /// <summary>세트 흐름(CR-003 개정)이면 세트·세트스코어를 보여 준다.</summary>
        public bool SetFlow { get; set; }
        public bool FrontendFlow { get; set; }
        public string SubText => shownSub ?? "";

        static Vector2 SlotOriginFor(float halfWidth, float halfHeight) => new Vector2(-halfWidth + 1.45f, halfHeight - 1.45f);

        public void SetArena(ArenaDef arena) => SlotOrigin = SlotOriginFor(arena.HalfWidth, arena.HalfHeight);

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            palette = paletteAsset;
            shapeLibrary = shapes;
            for (int i = 0; i < 2; i++)
            {
                if(formOutlines[i] == null) formOutlines[i] = ShapeLibrary.Create(transform, $"Form{i}Outline", shapes.Square, SortingLayers.UI, 3, palette.ink);
                if(formIcons[i] == null) formIcons[i] = ShapeLibrary.Create(transform, $"Form{i}Icon", shapes.Square, SortingLayers.UI, 4, palette.FighterBody(i));
                if(bars[i].Back == null) bars[i].Back = ShapeLibrary.Create(transform, $"Bar{i}Back", shapes.Square, SortingLayers.UI, 0, palette.uiEmpty);
                if(bars[i].Trail == null) bars[i].Trail = ShapeLibrary.Create(transform, $"Bar{i}Trail", shapes.Square, SortingLayers.UI, 1, palette.hit);
                if(bars[i].Fill == null) bars[i].Fill = ShapeLibrary.Create(transform, $"Bar{i}Fill", shapes.Square, SortingLayers.UI, 2, palette.FighterBody(i));
                bars[i].Shown = 1f;
                bars[i].Trailing = 1f;
                bars[i].Back.enabled=bars[i].Trail.enabled=bars[i].Fill.enabled=true;
                if(energyBack[i] == null) energyBack[i] = ShapeLibrary.Create(transform, $"Energy{i}Back", shapes.Square, SortingLayers.UI, 0, palette.uiEmpty);
                if(energyFill[i] == null) energyFill[i] = ShapeLibrary.Create(transform, $"Energy{i}Fill", shapes.Square, SortingLayers.UI, 2, palette.energy);
            }
            for (int i = 0; i < 2; i++)
            {
                if(augmentRings[i] == null) augmentRings[i] = ShapeLibrary.Create(transform, $"Augment{i}Line", shapes.Square, SortingLayers.UI, 5, palette.energy);
                if(augmentTexts[i] == null) augmentTexts[i] = CreateText($"Augment{i}Text", 48, 0.08f, palette.energy, 6);
                augmentRings[i].enabled = false;
            }
            if(bannerShadow == null) bannerShadow = CreateText("BannerShadow", 128, 0.2f, palette.ink, 10);
            if(banner == null) banner = CreateText("Banner", 128, 0.2f, palette.flash, 11);
            if(sub == null) sub = CreateText("Sub", 64, 0.1f, palette.uiSub, 11);
            banner.GetComponent<Renderer>().enabled=bannerShadow.GetComponent<Renderer>().enabled=sub.GetComponent<Renderer>().enabled=true;
            foreach(var text in augmentTexts)text.GetComponent<Renderer>().enabled=true;
            for (int k = 0; k < MaxSlots; k++)
            {
                if(slots0[k] == null) slots0[k] = ShapeLibrary.Create(transform, $"Score0_{k}", shapes.Circle, SortingLayers.UI, 0, palette.uiEmpty);
                if(slots1[k] == null) slots1[k] = ShapeLibrary.Create(transform, $"Score1_{k}", shapes.Circle, SortingLayers.UI, 0, palette.uiEmpty);
                ShapeLibrary.SetSize(slots0[k], SlotSize, SlotSize);
                ShapeLibrary.SetSize(slots1[k], SlotSize, SlotSize);
            }
        }

        TextMesh CreateText(string objectName, int fontSize, float characterSize, Color color, int order)
        {
            var go = new GameObject(SceneObjectName.Normalize(objectName));
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = text.font.material;
            mr.sortingLayerName = SortingLayers.UI;
            mr.sortingOrder = order;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = PaletteAsset.ForText(color);
            text.text = "";
            return text;
        }

        /// <summary>경기 단계에 따른 가운데 문구와 색.</summary>
        void ChooseBanner(in SimState state, in MatchParams match, out string text, out Color color, out string subText)
        {
            var m = state.Match;
            subText = "";
            if (FrontendFlow) { text = ""; color = palette.flash; return; }
            switch (m.Phase)
            {
                case MatchPhase.Countdown:
                    bool go = m.PhaseTick >= match.CountdownForRound(m.Round) - GoTicks;
                    text = go ? "GO" : "READY";
                    color = palette.flash;
                    subText = go ? "" : SetFlow ? $"SET {m.Set}   {m.SetScore0} - {m.SetScore1}" : $"ROUND {m.Round}";
                    return;
                case MatchPhase.FormSelect:
                    text = "";
                    color = palette.flash;
                    return;
                case MatchPhase.SetPick:
                    // 큰 문구는 양쪽 선택 목록을 가리므로 쓰지 않고, 아래 작은 문구로만 알린다.
                    text = "";
                    color = palette.flash;
                    subText = $"P{m.Picker + 1} LOST THE SET   SETS {m.SetScore0} - {m.SetScore1}";
                    return;
                case MatchPhase.KO:
                case MatchPhase.RoundEnd:
                case MatchPhase.RoundTransition:
                    text = m.RoundWinner < 0 ? "DRAW" : "KO";
                    color = palette.flash;
                    return;
                case MatchPhase.MatchEnd:
                    int winner = SetFlow ? (m.SetScore0 >= m.SetScore1 ? 0 : 1) : m.Score0 >= m.Score1 ? 0 : 1;
                    text = winner == 0 ? "P1 WINS" : "P2 WINS";
                    color = palette.flash;
                    string sets = SetFlow ? $"SETS {m.SetScore0} - {m.SetScore1}   " : "";
                    subText = sets + (m.PhaseTick >= match.MatchEndMinTicks ? (match.RematchBothHold ? "BOTH HOLD ATTACK" : "PRESS ANY BUTTON") : "");
                    return;
                default:
                    text = "";
                    color = palette.flash;
                    return;
            }
        }

        void RenderBanner(in SimState state, in MatchParams match, float dt)
        {
            ChooseBanner(state, match, out string text, out Color color, out string subText);
            if (!string.Equals(text, shownBanner))
            {
                shownBanner = text;
                banner.text = text;
                bannerShadow.text = text;
                bannerAge = 0f;
            }
            if (!string.Equals(subText, shownSub))
            {
                shownSub = subText;
                sub.text = subText;
            }
            banner.color = PaletteAsset.ForText(color);
            bannerAge += dt;
            // 새 문구가 뜰 때 크게 튀어나왔다가 자리 잡는다.
            float pop = 1f + 0.45f * Mathf.Exp(-bannerAge * 14f);
            banner.transform.localPosition = new Vector3(0f, BannerY, 0f);
            bannerShadow.transform.localPosition = new Vector3(0.14f, BannerY - 0.14f, 0f);
            banner.transform.localScale = bannerShadow.transform.localScale = new Vector3(pop, pop, 1f);
            sub.transform.localPosition = new Vector3(0f, SubY, 0f);
        }

        public void Render(in SimState state, Vector2 body0, Vector2 body1, in MatchParams match, float maxHealth, float dt) =>
            Render(state, body0, body1, match, maxHealth, default, false, dt);

        /// <param name="energyShown">에너지 스위치가 켜져 있을 때만 줄을 그린다.</param>
        public void Render(in SimState state, Vector2 body0, Vector2 body1, in MatchParams match, float maxHealth, in EnergyParams energy, bool energyShown, float dt)
        {
            int roundsToWin = SetFlow ? match.RoundsToWinSet : match.RoundsToWin;
            RenderBanner(state, match, dt);
            RenderBar(ref bars[0], body0, Health(state.Fighter0, maxHealth), dt);
            RenderBar(ref bars[1], body1, Health(state.Fighter1, maxHealth), dt);
            RenderEnergy(0, state.Fighter0, body0, energy, energyShown);
            RenderEnergy(1, state.Fighter1, body1, energy, energyShown);

            int count = Mathf.Clamp(roundsToWin, 0, MaxSlots);
            for (int k = 0; k < MaxSlots; k++)
            {
                bool visible = k < count && !FrontendFlow;
                slots0[k].enabled = visible;
                slots1[k].enabled = visible;
                if (!visible)
                    continue;
                // 왼쪽 위에서 오른쪽으로 채운다. 윗줄 P1, 아랫줄 P2.
                float x = SlotOrigin.x + k * (SlotSize + SlotGap);
                slots0[k].transform.localPosition = new Vector3(x, SlotOrigin.y, 0f);
                slots1[k].transform.localPosition = new Vector3(x, SlotOrigin.y - SlotSize - SlotGap, 0f);
                slots0[k].color = k < state.Match.Score0 ? palette.p1Body : palette.uiEmpty;
                slots1[k].color = k < state.Match.Score1 ? palette.p2Body : palette.uiEmpty;
            }
        }

        static float Health(in FighterState f, float maxHealth) => maxHealth > 0f ? f.Health / maxHealth : 0f;

        /// <summary>증강이 발동한 순간(CP23) 머리 위에 이름을 잠깐 띄운다.</summary>
        public void ShowAugment(int fighter, string name)
        {
            augmentTimers[fighter] = AugmentShowSeconds;
            augmentTexts[fighter].text = name;
        }

        /// <summary>지금 띄운 증강 이름(없으면 빈 문자열). 테스트·캡처 확인용.</summary>
        public string AugmentShown(int fighter) => augmentTimers[fighter] > 0f ? augmentTexts[fighter].text : "";

        void RenderAugment(int i, Vector2 body, float dt)
        {
            bool on = augmentTimers[i] > 0f;
            augmentRings[i].enabled = on;
            augmentTexts[i].gameObject.SetActive(on);
            if (!on)
                return;
            augmentTimers[i] -= dt;
            float k = Mathf.Clamp01(augmentTimers[i] / AugmentShowSeconds);
            var at = new Vector3(body.x, body.y + BarOffsetY + 0.95f + (1f - k) * 0.3f, 0f);
            augmentTexts[i].transform.localPosition = at;
            // 글자를 가리지 않게 아래 얇은 밑줄(사라지며 줄어든다).
            augmentRings[i].transform.localPosition = at + new Vector3(0f, -0.2f, 0f);
            ShapeLibrary.SetSize(augmentRings[i], Mathf.Lerp(0.5f, 1.1f, k), 0.05f);
        }

        /// <summary>현재 폼 표시(10 문서 5.1): 체력 바 위의 작은 모양. 방식은 FormDisplayMode.</summary>
        public void RenderForms(in SimState state, Vector2 body0, Vector2 body1, FormDisplayMode mode, SimConfig config)
        {
            RenderForm(0, state.Fighter0, body0, mode, state.Match.Phase);
            RenderForm(1, state.Fighter1, body1, mode, state.Match.Phase);
            RenderAugment(0, body0, Time.deltaTime);
            RenderAugment(1, body1, Time.deltaTime);
        }

        void RenderForm(int i, in FighterState f, Vector2 body, FormDisplayMode mode, MatchPhase phase)
        {
            bool acting = f.Action == ActionState.Charge || f.Action == ActionState.Lunge || f.Action == ActionState.Shoot || f.Action == ActionState.Attack;
            bool shown = f.Action != ActionState.KO && phase != MatchPhase.MatchEnd && (mode != FormDisplayMode.OnAction || acting);
            formIcons[i].enabled = shown;
            formOutlines[i].enabled = shown && mode != FormDisplayMode.Minimal;
            if (!shown)
                return;
            var sprite = f.Form == FighterForm.Scissors ? shapeLibrary.Triangle : f.Form == FighterForm.Rock ? shapeLibrary.Circle : shapeLibrary.Square;
            formIcons[i].sprite = sprite;
            formOutlines[i].sprite = sprite;
            bool minimal = mode == FormDisplayMode.Minimal;
            float size = minimal ? 0.14f : 0.3f;
            var at = new Vector3(body.x, body.y + BarOffsetY + BarHeight + (minimal ? 0.14f : 0.24f), 0f);
            formIcons[i].transform.localPosition = at;
            formOutlines[i].transform.localPosition = at;
            ShapeLibrary.SetSize(formIcons[i], size, size);
            ShapeLibrary.SetSize(formOutlines[i], size + 0.08f, size + 0.08f);
            formIcons[i].color = minimal ? palette.uiSub : palette.FighterBody(i);
        }

        void RenderEnergy(int i, in FighterState f, Vector2 body, in EnergyParams energy, bool shown)
        {
            shown &= energy.Max > 0;
            energyBack[i].enabled = shown;
            energyFill[i].enabled = shown;
            if (!shown)
                return;
            float fraction = Mathf.Clamp01((float)f.Energy / energy.Max);
            float y = body.y + BarOffsetY - BarHeight * 0.5f - EnergyGap - EnergyHeight * 0.5f;
            Place(energyBack[i], body.x - BarWidth * 0.5f, y, BarWidth, EnergyHeight, 0f);
            Place(energyFill[i], body.x - BarWidth * 0.5f, y, BarWidth * fraction, EnergyHeight, 0f);
            energyFill[i].color = f.Energy >= energy.CostOf(f.Form) ? palette.energy : palette.uiSub;
        }

        void RenderBar(ref Bar bar, Vector2 body, float fraction, float dt)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction < bar.Shown)
                bar.Hold = TrailHold;
            bar.Shown = fraction;
            if (bar.Trailing < bar.Shown)
                bar.Trailing = bar.Shown;
            if (bar.Hold > 0f)
                bar.Hold -= dt;
            else
                bar.Trailing = Mathf.Max(bar.Shown, bar.Trailing - TrailSpeed * dt);

            var center = new Vector2(body.x, body.y + BarOffsetY);
            Place(bar.Back, center.x - BarWidth * 0.5f, center.y, BarWidth + 0.06f, BarHeight + 0.06f, -0.03f);
            Place(bar.Trail, center.x - BarWidth * 0.5f, center.y, BarWidth * bar.Trailing, BarHeight, 0f);
            Place(bar.Fill, center.x - BarWidth * 0.5f, center.y, BarWidth * bar.Shown, BarHeight, 0f);
        }

        static void Place(SpriteRenderer sr, float left, float y, float width, float height, float pad)
        {
            sr.transform.localPosition = new Vector3(left + pad + width * 0.5f, y, 0f);
            ShapeLibrary.SetSize(sr, Mathf.Max(width, 0.0001f), height);
        }
    }
}
