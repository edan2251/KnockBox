using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// F3 판정 오버레이(06-match-and-tools.md 4절): 시뮬레이션의 몸 상자, 활성 공격 판정 원, 속도 벡터,
    /// 패링 구간을 실제 시뮬레이션 위치(보간 없음)에 그린다. 상태 이름과 틱 번호는 DebugStateLabels가 함께 보여 준다.
    /// 모드 A: 몸 충돌 판정을 한 순간 양쪽 위력 숫자를 0.6초 동안 띄운다(6 이상이면 위험색). 잡기 상자는 활성 틱에 spark 색.
    /// </summary>
    public sealed class HitboxOverlay : MonoBehaviour
    {
        const float Line = 0.04f;
        const float VelocityScale = 0.06f;
        const int SortingOrder = 50;
        const float PowerShowTime = 0.6f;

        struct FighterMarks
        {
            public SpriteRenderer[] Box;
            public SpriteRenderer[] Circles;
            public SpriteRenderer Velocity;
            public SpriteRenderer Parry;
            public SpriteRenderer[] GrabBox;
        }

        readonly FighterMarks[] marks = new FighterMarks[2];
        readonly TextMesh[] powerLabels = new TextMesh[2];
        readonly float[] powerShown = new float[2];
        readonly float[] powerLeft = new float[2];
        PaletteAsset palette;

        /// <summary>새 경기·시나리오를 시작할 때 남은 위력 숫자를 지운다.</summary>
        public void ClearPower()
        {
            for (int k = 0; k < 2; k++)
            {
                powerLeft[k] = 0f;
                if (powerLabels[k] != null)
                    powerLabels[k].gameObject.SetActive(false);
            }
        }

        /// <summary>몸 충돌 판정에서 계산한 위력을 잠깐 띄운다(PowerCheck 이벤트).</summary>
        public void ShowPower(int fighter, float power)
        {
            if (fighter < 0 || fighter > 1)
                return;
            powerLeft[fighter] = PowerShowTime;
            if (Mathf.Abs(power - powerShown[fighter]) < 0.05f && powerLabels[fighter].text.Length > 0)
                return;
            powerShown[fighter] = power;
            powerLabels[fighter].text = power.ToString("0.0");
            powerLabels[fighter].color = PaletteAsset.ForText(power >= 6f ? palette.hit : palette.flash);
        }

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            palette = paletteAsset;
            for (int k = 0; k < 2; k++)
            {
                var go = new GameObject($"P{k + 1}Power");
                go.transform.SetParent(transform, false);
                var text = go.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = text.font.material;
                mr.sortingLayerName = SortingLayers.UI;
                mr.sortingOrder = SortingOrder + 5;
                text.fontSize = 64;
                text.characterSize = 0.1f;
                text.anchor = TextAnchor.MiddleCenter;
                text.fontStyle = FontStyle.Bold;
                text.text = "";
                powerLabels[k] = text;
            }
            for (int k = 0; k < 2; k++)
            {
                var m = new FighterMarks
                {
                    Box = new SpriteRenderer[4],
                    Circles = new SpriteRenderer[AttackDef.MaxCircles],
                    GrabBox = new SpriteRenderer[4],
                };
                for (int i = 0; i < 4; i++)
                    m.GrabBox[i] = ShapeLibrary.Create(transform, $"P{k + 1}Grab{i}", shapes.Square, SortingLayers.UI, SortingOrder + 1, palette.energy);
                for (int i = 0; i < 4; i++)
                    m.Box[i] = ShapeLibrary.Create(transform, $"P{k + 1}Box{i}", shapes.Square, SortingLayers.UI, SortingOrder, palette.uiSub);
                for (int i = 0; i < m.Circles.Length; i++)
                    m.Circles[i] = ShapeLibrary.Create(transform, $"P{k + 1}Hit{i}", shapes.Ring, SortingLayers.UI, SortingOrder + 1, palette.hit);
                m.Velocity = ShapeLibrary.Create(transform, $"P{k + 1}Velocity", shapes.Square, SortingLayers.UI, SortingOrder + 2, palette.energySoft);
                m.Parry = ShapeLibrary.Create(transform, $"P{k + 1}Parry", shapes.Ring, SortingLayers.UI, SortingOrder, palette.energy);
                marks[k] = m;
            }
        }

        public void Render(in SimState state, SimConfig config, float dt)
        {
            Render(marks[0], state.Fighter0, config);
            Render(marks[1], state.Fighter1, config);
            for (int k = 0; k < 2; k++)
            {
                var f = state.GetFighter(k);
                var other = state.GetFighter(1 - k);
                // 붙어 있어도 겹치지 않게 상대 반대쪽 바깥에 띄운다.
                float outward = f.Position.X > other.Position.X ? 1f : f.Position.X < other.Position.X ? -1f : k == 0 ? -1f : 1f;
                powerLeft[k] = Mathf.Max(0f, powerLeft[k] - dt);
                powerLabels[k].gameObject.SetActive(powerLeft[k] > 0f);
                powerLabels[k].transform.localPosition = new Vector3(f.Position.X + outward * 0.95f, f.Position.Y + 0.25f, 0f);
            }
        }

        static void Render(in FighterMarks m, in FighterState f, SimConfig config)
        {
            float half = config.HalfBody;
            var c = new Vector2(f.Position.X, f.Position.Y);
            float size = half * 2f;
            PlaceRect(m.Box[0], c + new Vector2(0f, half), size + Line, Line);
            PlaceRect(m.Box[1], c - new Vector2(0f, half), size + Line, Line);
            PlaceRect(m.Box[2], c + new Vector2(half, 0f), Line, size + Line);
            PlaceRect(m.Box[3], c - new Vector2(half, 0f), Line, size + Line);

            bool active = FighterCombat.IsHitboxActive(f, config);
            int count = 0;
            if (active)
            {
                ref readonly var attack = ref config.GetAttack(f.AttackId);
                count = attack.UsedCircles;
                for (int i = 0; i < count; i++)
                {
                    var circle = attack.GetCircle(i);
                    var center = FighterCombat.CircleCenter(f, attack, circle);
                    var sr = m.Circles[i];
                    sr.enabled = true;
                    sr.transform.localPosition = new Vector3(center.X, center.Y, 0f);
                    ShapeLibrary.SetSize(sr, circle.Radius * 2f, circle.Radius * 2f);
                }
            }
            for (int i = count; i < m.Circles.Length; i++)
                m.Circles[i].enabled = false;

            var v = new Vector2(f.Velocity.X, f.Velocity.Y);
            float length = v.magnitude * VelocityScale;
            m.Velocity.enabled = length > 0.01f;
            if (m.Velocity.enabled)
            {
                m.Velocity.transform.localPosition = c + v.normalized * (length * 0.5f);
                m.Velocity.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                ShapeLibrary.SetSize(m.Velocity, length, Line);
            }

            bool grabbing = FighterCombat.IsGrabActive(f, config);
            for (int i = 0; i < 4; i++)
                m.GrabBox[i].enabled = grabbing;
            if (grabbing)
            {
                ref readonly var g = ref config.Combat.Grab;
                var gc = FighterCombat.GrabBoxCenter(f, config);
                var center = new Vector2(gc.X, gc.Y);
                float hw = g.BoxWidth * 0.5f, hh = g.BoxHeight * 0.5f;
                PlaceRect(m.GrabBox[0], center + new Vector2(0f, hh), g.BoxWidth + Line, Line);
                PlaceRect(m.GrabBox[1], center - new Vector2(0f, hh), g.BoxWidth + Line, Line);
                PlaceRect(m.GrabBox[2], center + new Vector2(hw, 0f), Line, g.BoxHeight + Line);
                PlaceRect(m.GrabBox[3], center - new Vector2(hw, 0f), Line, g.BoxHeight + Line);
            }

            m.Parry.enabled = f.ParryTicks > 0;
            if (m.Parry.enabled)
            {
                m.Parry.transform.localPosition = c;
                ShapeLibrary.SetSize(m.Parry, size * 1.6f, size * 1.6f);
            }
        }

        static void PlaceRect(SpriteRenderer sr, Vector2 center, float width, float height)
        {
            sr.transform.localPosition = center;
            ShapeLibrary.SetSize(sr, width, height);
        }
    }
}
