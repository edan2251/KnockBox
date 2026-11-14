using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.UI
{
    /// <summary>카드 선택 확정 연출(4단계)의 무대 쪽: 고른 비석으로 시간표를 만들고, 패자 위치·흡수 점을 시간표에 맞춰 그린다.</summary>
    public sealed partial class CardPickStage
    {
        /// <summary>확정 연출 중 패자의 발 위치(퇴장 중이면 지형과 함께 아래로 빠진 값)·속도·충전 진행도(−1 없음)·공중 여부·돌진 모습 여부.</summary>
        void ConfirmFighter(out Vector2 foot, out Vector2 velocity, out float charge, out bool airborne, out bool lunging)
        {
            confirm.Fighter(confirmSince, out foot, out velocity, out charge, out airborne, out lunging);
            foot.y -= ExitDrop;
        }

        /// <summary>확정 월드에서 고른 비석과 확정 뒤 경과초를 읽는다. 확정 대기에서 퇴장 시간을 뺀 만큼에 맞춰 연출을 줄인다.</summary>
        void UpdateConfirm(in MatchState m)
        {
            confirmSince = -1;
            confirmChosen = -1;
            if (m.PickedAt < 0) return;
            confirmChosen = ChosenInstance(runner.FrontendWorld.State.GetFighter(m.Picker).Augments, m);
            if (confirmChosen < 0) return;
            if (confirm == null)
            {
                int hold = runner.FrontendWorld.Config.Match.PickHoldTicks;
                float exitSeconds = Mathf.Min(ExitSeconds, hold * .5f / SimConfig.TickRate);
                // 고른 비석의 거리·높이로 시간표를 정한다(멀수록 오래 날고, 화면 위끝까지 남은 높이만큼 다시 뛰어오른다).
                var b = basePositions[confirmChosen];
                float height = monoliths[confirmChosen].Height;
                confirm = new CardPickConfirm(hold / (float)SimConfig.TickRate - exitSeconds, Foot, new Vector2(b.x, b.y + height), b.y, height);
            }
            pickStart = m.PickedAt / (float)SimConfig.TickRate;
            confirmSince = Mathf.Max(0, stageTime - pickStart);
        }

        /// <summary>흡수 점: 고른 비석의 아이콘(황금)·이름 띠(플레이어 색)에서 흩어졌다가 패자 몸으로 빨려 든다.</summary>
        void UpdateAbsorbDots(Vector2 fighterFoot, Color accent)
        {
            var b = basePositions[confirmChosen];
            var body = fighterFoot + Vector2.up * runner.FrontendWorld.Config.HalfBody;
            // 비석이 박혀 사라진 구멍(바닥)에서 솟는다.
            var hole = new Vector2(b.x, b.y + .15f);
            for (int k = 0; k < absorbDots.Length; k++)
            {
                bool icon = k % 2 == 0;
                var from = hole + Vector2.right * ((k % 5) - 2) * .35f;
                bool show = confirm.Dot(k, confirmSince, from, body, out var position, out float size) && exitProgress <= 0;
                var dot = absorbDots[k];
                if (dot.gameObject.activeSelf != show) dot.gameObject.SetActive(show);
                if (!show) continue;
                dot.color = icon ? PaletteAsset.MonolithDefaults.IconFill : accent;
                dot.transform.localPosition = new Vector3(position.x, position.y, 0);
                ShapeLibrary.SetSize(dot, size, size);
            }
        }
    }
}
