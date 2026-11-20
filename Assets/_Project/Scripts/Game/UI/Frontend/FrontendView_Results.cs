using Knockbox.Game.Maps;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 결과 발표(2026-11-20). 결과 공간 맵의 발표 값(ResultsStageSettings)과 결과 단계 틱으로 세 박자를 그린다(두 PC가 같은 틱이면 같은 순서).
    /// ① 바닥만 재구성된 뒤 빈 시상대가 땅속 울림 → 끝만 내밀고 떨림 → 갈리며 솟음 → 쾅 멈춤(모놀리스와 같은 CardPickEntrance 곡선·MonolithRiseBeats 효과).
    /// ② 시상대가 멈춘 뒤 승자·패자가 하늘에서 떨어져 착지(판정 위치는 처음부터 착지 자리, 조작 잠금 동안 그림만 위에서 내려온다).
    /// ③ 두 선수가 착지한 뒤 세트 점수 "P1 : P2"가 떨어져 밧줄에 '툭' 걸린다. 본편은 실제 물리 숫자 발판(각 숫자는 위에서 줄 하나, 콜론 두 점은 양쪽 숫자에 이음)이라
    /// 선수가 숫자 위·안쪽 가로획을 밟고 함께 흔들릴 수 있다(2026-11-20). 승자 문구·이름은 매달지 않는다(사용자 확정).
    /// 조작은 unlockTick에 풀린다(시뮬레이션 설정). 결과 공간에 있는 동안 점수는 그대로 남고(KO 재등장과 무관), 결과 공간을 떠나면 사라진다.
    /// </summary>
    public sealed partial class FrontendView
    {
        ResultSign resultSign;
        CardPickEntrance podiumEntrance;
        RewardStageSettings podiumEntranceSettings;
        SimWorld ceremonyWorld;
        int ceremonyEntry = int.MinValue, ceremonyGrind = -1;
        bool ceremonyRumbled, ceremonyLanded;
        readonly bool[] ceremonyFighterLanded = new bool[2];
        Text resultsExitHint;
        CanvasGroup resultsExitAlpha;

        /// <summary>결과 발표가 켜진 결과 공간인지(그 전 설정·기록 재생은 기존 시상대 상승).</summary>
        bool ResultsCeremony(SimWorld w) => w.InResultsSpace && w.Config.Match.ResultsCeremonyTicks > 0 && motionRunner.ResultsMap != null;

        void BuildResultsHud()
        {
            if (resultsExitHint != null) return;
            resultsExitHint = Text(stage, "Results_exit_hint", new Rect(44, 1006, 420, 44), "[Esc] 나가기", Theme.textFont, 26, Theme.white, TextAnchor.MiddleLeft);
            ReadableOnDivider(resultsExitHint, 2);
            resultsExitAlpha = resultsExitHint.gameObject.AddComponent<CanvasGroup>();
            resultsExitAlpha.alpha = 0;
            resultsExitHint.gameObject.SetActive(false);
        }

        /// <summary>결과 공간 HUD: 발표 중에는 아무것도 띄우지 않고, 조작이 풀리면 한쪽 아래에 작은 "[Esc] 나가기"만 보인다(재대전 안내는 머리 위).</summary>
        void AnimateResultsHud(SimWorld w)
        {
            BuildResultsHud();
            float tick = w.State.Match.PhaseTick + motionRunner.RenderAlpha;
            float open = Out(Range(tick, w.FreeSpaceUnlockTick, w.FreeSpaceUnlockTick + 18));
            resultsExitHint.gameObject.SetActive(open > 0);
            resultsExitAlpha.alpha = open;
            resultsExitHint.color = PaletteAsset.WithAlpha(Theme.white, .8f);
            resultsExitHint.rectTransform.anchoredPosition = new Vector2(44 - (1 - open) * 30, -1006);
            setScore.gameObject.SetActive(false);
            pickHintAlpha.alpha = 0;
            resultReady.gameObject.SetActive(false);
            resultBack.gameObject.SetActive(false);
        }

        void HideResultsHud()
        {
            if (resultsExitHint != null) resultsExitHint.gameObject.SetActive(false);
        }

        void HideResultSign()
        {
            resultSign?.Hide();
            if (motionPresenter != null) motionPresenter.ClearMovingTerrainShadow();
        }

        void PresentResultsCeremony(SimWorld w, float tick)
        {
            var map = motionRunner.ResultsMap;
            var s = map.ResultsStage;
            int entry = w.State.Tick - w.State.Match.PhaseTick;
            if (ceremonyWorld != w || ceremonyEntry != entry)
            {
                ceremonyWorld = w; ceremonyEntry = entry; ceremonyGrind = -1;
                ceremonyRumbled = ceremonyLanded = false;
                ceremonyFighterLanded[0] = ceremonyFighterLanded[1] = false;
            }
            float build = Range(tick, 0, CardCollectionRules.RebuildTicks);
            bool lit = build >= 1;
            int winner = SimWorld.ResultsWinner(w.State.Match);
            int unlock = w.FreeSpaceUnlockTick;
            bool open = tick >= unlock;
            float fall = Mathf.Max(1, s.fighterFallTicks);
            float drop0 = winner == 0 ? s.winnerDropTick : s.loserDropTick;
            float drop1 = winner == 1 ? s.winnerDropTick : s.loserDropTick;
            points.Root.gameObject.SetActive(false);
            startCue.gameObject.SetActive(false);
            // 선수는 자기 낙하 틱 전에는 보이지 않는다(머리 위 표시·그림자도). 조작이 풀린 뒤에는 결과 공간 기본 동작(KO 재등장 등).
            float scale0 = open || tick >= drop0 ? 1 : 0, scale1 = open || tick >= drop1 ? 1 : 0;
            motionPresenter.PresentFrontendStage(build, true, Out(build), scale0, scale1, 0, open, lit ? 1f : 0f, false, false);
            motionPresenter.Background.SetRoundLightPulse(lit ? (tick - CardCollectionRules.RebuildTicks) / 60f : -1);
            PresentCeremonyPodium(map, tick, s);
            if (!open)
            {
                for (int k = 0; k < 2; k++)
                {
                    float drop = k == 0 ? drop0 : drop1;
                    if (tick < drop) continue;
                    float u = Range(tick, drop, drop + fall);
                    if (u < 1) motionPresenter.OffsetFighterVisual(k, s.fighterDropHeight * (1 - u * u));
                    else if (!ceremonyFighterLanded[k])
                    {
                        // 착지: 기존 착지 반응(찌그러짐·먼지·소리)과 작은 흔들림을 한 번.
                        ceremonyFighterLanded[k] = true;
                        var f = w.State.GetFighter(k);
                        motionPresenter.PresentDisplayEvent(new SimEvent(w.State.Tick, SimEventType.Land, k, f.Position, Vec2.Zero, 26f));
                        motionPresenter.CameraRig?.AddTrauma(k == winner ? .16f : .12f);
                    }
                }
                if (tick < Mathf.Max(drop0, drop1) + fall) motionPresenter.HideFighterShadows();
            }
            PresentCeremonySign(w);
        }

        /// <summary>빈 시상대: 바닥 재구성 동안은 땅속에 숨고, podiumRumbleTick부터 모놀리스와 같은 곡선으로 솟는다. 그림자도 같이 움직인다.</summary>
        void PresentCeremonyPodium(MapDefinition map, float tick, ResultsStageSettings s)
        {
            EnsurePodium(map);
            if (podiumBox < 0) return;
            var reward = map.Reward;
            if (podiumEntrance == null || podiumEntranceSettings != reward) { podiumEntrance = new CardPickEntrance(reward); podiumEntranceSettings = reward; }
            float t = (tick - s.podiumRumbleTick) / SimConfig.TickRate + podiumEntrance.RumbleAt(0);
            var pose = podiumEntrance.Monolith(t, 0, podiumHeight);
            // 재구성 동안 바닥 조각은 위에서 내려오지만 시상대는 처음부터 땅속에 있다가 솟는다(재구성 오프셋을 더하지 않는다).
            motionPresenter.PlaceTerrainBox(podiumBox, pose.jitterX, pose.offsetY);
            var b = map.Box(podiumBox);
            var center = new Vector2((b.minX + b.maxX) * .5f, b.minY);
            var left = new Vector2(b.minX, b.minY); var right = new Vector2(b.maxX, b.minY);
            if (!ceremonyRumbled && t >= podiumEntrance.RumbleAt(0) && t < podiumEntrance.RumbleAt(0) + .3f)
            {
                ceremonyRumbled = true;
                MonolithRiseBeats.Rumble(motionPresenter, center, reward);
            }
            for (int beat = ceremonyGrind + 1; beat < podiumEntrance.GrindBeats && t >= podiumEntrance.GrindAt(0, beat); beat++)
            {
                ceremonyGrind = beat;
                if (t - podiumEntrance.GrindAt(0, beat) >= .3f) continue;
                MonolithRiseBeats.Grind(motionPresenter, center, left, right, beat, 1 - beat / (float)podiumEntrance.GrindBeats * .6f, reward);
            }
            if (!ceremonyLanded && t >= podiumEntrance.LandAt(0))
            {
                ceremonyLanded = true;
                if (t < podiumEntrance.LandAt(0) + .3f) MonolithRiseBeats.Land(motionPresenter, center, left, right, reward);
            }
        }

        void EnsurePodium(MapDefinition map)
        {
            if (map == podiumMap) return;
            podiumMap = map; podiumBox = -1; podiumHeight = 0;
            if (map == null || string.IsNullOrEmpty(map.PodiumBoxId)) return;
            for (int i = 0; i < map.BoxCount; i++)
                if (map.Box(i).id == map.PodiumBoxId) { podiumBox = i; var b = map.Box(i); podiumHeight = b.maxY - b.minY; break; }
        }

        /// <summary>
        /// 결과 점수: 결과 맵의 숫자 발판(모양 있는 물리 몸)의 실제 위치·회전에 깎은 조각을 그린다(점수와 맞는 숫자만 남아 있고,
        /// 낙하·'툭'·흔들림은 물리 로프가 만든다). 숫자 발판이 없는 결과 맵은 점수를 매달지 않는다.
        /// </summary>
        void PresentCeremonySign(SimWorld w)
        {
            resultSign ??= new ResultSign(motionPresenter.transform, motionPresenter.Shapes, motionPresenter.Palette);
            resultSign.BuildBodies(w.Arena);
            if (!resultSign.HasBodies) { resultSign.Hide(); return; }
            resultSign.PresentBodies(motionPresenter.Props, w.State.Props);
        }
    }
}
