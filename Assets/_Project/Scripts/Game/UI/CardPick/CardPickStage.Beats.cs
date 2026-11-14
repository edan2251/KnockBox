using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.UI
{
    /// <summary>카드 선택 무대의 일회성 효과(먼지·소리·흔들림·돌 조각·파이터 반응). 확정 월드 시간표의 시각을 지나는 순간 한 번만 낸다.</summary>
    public sealed partial class CardPickStage
    {
        bool Due(int bit, float at)
        {
            if ((beats & (1 << bit)) != 0 || stageTime < at) return false;
            beats |= 1 << bit;
            // 늦게 들어왔으면(온라인 확정 지연, 중간 진입) 지난 효과는 건너뛴다.
            return stageTime - at < .3f;
        }

        Vector2 World(Vector3 local) => monolithRoot.parent != null ? (Vector2)monolithRoot.parent.TransformPoint(local) : (Vector2)local;

        /// <summary>진입 연출의 일회성 효과: 착지 찌그러짐·먼지·흔들림·소리·돌 조각. 확정 시간표를 지나는 순간 한 번만 낸다.</summary>
        void PlayEntranceBeats()
        {
            var settings = Settings;
            var rig = presenter.CameraRig;
            var sfx = presenter.SfxPlayer;
            if (Due(0, entrance.LoserLandAt))
            {
                presenter.Fighter(shownPicker).OnEvent(new SimEvent { Type = SimEventType.Land, Strength = 30 });
                var foot = World(Foot);
                presenter.Dust.Burst(foot, 5, 3.4f, -1);
                presenter.Dust.Burst(foot, 5, 3.4f, 1);
                if (sfx != null) sfx.Play(Sfx.Land, .85f, .9f);
                if (rig != null) rig.AddTrauma(.14f);
            }
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                int k = riseOrder[i];
                var baseCenter = World(basePositions[i]);
                if (Due(2 + k, entrance.RumbleAt(k)))
                {
                    // 땅속 울림: 낮은 소리, 흔들림, 가운데서 피어오르는 먼지.
                    presenter.Dust.Burst(baseCenter, 5, 1.8f, 0);
                    if (sfx != null) sfx.Play(Sfx.HitWeak, .4f, .4f);
                    if (rig != null) rig.AddTrauma(settings.grindShake * 2.5f);
                }
                // 땅을 가르며 솟는 동안 박자마다: 양옆으로 튀는 먼지, 위로 튀는 돌 부스러기, 갈리는 낮은 소리, 계속되는 흔들림.
                for (int beat = grindFired[i] + 1; beat < entrance.GrindBeats && stageTime >= entrance.GrindAt(k, beat); beat++)
                {
                    grindFired[i] = beat;
                    if (stageTime - entrance.GrindAt(k, beat) >= .3f) continue;
                    float fade = 1 - beat / (float)entrance.GrindBeats * .6f;
                    BaseDust(i, 2, 2.6f * fade);
                    presenter.Fx.Shards(baseCenter + Vector2.up * .15f, Vector2.up, 110f, 2, PaletteAsset.MonolithDefaults.Rim, 7f * fade, .13f, .4f);
                    if (sfx != null) sfx.Play(Sfx.HitWeak, .3f * fade, .34f + .05f * (beat % 3));
                    if (rig != null) rig.AddTrauma(settings.grindShake * fade);
                }
                if (Due(5 + k, entrance.LandAt(k)))
                {
                    // 쾅 멈춤: 큰 먼지, 돌 조각, 강한 흔들림과 아래로 꺼지는 반동, 묵직한 소리.
                    BaseDust(i, 7, 5.4f);
                    presenter.Fx.Shards(baseCenter + Vector2.up * .2f, Vector2.up, 120f, 9, PaletteAsset.MonolithDefaults.Rim, 11f, .17f, .6f);
                    if (rig != null) { rig.AddTrauma(settings.impactShake); rig.Kick(Vector2.down, .14f, .012f, .2f); }
                    if (sfx != null) { sfx.Play(Sfx.HitStrong, .7f, .5f); sfx.Play(Sfx.Land, .9f, .62f); }
                }
            }
            // 조명이 팍 켜지는 순간: 경기 GO처럼 밝은 신호음(선택 중 표시도 이때 튀어나온다).
            if (Due(11, entrance.LightAt(usedCount)) && sfx != null) sfx.Play(Sfx.Countdown, .7f, 1.5f);
            PlayConfirmBeats();
        }

        /// <summary>확정 연출의 일회성 효과: 도약, 내리찍기(충격파·흔들림·먼지·돌 조각·묵직한 소리), 흡수 끝(번쩍임).</summary>
        void PlayConfirmBeats()
        {
            if (!ConfirmActive) return;
            var rig = presenter.CameraRig;
            var sfx = presenter.SfxPlayer;
            var fighter = presenter.Fighter(shownPicker);
            int c = confirmChosen;
            var rim = PaletteAsset.MonolithDefaults.Rim;
            if (Due(19, pickStart))
            {
                // 클릭 반응: 비석이 쿵 하고 깨어난다(묵직한 흔들림·짧은 확대·밑동 먼지·낮은 소리).
                BaseDust(c, 5, 3.2f);
                if (rig != null) { rig.AddTrauma(.28f); rig.Punch(.014f, .1f); }
                if (sfx != null) { sfx.Play(Sfx.HitStrong, .55f, .6f); sfx.Play(Sfx.Land, .6f, .7f); }
            }
            if (Due(20, pickStart + confirm.SecondSurgeTime))
            {
                // 두 번째 박동: 한 번 더 울린다.
                BaseDust(c, 3, 2.2f);
                if (rig != null) rig.AddTrauma(.18f);
                if (sfx != null) sfx.Play(Sfx.HitWeak, .5f, .55f);
            }
            if (Due(21, pickStart + confirm.FilledTime))
            {
                // 꽉 참: 밝게 번쩍이며 아래로 눌리는 반동과 맑은 소리.
                if (rig != null) { rig.AddTrauma(.2f); rig.Kick(Vector2.down, .08f, .012f, .16f); }
                if (sfx != null) sfx.Play(Sfx.Countdown, .4f, 1.35f);
            }
            if (Due(12, pickStart + confirm.LaunchTime))
            {
                // 돌진 발사: 경기와 같은 발사 사건(먼지·바람 소리·흔들림·몸 늘어남)에, 발밑 땅 금과 큰 먼지를 더한다.
                TryGetFighter(shownPicker, out var launch);
                var top = basePositions[c] + Vector3.up * monoliths[c].Height;
                var direction = ((Vector2)top - Foot).normalized;
                presenter.PresentDisplayEvent(FighterActionPose.LungeStart(shownPicker, launch, direction, 30));
                var foot = World(Foot);
                presenter.Fx.Crack(foot, Vector2.up);
                presenter.Fx.Shards(foot + Vector2.up * .1f, Vector2.up, 120f, 8, rim, 9f, .14f, .45f);
                presenter.Dust.Burst(foot, 8, 5.5f, -1);
                presenter.Dust.Burst(foot, 8, 5.5f, 1);
                if (rig != null) rig.Kick(Vector2.up, .1f, .01f, .16f);
                if (sfx != null) sfx.Play(Sfx.Jump, .8f, .8f);
            }
            if (Due(17, pickStart + confirm.TopLandTime))
            {
                // 곡선 도약 끝: 비석 꼭대기에 한 번 내려앉는다(착지 찌그러짐·꼭대기 먼지·착지 소리·작은 흔들림).
                fighter.OnEvent(new SimEvent { Type = SimEventType.Land, Strength = 22 });
                var topWorld = World(basePositions[c] + Vector3.up * monoliths[c].Height);
                presenter.Dust.Burst(topWorld, 4, 2.6f, -1);
                presenter.Dust.Burst(topWorld, 4, 2.6f, 1);
                if (rig != null) rig.AddTrauma(.18f);
                if (sfx != null) sfx.Play(Sfx.Land, .75f, .85f);
            }
            if (Due(18, pickStart + confirm.HopTime))
            {
                // 제자리 도약: 꼭대기에서 위로 튀어 오른다(경기 점프 늘어남·꼭대기 먼지·점프 소리).
                fighter.OnEvent(new SimEvent { Type = SimEventType.Jump });
                var topWorld = World(basePositions[c] + Vector3.up * monoliths[c].Height);
                presenter.Dust.Burst(topWorld, 5, 3.4f, -1);
                presenter.Dust.Burst(topWorld, 5, 3.4f, 1);
                if (sfx != null) sfx.Play(Sfx.Jump, .85f, .95f);
            }
            if (Due(13, pickStart + confirm.ImpactTime))
            {
                // 쾅: 비석 꼭대기에 꽂힌다(히트스톱 동안 멈춤). 발밑 고리와 튀는 돌 조각, 강한 흔들림.
                fighter.OnEvent(new SimEvent { Type = SimEventType.Land, Strength = 30 });
                var topWorld = World(basePositions[c] + Vector3.up * monoliths[c].Height);
                var accent = presenter.Palette.FighterBody(shownPicker);
                presenter.Fx.RingBurst(topWorld, accent, .3f, 3f, .16f);
                presenter.Fx.Shards(topWorld, Vector2.up, 160f, 12, rim, 12f, .17f, .55f);
                if (rig != null) { rig.AddTrauma(.6f); rig.Punch(.03f, .12f); }
                if (sfx != null) { sfx.Play(Sfx.HitStrong, 1f, .55f); sfx.Play(Sfx.WallSplat, .7f, .65f); }
            }
            if (Due(15, pickStart + confirm.FloorTime))
            {
                // 슉 끝: 비석이 바닥 끝까지 박히고 패자가 바닥에 착지한다. 바닥 금, 바닥을 따라 퍼지는 충격파, 큰 먼지, 묵직한 흔들림.
                fighter.OnEvent(new SimEvent { Type = SimEventType.Land, Strength = 40 });
                float halfWidth = monoliths[c].Width * .5f;
                var baseWorld = World(basePositions[c]);
                var leftBase = World(basePositions[c] + Vector3.left * halfWidth);
                var rightBase = World(basePositions[c] + Vector3.right * halfWidth);
                presenter.Fx.Crack(baseWorld, Vector2.up);
                presenter.Fx.Crack(leftBase, Vector2.up);
                presenter.Fx.Crack(rightBase, Vector2.up);
                presenter.Fx.Shards(leftBase + Vector2.up * .1f, Vector2.left, 16f, 10, rim, 17f, .15f, .5f);
                presenter.Fx.Shards(rightBase + Vector2.up * .1f, Vector2.right, 16f, 10, rim, 17f, .15f, .5f);
                BaseDust(c, 12, 8f);
                // 충격파가 다른 비석 밑까지 닿는다.
                for (int i = 0; i < monoliths.Length; i++) if (i != c && options[i] >= 0) BaseDust(i, 4, 2.8f);
                if (rig != null) { rig.AddTrauma(.8f); rig.Kick(Vector2.down, .24f, .025f, .26f); rig.Punch(.025f, .14f); }
                if (sfx != null) { sfx.Play(Sfx.Land, 1f, .45f); sfx.Play(Sfx.HitStrong, .8f, .42f); }
            }
            if (Due(14, pickStart + confirm.AbsorbedTime))
            {
                // 흡수 끝: 패자가 번쩍이며 기운을 받아 늘어났다 돌아온다.
                fighter.Flash(.15f);
                fighter.FlashOutline(.3f);
                fighter.OnEvent(new SimEvent { Type = SimEventType.PadBounce });
                if (sfx != null) sfx.Play(Sfx.Countdown, .45f, 1.8f);
            }
        }

        /// <summary>비석 밑 양 끝에서 바깥쪽으로 먼지를 낸다.</summary>
        void BaseDust(int index, int count, float spread)
        {
            float half = monoliths[index].Width * .5f;
            presenter.Dust.Burst(World(basePositions[index] + Vector3.left * half), count, spread, -1);
            presenter.Dust.Burst(World(basePositions[index] + Vector3.right * half), count, spread, 1);
        }
    }
}
