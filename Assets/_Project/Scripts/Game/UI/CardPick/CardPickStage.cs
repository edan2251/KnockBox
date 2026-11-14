using System;
using System.Collections.Generic;
using Knockbox.Game.Cards;
using Knockbox.Game.Online;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 세트 패자의 카드 선택을 게임 안 장면으로 보여 준다(선택 맵, 비석 모놀리스, 호버 점등·어두워짐, 패자 위 고정 광원과 그림자, 대기 캐릭터, 선택 중 표시).
    /// 상태는 확정 월드(runner.FrontendWorld)에서만 읽고, 선택은 FrontendController → FrontendInputSource.Choose 기존 경로로 보낸다.
    /// 호버·점등·시선은 화면에서만 바뀌며 시뮬레이션 상태·설정·입력 형식·기록에 들어가지 않는다. 광원은 호버해도 움직이지 않는다.
    /// 선택 맵은 미등록 보상 맵 정의다. 바닥·비석·패자 자리·장면 값(광원·원근·점등·대기 캐릭터·진입 연출)은 모두 그 맵 파일에 있고 MapAuthoring에서 고친다.
    /// 진입 연출은 CardPickEntrance, 확정 연출은 CardPickConfirm이 시간표를 계산하고, 일회성 효과는 CardPickStage.Beats, 확정 무대 쪽은 CardPickStage.Confirm에 있다.
    /// 모놀리스는 처음 쓸 때 프리팹에서 비석 수만큼 만든다.
    /// </summary>
    public sealed partial class CardPickStage : MonoBehaviour
    {
        [SerializeField, InspectorName("모놀리스 선택 사용"), Tooltip("끄면 기존 카드 선택 UI를 그대로 사용합니다.")]
        bool useMonoliths = true;
        [SerializeField] MatchRunner runner;
        [SerializeField] MatchPresenter presenter;
        [SerializeField] FrontendController controller;
        [SerializeField, Tooltip("보상 맵(바닥·비석·패자 자리·장면 값). MapAuthoring에서 편집하고 GameMaps에 등록하지 않습니다.")] Maps.MapDefinition stageMap;
        [SerializeField] CardFaceCatalog catalog;
        [SerializeField, Tooltip("비석마다 하나씩 만들 모놀리스 프리팹(부품·색·카드 면 배치)")] MonolithView monolithPrefab;
        [SerializeField, Tooltip("만든 모놀리스의 부모")] Transform monolithRoot;
        [SerializeField, Tooltip("누가 고르는 중인지 보여 주는 표시")] CardPickBanner banner;

        MonolithView[] monoliths = Array.Empty<MonolithView>();
        float[] placedX, lit, silhouette;
        int[] options;
        ConvexPolygon[] shadowPolygons;
        Vector2[][] mapOutlines;
        Vector3[] mapPositions;
        Vector2 standFoot;
        float steleCenterX;
        // 진입 연출(솟아오름). 시간은 확정 월드의 PhaseTick에서 매 프레임 다시 계산하고, 일회성 효과는 비트로 한 번만 낸다.
        CardPickEntrance entrance;
        int[] riseOrder;
        Vector3[] basePositions;
        MonolithPose[] poses;
        float stageTime = -1;
        // 선택 확정 뒤 다음 라운드 직전의 퇴장 진행도(0~1). 확정 월드의 PhaseTick·PickedAt에서 계산한다.
        float exitProgress;
        /// <summary>퇴장 시간(초): 확정 대기(PickHoldTicks)의 마지막 이만큼 동안 전투 맵 퇴장처럼 지형·배경이 빠진다.</summary>
        public const float ExitSeconds = .35f;
        // 확정 연출(4단계): 점프·내리찍기·박힘·흡수. 확정 월드의 PickedAt에서 경과초를 계산한다.
        CardPickConfirm confirm;
        int confirmChosen = -1;
        float confirmSince = -1, pickStart;
        SpriteRenderer[] absorbDots;
        bool ConfirmActive => confirm != null && confirmChosen >= 0 && confirmSince >= 0;
        public CardPickConfirm Confirm => confirm;
        public int ConfirmedMonolith => ConfirmActive ? confirmChosen : -1;
        int beats, usedCount;
        bool posesMoving;
        SpriteRenderer floorCover;
        // 솟는 동안 갈리는 박자를 몇 번째까지 냈는지(비석 칸별). 일회성 효과를 한 번만 내기 위한 표시다.
        int[] grindFired;
        // 진입이 끝난 첫 프레임에 어두운 상태로 바로 맞추기 위한 표시(시간이 건너뛰어도 밝았다 어두워지지 않게).
        bool settledShown;
        // 호버가 바뀐 비석(바닥 먼지 반응용).
        int feltHover = -1;
        ProjectedShadowMesh shadows;
        Transform shadowRoot;
        ArenaDef arena;
        ArenaMeta meta;
        FighterState standing;
        Vector2 fixedLight;
        bool prepared, broken, active, everHovered;
        int shownRound = -1, shownPicker = -1, pointerHover = -1, selectedHover = -1, frozenHover = -1, remoteHover = -1;
        OnlineMatch online;
        CardHoverRelay relayOverride;

        public bool UseMonoliths { get => useMonoliths; set => useMonoliths = value; }
        /// <summary>FrontendController가 기존 카드 화면 대신 이 무대를 쓸지 정할 때 읽는다. 설정이 빠졌으면 기존 UI로 돌아간다.</summary>
        public bool UsesMonoliths => useMonoliths && !broken && isActiveAndEnabled && runner != null && runner.FrontendEnabled
            && stageMap != null && catalog != null && monolithPrefab != null && monolithRoot != null;
        public bool Active => active;
        public int Picker => shownPicker;
        public ArenaDef Arena => arena;
        public CardFaceCatalog Catalog => catalog;
        public CardPickBanner Banner => banner;
        public int MonolithCount => monoliths.Length;
        public MonolithView Monolith(int index) => monoliths[index];
        /// <summary>모놀리스 칸 → 선택지 번호(-1이면 쓰지 않음).</summary>
        public int OptionOf(int index) => options != null && index < options.Length ? options[index] : -1;
        /// <summary>지금 호버로 켜진 모놀리스 칸(-1 없음). 클릭 뒤에는 고른 칸에 고정된다.</summary>
        public int Hovered => LocalHovered >= 0 ? LocalHovered : remoteHover;
        int LocalHovered => frozenHover >= 0 ? frozenHover : pointerHover >= 0 ? pointerHover : selectedHover;
        /// <summary>상대가 고를 때 받은 호버(모놀리스 칸, -1 없음).</summary>
        public int RemoteHovered => remoteHover;
        /// <summary>
        /// 호버 전송 경로. 온라인(Fusion) 경기면 OnlineMatch의 전송, 아니면 없음(로컬 연습은 같은 화면이라 필요 없다).
        /// 대입하면 그 경로를 쓴다(검사에서 네트워크 없이 끼워 넣을 때).
        /// </summary>
        public CardHoverRelay HoverRelay { get => relayOverride ?? (online != null ? online.CardHover : null); set => relayOverride = value; }
        public float Lit(int index) => lit[index];
        public float Silhouette(int index) => silhouette[index];
        /// <summary>이 선택 단계의 고정 광원(배경 좌표). 선택 맵 배경과 그림자를 이 위치로 굽는다.</summary>
        public Vector2 LightPosition => fixedLight;
        /// <summary>보상 장면 값. 보상 맵 파일에 저장되어 있다.</summary>
        public Maps.RewardStageSettings Settings => stageMap != null ? stageMap.Reward : null;
        public int ShadowCount => shadows != null ? shadows.Count : 0;
        /// <summary>진입 연출이 끝나 호버·클릭을 받는지.</summary>
        public bool EntranceSettled => active && entrance != null && stageTime >= entrance.SettledAt(usedCount);
        public MonolithPose Pose(int index) => poses[index];
        public CardPickEntrance Entrance => entrance;

        /// <summary>
        /// 원근 보정: 높이로 앞뒤를 정한다. 가장 높은 비석은 그대로, 가장 낮은 비석은 depthDarken만큼, 그 사이는 높이 비율로 어둡게 한다.
        /// </summary>
        public static float DepthFor(float height, float tallest, float shortest, float darken) =>
            tallest - shortest < 1e-4f ? 0 : darken * Mathf.Clamp01((tallest - height) / (tallest - shortest));

        void ApplyDepth()
        {
            float tallest = float.MinValue, shortest = float.MaxValue;
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                tallest = Mathf.Max(tallest, monoliths[i].Height);
                shortest = Mathf.Min(shortest, monoliths[i].Height);
            }
            for (int i = 0; i < monoliths.Length; i++)
                if (options[i] >= 0) monoliths[i].SetDepth(DepthFor(monoliths[i].Height, tallest, shortest, Settings.depthDarken));
        }

        void Awake()
        {
            runner ??= GetComponentInParent<MatchRunner>();
            presenter ??= GetComponentInParent<MatchPresenter>();
            controller ??= GetComponentInParent<FrontendController>();
            online = GetComponentInParent<OnlineMatch>();
            if (monolithRoot != null) monolithRoot.gameObject.SetActive(false);
            if (banner != null) banner.Hide();
        }

        void OnDisable()
        {
            if (active) Leave();
        }

        void OnDestroy() => shadows?.Dispose();

        bool Prepare()
        {
            if (prepared) return !broken;
            prepared = true;
            int count = CardCollectionRules.CardCount;
            try
            {
                if (!stageMap.IsRewardMap) throw new InvalidOperationException(stageMap.name + "은 보상 맵이 아닙니다(MapAuthoring의 보상 맵 설정).");
                // ToArena가 보상 맵 규칙(비석 3개·볼록·바닥 위·겹침 없음)까지 검증한다. 화면에 그릴 지형은 바닥 상자뿐이다.
                arena = stageMap.ToArena();
                meta = stageMap.Metadata();
                if (arena.PropCount != 0) throw new InvalidOperationException("선택 맵에는 물리 상자를 두지 않습니다.");
                if (stageMap.SteleCount < count) throw new InvalidOperationException($"비석이 {stageMap.SteleCount}개인데 {count}개가 필요합니다.");
                mapOutlines = new Vector2[count][];
                mapPositions = new Vector3[count];
                float center = 0;
                for (int i = 0; i < count; i++)
                {
                    mapOutlines[i] = CardPickLayout.LocalOutline(stageMap.Stele(i), out var origin);
                    mapPositions[i] = origin;
                    center += origin.x / count;
                }
                standFoot = stageMap.LoserStand;
                steleCenterX = center;
            }
            catch (Exception e)
            {
                broken = true;
                Debug.LogError("[Knockbox] 카드 선택 맵을 읽지 못해 기존 카드 UI를 사용합니다: " + e.Message);
                return false;
            }
            var theme = FrontendTheme.Load();
            monoliths = new MonolithView[count];
            placedX = new float[count];
            lit = new float[count];
            silhouette = new float[count];
            options = new int[count];
            shadowPolygons = new ConvexPolygon[count];
            riseOrder = new int[count];
            basePositions = new Vector3[count];
            poses = new MonolithPose[count];
            grindFired = new int[count];
            // 솟아오르는 동안 비석의 땅속 부분을 가리는 바닥색 판. 바닥(지형)보다 위, 비석 부품보다 위에 그린다.
            floorCover = ShapeLibrary.Create(transform, "Monolith_floor_cover", presenter.Shapes.Square, SortingLayers.Terrain, 20, PaletteAsset.MonolithDefaults.Body);
            floorCover.gameObject.SetActive(false);
            // 확정 때 카드 조각이 패자에게 빨려 드는 점(KO 뒤 점이 점수판으로 가는 문법과 같은 둥근 점).
            absorbDots = new SpriteRenderer[CardPickConfirm.DotCount];
            for (int k = 0; k < absorbDots.Length; k++)
            {
                absorbDots[k] = ShapeLibrary.Create(transform, "Absorb_dot_" + k, presenter.Shapes.Circle, SortingLayers.Fx, 20, PaletteAsset.MonolithDefaults.IconFill);
                absorbDots[k].gameObject.SetActive(false);
            }
            for (int i = 0; i < count; i++)
            {
                // 비석마다 프리팹에서 하나씩 만든다. 모양·자리는 Enter에서 보상 맵 비석으로 맞춘다.
                var view = Instantiate(monolithPrefab, monolithRoot, false);
                view.name = "Monolith_" + (i + 1);
                monoliths[i] = view;
                view.Build(presenter.Shapes, theme);
                int index = i;
                view.Clicked += () => Click(index);
                var hover = view.Hover;
                if (hover != null)
                {
                    hover.Hover = point => OnHover(index, point.HasValue);
                    hover.Leave = () => OnLeave(index);
                }
            }
            if (banner != null) banner.Build(presenter.Shapes, theme);
            // 비석 그림자는 기존 경기와 같은 셰이더·재질·진하기(맵 shadow 0.65)로, 같은 고정 광원에서 드리운다(겹쳐도 진해지지 않음).
            shadowRoot = new GameObject("Monolith_shadows").transform;
            shadowRoot.SetParent(transform, false);
            shadows = new ProjectedShadowMesh(shadowRoot.gameObject, count, presenter.Background.ShadowMaterial, 0);
            return true;
        }

        /// <summary>MatchPresenter가 라운드 전환 처리 직후, 파이터를 그리기 전에 매 프레임 부른다. 이번 프레임에 무대가 화면을 맡으면 true.</summary>
        public bool BeginFrame()
        {
            if (!Wants(out var m))
            {
                if (active) Leave();
                return false;
            }
            if (!active || m.Round != shownRound || m.Picker != shownPicker) Enter(m);
            stageTime = Mathf.Max(stageTime, EntranceClock(m));
            exitProgress = ExitClock(m);
            UpdateConfirm(m);
            // 패자 착지 찌그러짐은 파이터를 그리기 전에 넣어야 하므로 일회성 효과를 여기서 낸다.
            PlayEntranceBeats();
            ApplyPoses();
            // 호버 점등은 로컬 화면 반응이라 실제 시간으로 움직인다(시뮬레이션 틱과 무관).
            UpdateHover(m, Mathf.Min(Time.unscaledDeltaTime, .1f));
            return true;
        }

        bool Wants(out MatchState match)
        {
            match = default;
            if (!UsesMonoliths || runner.World == null) return false;
            var confirmed = runner.FrontendWorld;
            match = confirmed.State.Match;
            // 클라이언트의 예측 월드가 이미 다음 라운드로 넘어갔으면 경기장 재구성에 화면을 넘긴다.
            return confirmed.Config.Match.CollectCardsOnly && match.Phase == MatchPhase.SetPick && match.Picker >= 0
                && runner.World.State.Match.Round == match.Round && Prepare();
        }

        void Enter(in MatchState m)
        {
            var confirmed = runner.FrontendWorld;
            // 패자는 보상 맵의 패자 자리에 서서 비석 쪽을 본다. 표시 전용 상태이며 시뮬레이션에 쓰지 않는다.
            var stand = SimState.CreateRound(confirmed.Config, arena, 0u, MatchPhase.Countdown);
            standing = stand.Fighter0;
            int picker = m.Picker;
            // P1 배치로 저장된 패자 자리. P2가 고르면 좌우를 뒤집은 자리에 선다(맵의 시작 위치는 쓰지 않는다).
            float standX = picker == 1 ? -standFoot.x : standFoot.x;
            standing.Position = new Vec2(standX, standFoot.y + confirmed.Config.HalfBody);
            standing.Facing = (sbyte)CardPickLayout.FacingToward(standFoot.x, steleCenterX, picker);
            var settings = Settings;
            // 고정 광원은 패자 바로 위. 선택 맵 배경(빛 감쇠)과 그림자를 이 위치로 굽는다. 호버해도 움직이지 않는다.
            fixedLight = CardPickLayout.FixedLight(Foot, picker, settings.lightHeight, settings.lightOffsetX);
            var stageMeta = meta;
            stageMeta.HasLight = true;
            stageMeta.Light = fixedLight;
            presenter.BuildStageArena(arena, stageMeta);
            presenter.Fighter(0).ResetPose();
            presenter.Fighter(1).ResetPose();
            for (int i = 0; i < monoliths.Length; i++)
            {
                var t = monoliths[i].transform;
                // 비석 모양과 자리는 보상 맵 비석 그대로(P2면 좌우 반전).
                monoliths[i].SetOutline(picker == 1 ? CardPickLayout.Mirror(mapOutlines[i]) : mapOutlines[i]);
                t.localPosition = CardPickLayout.Place(mapPositions[i], picker);
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                basePositions[i] = t.localPosition;
                placedX[i] = t.localPosition.x;
            }
            CardPickLayout.AssignOptions(placedX, m.OptionCount, options);
            for (int i = 0; i < monoliths.Length; i++)
            {
                bool used = options[i] >= 0;
                monoliths[i].gameObject.SetActive(used);
                // 처음부터 어두운 상태로 둔다(진입 중에는 UpdateHover가 빈 비석은 밝게 덮어쓴다). 늦게 들어와도 밝았다 어두워지지 않는다.
                lit[i] = 0;
                silhouette[i] = 1;
                if (!used) continue;
                monoliths[i].Bind(Face(m.GetOption(options[i]), confirmed.Config), presenter.Shapes);
                monoliths[i].Canvas.worldCamera = Camera.main;
            }
            ApplyDepth();
            LinkNavigation();
            // 진입 연출: 패자에게 가까운 비석부터 솟는다.
            entrance = new CardPickEntrance(settings);
            usedCount = 0;
            for (int i = 0; i < monoliths.Length; i++)
            {
                grindFired[i] = -1;
                riseOrder[i] = 0;
                if (options[i] < 0) continue;
                usedCount++;
                // 큰(높은) 비석부터 솟는다. 높이가 같으면 패자에게 가까운 것부터.
                for (int j = 0; j < monoliths.Length; j++)
                    if (j != i && options[j] >= 0 && CardPickLayout.RisesBefore(monoliths[j].Height, Mathf.Abs(placedX[j] - standX), j,
                        monoliths[i].Height, Mathf.Abs(placedX[i] - standX), i)) riseOrder[i]++;
            }
            feltHover = -1;
            settledShown = false;
            beats = 0;
            stageTime = -1;
            posesMoving = true;
            floorCover.color = presenter.CurrentTheme.terrain;
            stageTime = EntranceClock(m);
            exitProgress = ExitClock(m);
            confirm = null;
            UpdateConfirm(m);
            ApplyPoses();
            monolithRoot.gameObject.SetActive(true);
            BuildShadowShapes();
            pointerHover = selectedHover = frozenHover = remoteHover = -1;
            // 처음부터 카드 내용은 어둡게(호버하지 않은 비석과 같은 상태) 두고, 호버한 비석만 밝힌다.
            everHovered = true;
            var events = EventSystem.current;
            if (events != null) events.SetSelectedGameObject(null);
            active = true;
            shownRound = m.Round;
            shownPicker = picker;
        }

        void Leave()
        {
            active = false;
            if (monolithRoot != null) monolithRoot.gameObject.SetActive(false);
            if (floorCover != null) floorCover.gameObject.SetActive(false);
            if (absorbDots != null) foreach (var dot in absorbDots) dot.gameObject.SetActive(false);
            confirmSince = -1;
            confirmChosen = -1;
            if (banner != null) { banner.Hide(); banner.transform.localScale = Vector3.one; }
            stageTime = -1;
            if (shadows != null) { shadows.Begin(); shadows.Commit(); }
            if (presenter != null && shownPicker >= 0)
            {
                var view = presenter.Fighter(shownPicker);
                view.LookTarget = null;
                view.StageLean = 0;
                view.StageBreath = 0;
            }
            var events = EventSystem.current;
            if (events != null && IsMonolith(events.currentSelectedGameObject)) events.SetSelectedGameObject(null);
            shownRound = shownPicker = -1;
            pointerHover = selectedHover = frozenHover = remoteHover = -1;
            HoverRelay?.StopPublishing();
            // 같은 라운드에서 끝나면(토글 끔·메뉴 복귀) 경기 맵을 바로 되돌린다. 라운드가 바뀌었으면 이미 다시 그렸다.
            if (presenter != null && runner != null && runner.World != null) presenter.RestoreMatchArena();
        }

        void LinkNavigation()
        {
            // 키보드·패드 좌우 이동이 화면 순서(선택지 0 → 1 → 2)를 따르게 한다.
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                Selectable left = null, right = null;
                for (int j = 0; j < monoliths.Length; j++)
                {
                    if (options[j] == options[i] - 1) left = monoliths[j].Hit;
                    if (options[j] == options[i] + 1) right = monoliths[j].Hit;
                }
                monoliths[i].Hit.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit, selectOnLeft = left, selectOnRight = right,
                };
            }
        }

        void BuildShadowShapes()
        {
            for (int i = 0; i < monoliths.Length; i++)
            {
                shadowPolygons[i] = null;
                if (options[i] < 0) continue;
                // 땅속에 있는 부분은 그림자를 드리우지 않는다(바닥 높이에서 자른다).
                var t = monoliths[i].transform;
                float floorLocal = (basePositions[i].y - t.localPosition.y) / Mathf.Max(.01f, t.localScale.y);
                var local = MonolithView.ClipY(monoliths[i].Outline, floorLocal, 1e4f);
                if (local.Length < 3) continue;
                var points = new Vec2[local.Length];
                for (int k = 0; k < local.Length; k++)
                {
                    var world = monoliths[i].transform.TransformPoint(local[k]);
                    var p = shadowRoot.InverseTransformPoint(world);
                    points[k] = new Vec2(p.x, p.y);
                }
                try { shadowPolygons[i] = new ConvexPolygon(points); }
                catch (ArgumentException e) { Debug.LogWarning($"[Knockbox] {monoliths[i].name} 그림자 윤곽이 볼록 다각형이 아닙니다: {e.Message}"); }
            }
        }

        bool LocalPicking(in MatchState m) => m.PickedAt < 0 && m.Picker == runner.LocalPlayerSlot && !runner.MenuInputBlocked
            && controller != null && !controller.CardSubmitted;

        void OnHover(int index, bool pointer)
        {
            if (!active || options[index] < 0 || !EntranceSettled || !LocalPicking(runner.FrontendWorld.State.Match)) return;
            if (pointer)
            {
                pointerHover = index;
                // 마우스로 옮긴 자리에서 키보드 이동을 이어 갈 수 있게 선택도 함께 옮긴다.
                var events = EventSystem.current;
                if (events != null && events.currentSelectedGameObject != monoliths[index].Hit.gameObject)
                    events.SetSelectedGameObject(monoliths[index].Hit.gameObject);
            }
            selectedHover = index;
            everHovered = true;
        }

        void OnLeave(int index)
        {
            if (pointerHover == index)
            {
                pointerHover = -1;
                var events = EventSystem.current;
                // 선택 해제 콜백 안(다른 선택으로 옮기는 중)에서는 다시 선택을 바꾸지 않는다.
                if (events != null && !events.alreadySelecting && events.currentSelectedGameObject == monoliths[index].Hit.gameObject)
                    events.SetSelectedGameObject(null);
            }
            if (selectedHover == index) selectedHover = -1;
        }

        bool IsMonolith(GameObject go)
        {
            if (go == null || monoliths == null) return false;
            foreach (var view in monoliths) if (view != null && view.Hit != null && view.Hit.gameObject == go) return true;
            return false;
        }

        void UpdateHover(in MatchState m, float dt)
        {
            var settings = Settings;
            bool picking = LocalPicking(m) && EntranceSettled;
            if (picking)
            {
                // 아무것도 고르지 않은 채 좌우 키를 누르면 그 방향의 끝 모놀리스부터 고른다. 이후 이동은 UI 탐색이 맡는다.
                var keyboard = Keyboard.current;
                var events = EventSystem.current;
                if (keyboard != null && events != null && !IsMonolith(events.currentSelectedGameObject))
                {
                    bool right = keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame;
                    bool left = keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame;
                    if (right || left)
                    {
                        int option = right ? 0 : m.OptionCount - 1;
                        for (int i = 0; i < monoliths.Length; i++)
                            if (options[i] == option) events.SetSelectedGameObject(monoliths[i].Hit.gameObject);
                    }
                }
            }
            else if (frozenHover < 0) pointerHover = selectedHover = -1;
            ExchangeHover(m);
            int chosen = m.PickedAt >= 0 ? ChosenInstance(runner.FrontendWorld.State.GetFighter(m.Picker).Augments, m) : -1;
            int hovered = Hovered;
            // 무거운 비석은 바닥이 고정이다. 호버가 옮겨 가면 그 비석 밑에서 돌가루만 살짝 떨어진다.
            if (hovered != feltHover)
            {
                if (hovered >= 0 && chosen < 0) BaseDust(hovered, 2, 1.3f);
                feltHover = hovered;
            }
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                if (!EntranceSettled && chosen < 0)
                {
                    // 진입 중: 플레이어 색이 덮고 있다가 걷힌 자리부터 어두운 카드 내용이 보인다(밑은 처음부터 어두운 상태).
                    lit[i] = 0;
                    silhouette[i] = 1;
                    continue;
                }
                CardPickLayout.HoverTargets(i, hovered, chosen, everHovered, out float litTarget, out float silhouetteTarget);
                if (!settledShown) silhouette[i] = silhouetteTarget;
                lit[i] = Mathf.MoveTowards(lit[i], litTarget, dt / Mathf.Max(.05f, litTarget > lit[i] ? settings.litRiseSeconds : settings.litFallSeconds));
                silhouette[i] = Mathf.MoveTowards(silhouette[i], silhouetteTarget, dt / Mathf.Max(.05f, settings.silhouetteSeconds));
            }
            if (EntranceSettled || chosen >= 0) settledShown = true;
            int focus = chosen >= 0 ? chosen : hovered;
            // 대기 중 패자: 숨쉬기, 호버 쪽 시선과 살짝 기울기.
            var fighter = presenter.Fighter(shownPicker);
            fighter.StageBreath = settings.breath;
            if (focus >= 0)
            {
                var face = (Vector2)monoliths[focus].transform.TransformPoint(new Vector3(0, monoliths[focus].FaceCenterY, 0));
                fighter.LookTarget = face;
                fighter.StageLean = -Mathf.Sign(face.x - fighter.transform.position.x) * settings.leanDegrees;
            }
            else
            {
                fighter.LookTarget = null;
                fighter.StageLean = 0;
            }
            // 확정 연출 중에는 기울이지 않는다(점프·내리찍기 자세를 그대로 보인다).
            if (ConfirmActive) fighter.StageLean = 0;
        }

        /// <summary>
        /// 승자도 같은 화면을 보도록 호버를 주고받는다. 내가 고르면 호버한 선택지 번호를 보내고, 상대가 고르면 받은 값을 표시한다.
        /// 선택지 번호로 주고받아 좌우 반전과 무관하다. 확정 뒤에는 보내지도 쓰지도 않는다(확정 표시는 확정 월드를 따른다).
        /// </summary>
        void ExchangeHover(in MatchState m)
        {
            remoteHover = -1;
            var relay = HoverRelay;
            if (relay == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (m.Picker == runner.LocalPlayerSlot && m.PickedAt < 0)
            {
                int local = LocalHovered;
                relay.Publish(now, m.Round, local >= 0 ? options[local] : CardHoverRelay.None);
                return;
            }
            relay.StopPublishing();
            if (m.PickedAt >= 0) return;
            int option = relay.RemoteOption(now, m.Round);
            for (int i = 0; i < monoliths.Length; i++)
                if (option >= 0 && options[i] == option) remoteHover = i;
            if (remoteHover >= 0) everHovered = true;
        }

        /// <summary>선택 단계 시작부터의 경과초. 확정 월드의 PhaseTick에 프레임 보간만 더한다(누적 타이머 없음).</summary>
        /// <summary>퇴장 진행도: 확정 뒤 대기(PickHoldTicks)의 마지막 ExitSeconds 동안 0 → 1.</summary>
        float ExitClock(in MatchState m)
        {
            if (m.PickedAt < 0) return 0;
            int hold = runner.FrontendWorld.Config.Match.PickHoldTicks;
            float exitTicks = Mathf.Min(ExitSeconds * SimConfig.TickRate, hold * .5f);
            float since = m.PhaseTick - m.PickedAt + Alpha;
            return FrontendMotion.Range(since, hold - exitTicks, hold);
        }

        /// <summary>틱 사이 화면 보간 비율(0~1).</summary>
        float Alpha => Mathf.Clamp01(runner.RenderAlpha);
        /// <summary>퇴장 중 지형·비석·패자가 아래로 빠진 거리(전투 맵 지형 퇴장과 같은 가속).</summary>
        float ExitDrop => exitProgress * exitProgress * (arena.Rows + 3);

        float EntranceClock(in MatchState m) => (m.PhaseTick + Alpha) / SimConfig.TickRate;

        /// <summary>진입 상태를 비석에 적용한다: 세로 이동·떨림(크기는 그대로), 플레이어 색 기운과 카드 내용 드러남.</summary>
        void ApplyPoses()
        {
            bool anyBelow = false;
            var energyColor = presenter.Palette.FighterBody(shownPicker);
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                var pose = entrance.Monolith(stageTime, riseOrder[i], monoliths[i].Height);
                // 확정: 패자가 내리찍은 비석은 오버슈트하며 땅에 박힌다(크기 변화 없음).
                if (ConfirmActive && i == confirmChosen) pose.offsetY += confirm.Sink(confirmSince);
                poses[i] = pose;
                anyBelow |= pose.offsetY < -.001f;
                var t = monoliths[i].transform;
                // 퇴장: 전투 맵 지형처럼 가속하며 아래로 빠진다.
                float away = ExitDrop;
                t.localPosition = basePositions[i] + new Vector3(pose.jitterX, pose.offsetY - away, 0);
                t.localScale = Vector3.one;
                if (ConfirmActive)
                {
                    if (i == confirmChosen)
                    {
                        // 고른 비석(클릭 반응): 플레이어 색이 두 번에 걸쳐 차오르고(덮인 자리의 카드 내용은 가려짐), 꽉 찬 순간 밝게 번쩍이며, 그동안 비석이 통째로 살짝 떤다.
                        float fill = confirm.Cover(confirmSince);
                        var surgeColor = Color.Lerp(energyColor, PaletteAsset.MonolithDefaults.EnergyHighlight, .65f * confirm.FillFlash(confirmSince));
                        monoliths[i].SetEntrance(fill, surgeColor, fill > 0 ? fill * monoliths[i].Height : float.NegativeInfinity);
                        t.localPosition += Vector3.right * confirm.Tremble(confirmSince);
                    }
                    // 나머지 비석: 어두운 상태 그대로(글자·아이콘은 흐리게 보인다).
                    else monoliths[i].SetEntrance(0, energyColor, float.NegativeInfinity);
                }
                else monoliths[i].SetEntrance(pose.energy, energyColor, pose.contentFront);
            }
            // 바닥 아래를 가리는 판은 비석이 땅속에 있는 동안, 바닥이 자리 잡은 뒤에만 켠다.
            bool cover = anyBelow && entrance.FloorProgress(stageTime) >= 1;
            if (floorCover.gameObject.activeSelf != cover) floorCover.gameObject.SetActive(cover);
            if (cover)
            {
                float floorY = basePositions[0].y;
                for (int i = 0; i < monoliths.Length; i++) if (options[i] >= 0) floorY = basePositions[i].y;
                floorCover.transform.localPosition = new Vector3(0, floorY - 4, 0);
                ShapeLibrary.SetSize(floorCover, 80, 8);
            }
        }

        CardFace Face(int option, SimConfig config)
        {
            int id = PickOption.IsAugment(option) ? PickOption.AugmentOf(option) : -1;
            if (catalog.TryGet(id, out var face)) return face;
            // 표시 목록에 없는 카드도 이름만으로 그린다(새 카드를 추가하는 도중의 안전장치).
            return new CardFace { cardId = id, title = Augments.TryGet(config, id, out var def) ? def.Name : "카드", description = "" };
        }

        Vector2 Foot => new Vector2(standing.Position.X, standing.Position.Y - runner.FrontendWorld.Config.HalfBody);

        public bool TryGetFighter(int index, out FighterState state)
        {
            state = standing;
            if (active && entrance != null && entrance.Loser(stageTime, out float height, out float velocityY) && height > 0)
            {
                // 위에서 떨어지는 중: 공중 자세, 낙하 속도만큼 세로로 늘어난다(파이터 그림의 기존 늘어남).
                state.Position = new Vec2(state.Position.X, state.Position.Y + height);
                state.Velocity = new Vec2(0, Mathf.Max(velocityY, -40f));
                state.Grounded = false;
            }
            if (active && ConfirmActive)
            {
                // 확정: 경기의 좌클릭 충전 → 돌진 모습 그대로 고른 비석 위로 뛰어 내리찍고, 박히는 비석 위에 선다(표시 전용 상태).
                ConfirmFighter(out var foot, out var velocity, out float charge, out bool airborne, out bool lunging);
                var config = runner.FrontendWorld.Config;
                state.Position = new Vec2(foot.x, foot.y + config.HalfBody);
                var toward = (sbyte)(basePositions[confirmChosen].x >= Foot.x ? 1 : -1);
                if (charge >= 0)
                {
                    state.Facing = toward;
                    state = FighterActionPose.Charge(state, charge, config);
                }
                else if (lunging)
                {
                    // 새총 발사 직후: 경기 돌진 모습(잔상·팔 뻗기).
                    state.Facing = toward;
                    state = FighterActionPose.Lunge(state, Vector2.ClampMagnitude(velocity, 70));
                }
                else if (airborne)
                {
                    // 나머지 비행·점프: 평소 공중 자세, 속도만큼 늘어난다.
                    state.Facing = toward;
                    state.Velocity = new Vec2(Mathf.Clamp(velocity.x, -60, 60), Mathf.Clamp(velocity.y, -60, 60));
                    state.Grounded = false;
                }
                else
                {
                    state.Velocity = new Vec2(0, 0);
                    state.Grounded = true;
                }
            }
            return active && index == shownPicker;
        }

        /// <summary>FrontendView 화면 처리 뒤에 부른다. 선택 맵을 세워 두고, 모놀리스 상태·그림자·선택 중 표시를 그린다.</summary>
        public void Present()
        {
            if (!active) return;
            float lightLevel = entrance.Light(stageTime, usedCount, out float pulse);
            if (exitProgress > 0) lightLevel = Mathf.Min(lightLevel, CardPickEntrance.ExitLight(exitProgress));
            // 내리찍는 순간 라운드 광원 펄스를 다시 쓴다.
            if (ConfirmActive && confirmSince >= confirm.ImpactTime) pulse = confirmSince - confirm.ImpactTime;
            presenter.PresentCardPickStage(shownPicker, entrance.FloorProgress(stageTime), entrance.Loser(stageTime, out _, out _), lightLevel, pulse, exitProgress);
            if (exitProgress > 0)
            {
                // 패자는 퇴장하며 작아져 사라진다(전투 KO 뒤 퇴장과 같은 감속 이징).
                float size = Mathf.Max(.0001f, 1 - FrontendMotion.Out(FrontendMotion.Range(exitProgress, 0, .6f)));
                presenter.Fighter(shownPicker).transform.localScale = Vector3.one * size;
            }
            var confirmed = runner.FrontendWorld;
            var m = confirmed.State.Match;
            int chosen = m.PickedAt >= 0 ? ChosenInstance(confirmed.State.GetFighter(m.Picker).Augments, m) : -1;
            bool canChoose = LocalPicking(m) && EntranceSettled && exitProgress <= 0;
            var accent = presenter.Palette.FighterBody(shownPicker);
            // 진입 중에만 그림자 윤곽을 다시 만든다. 멈춘 뒤 한 번 더 만들어 마지막 자세에 맞춘다.
            bool moving = stageTime < entrance.FinishedAt(usedCount) || exitProgress > 0 || ConfirmActive;
            if (moving || posesMoving) BuildShadowShapes();
            posesMoving = moving;
            // 그림자는 배경과 같은 광원 위치(타격 광원 흔들림 포함)에서 비석 셋 모두 드리운다.
            shadows.Begin();
            var lightWorld = presenter.Background.transform.TransformPoint(presenter.Background.LightPosition);
            var lightLocal = (Vector2)shadowRoot.InverseTransformPoint(lightWorld);
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                monoliths[i].SetInteractable(canChoose);
                // 점등은 MonolithView 안에서 감속 이징으로, 어두워짐은 여기서 부드러운 이징으로 바꿔 넘긴다(선형 진행값을 그대로 쓰지 않음).
                if (ConfirmActive && i == confirmChosen)
                {
                    // 고른 비석: 호버 빛이 빠지며 플레이어 색이 아래에서 위로 덮고, 덮인 채 순식간에 땅속으로 박혀 사라진다.
                    monoliths[i].Present(confirm.ChosenLit(confirmSince), 0, false);
                }
                else monoliths[i].Present(lit[i], FrontendMotion.Smooth(silhouette[i]), chosen == i);
                if (shadowPolygons[i] != null) shadows.Polygon(shadowPolygons[i], lightLocal, 200);
            }
            shadows.Commit();
            var bannerScale = entrance.Banner(stageTime, usedCount);
            // 퇴장이 시작되면 선택 중 표시는 빠르게 작아져 사라진다(가속 이징).
            float exitShrink = exitProgress > 0 ? 1 - FrontendMotion.InQuad(FrontendMotion.Range(exitProgress, 0, .5f)) : 1;
            var bannerFoot = Foot;
            if (ConfirmActive)
            {
                const float ShrinkEnd = CardPickConfirm.ChargeStart + .1f;
                // 확정: 충전을 시작하며 표시가 빠르게 작아지고, 바닥에 착지하는 순간 "획득"으로 다시 튀어나온다(오버슈트). 표시는 패자를 따라간다.
                float b = confirm.Base(confirmSince);
                float pop = b < ShrinkEnd ? 1 - FrontendMotion.InQuad(b / ShrinkEnd)
                    : b < confirm.SinkEnd ? 0 : FrontendMotion.OutBack(FrontendMotion.Range(b, confirm.SinkEnd, confirm.SinkEnd + .3f), 2.2f);
                bannerScale = new Vector2(pop, pop);
                ConfirmFighter(out var foot, out _, out _, out _, out _);
                bannerFoot = new Vector2(foot.x, Mathf.Min(foot.y, 5.2f));
                UpdateAbsorbDots(foot, accent);
            }
            else if (absorbDots != null) foreach (var dot in absorbDots) if (dot.gameObject.activeSelf) dot.gameObject.SetActive(false);
            bannerScale *= exitShrink;
            if (banner != null && bannerScale.y <= 0) banner.Hide();
            else if (banner != null)
            {
                banner.transform.localScale = new Vector3(bannerScale.x, bannerScale.y, 1);
                string text = chosen >= 0 ? monoliths[chosen].Title.text + " 획득" : "P" + (shownPicker + 1) + " 선택 중";
                // 남은 시간은 확정 월드 PhaseTick에서 계산한다(시간 제한 자체는 시뮬레이션 설정 그대로).
                float remaining = Mathf.Max(0, (confirmed.Config.Match.PickTimeoutTicks - m.PhaseTick - Alpha) / SimConfig.TickRate);
                int seconds = chosen >= 0 ? -1 : Mathf.Max(0, Mathf.CeilToInt(remaining));
                banner.Show(shownPicker, bannerFoot, accent, text, seconds, remaining, stageTime);
            }
        }

        int ChosenInstance(in AugmentState owned, in MatchState m)
        {
            if (owned.Count == 0) return -1;
            int id = owned.Get(owned.Count - 1);
            for (int i = 0; i < monoliths.Length; i++)
            {
                if (options[i] < 0) continue;
                int option = m.GetOption(options[i]);
                if (PickOption.IsAugment(option) && PickOption.AugmentOf(option) == id) return i;
            }
            return -1;
        }

        void Click(int index)
        {
            if (!active || controller == null || options[index] < 0) return;
            if (!EntranceSettled || !LocalPicking(runner.FrontendWorld.State.Match)) return;
            // 누르면 비석이 땅을 한 번 짓누르듯: 바닥 먼지와 짧은 흔들림, 낮은 소리(비석은 움직이지 않는다).
            BaseDust(index, 4, 3f);
            if (presenter.CameraRig != null) presenter.CameraRig.AddTrauma(.14f);
            if (presenter.SfxPlayer != null) presenter.SfxPlayer.Play(Sfx.Land, .55f, .7f);
            frozenHover = index;
            everHovered = true;
            controller.ChooseCard(options[index]);
        }
    }
}
