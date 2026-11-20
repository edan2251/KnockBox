using Knockbox.Presentation;
using UnityEngine;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 땅에서 솟는 돌(모놀리스 비석, 결과 공간 시상대)의 일회성 효과(2026-11-20 공용으로 분리). 시간표는 CardPickEntrance가 정하고,
    /// 여기서는 그 박자에 맞춰 먼지·돌 조각·소리·카메라 흔들림을 낸다. 세기는 맵의 보상 장면 값(RewardStageSettings)을 쓴다. 화면 전용.
    /// </summary>
    public static class MonolithRiseBeats
    {
        /// <summary>땅속 울림: 낮은 소리, 흔들림, 가운데서 피어오르는 먼지.</summary>
        public static void Rumble(MatchPresenter presenter, Vector2 baseCenter, Maps.RewardStageSettings settings)
        {
            presenter.Dust.Burst(baseCenter, 5, 1.8f, 0);
            presenter.SfxPlayer?.Play(Sfx.HitWeak, .4f, .4f);
            presenter.CameraRig?.AddTrauma(settings.grindShake * 2.5f);
        }

        /// <summary>땅을 가르며 솟는 박자 하나: 양옆으로 튀는 먼지, 위로 튀는 돌 부스러기, 갈리는 낮은 소리, 흔들림. fade는 뒤로 갈수록 약해지는 비율.</summary>
        public static void Grind(MatchPresenter presenter, Vector2 baseCenter, Vector2 left, Vector2 right, int beat, float fade, Maps.RewardStageSettings settings)
        {
            presenter.Dust.Burst(left, 2, 2.6f * fade, -1);
            presenter.Dust.Burst(right, 2, 2.6f * fade, 1);
            presenter.Fx.Shards(baseCenter + Vector2.up * .15f, Vector2.up, 110f, 2, PaletteAsset.MonolithDefaults.Rim, 7f * fade, .13f, .4f);
            presenter.SfxPlayer?.Play(Sfx.HitWeak, .3f * fade, .34f + .05f * (beat % 3));
            presenter.CameraRig?.AddTrauma(settings.grindShake * fade);
        }

        /// <summary>쾅 멈춤: 큰 먼지, 돌 조각, 강한 흔들림과 아래로 꺼지는 반동, 묵직한 소리.</summary>
        public static void Land(MatchPresenter presenter, Vector2 baseCenter, Vector2 left, Vector2 right, Maps.RewardStageSettings settings)
        {
            presenter.Dust.Burst(left, 7, 5.4f, -1);
            presenter.Dust.Burst(right, 7, 5.4f, 1);
            presenter.Fx.Shards(baseCenter + Vector2.up * .2f, Vector2.up, 120f, 9, PaletteAsset.MonolithDefaults.Rim, 11f, .17f, .6f);
            var rig = presenter.CameraRig;
            if (rig != null) { rig.AddTrauma(settings.impactShake); rig.Kick(Vector2.down, .14f, .012f, .2f); }
            var sfx = presenter.SfxPlayer;
            if (sfx != null) { sfx.Play(Sfx.HitStrong, .7f, .5f); sfx.Play(Sfx.Land, .9f, .62f); }
        }
    }
}
