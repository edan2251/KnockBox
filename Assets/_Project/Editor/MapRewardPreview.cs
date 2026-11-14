using System.Collections.Generic;
using System.Linq;
using Knockbox.Game.Cards;
using Knockbox.Game.Maps;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>
    /// MapAuthoring에서 보상 맵을 편집할 때 비석 상자 위에 실제 모놀리스 모습(샘플 카드, 꺼짐·점등·실루엣, 원근, P2 반전)을 겹쳐 보여 준다.
    /// 미리보기 오브젝트는 숨김·저장 안 함이며 Play·스크립트 다시 읽기·씬 닫기 때 지운다. 광원 위치와 패자 자리는 씬 뷰에 선으로 그린다.
    /// 모놀리스 프리팹에 편집 미리보기 색·샘플 카드를 칠하는 메뉴도 둔다(실행 때는 다시 칠한다).
    /// </summary>
    [InitializeOnLoad]
    public static class MapRewardPreview
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/CardPick/Monolith.prefab";
        public const string CatalogPath = "Assets/_Project/Data/Cards/CardFaceCatalog.asset";
        public const string ShapesPath = "Assets/_Project/Art/ShapeLibrary.asset";
        const float HalfBody = .36f, BodySize = .72f;

        sealed class State
        {
            public readonly List<MonolithView> views = new List<MonolithView>();
            public int signature;
        }

        static readonly Dictionary<MapAuthoring, State> states = new Dictionary<MapAuthoring, State>();

        static MapRewardPreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += ClearAll;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) ClearAll(); };
            EditorSceneManager.sceneClosing += (scene, removing) => ClearAll();
            EditorApplication.quitting += ClearAll;
            SceneView.duringSceneGui += DrawMarkers;
        }

        public static bool Wants(MapAuthoring a) => a != null && a.isActiveAndEnabled && a.rewardPreview && a.definition != null
            && a.definition.IsRewardMap && a.LoadedReward && a.LoadedId == a.definition.Id && a.rewardRoot != null && a.shapes != null;

        /// <summary>지금 그리는 미리보기 모놀리스 수(검사용).</summary>
        public static int Count(MapAuthoring a) => a != null && states.TryGetValue(a, out var s) ? s.views.Count : 0;
        public static MonolithView View(MapAuthoring a, int index) => states[a].views[index];

        static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (states.Count > 0) ClearAll();
                return;
            }
            foreach (var a in states.Keys.ToArray())
                if (!Wants(a)) Clear(a);
            foreach (var a in Object.FindObjectsByType<MapAuthoring>(FindObjectsSortMode.None))
                if (Wants(a)) Refresh(a, false);
        }

        /// <summary>비석·패자 자리·장면 값·미리보기 설정이 바뀌었으면 미리보기를 다시 그린다.</summary>
        public static void Refresh(MapAuthoring a, bool force)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MonolithView>(PrefabPath);
            if (prefab == null) return;
            var handles = a.RewardHandles().Select(h => (handle: h, box: h.Read())).OrderBy(x => x.box.minX + x.box.maxX).ToArray();
            var settings = a.definition.Reward;
            int signature = Signature(a, handles.Select(x => x.box), settings);
            if (!states.TryGetValue(a, out var state)) states[a] = state = new State();
            if (!force && state.signature == signature && state.views.Count == handles.Length && state.views.All(v => v != null)) return;
            state.signature = signature;
            Clear(state);
            a.shapes.Activate();
            var theme = FrontendTheme.Load();
            var faces = SampleFaces();
            bool mirror = a.rewardPreviewMirror;
            int picker = mirror ? 1 : 0;
            float tallest = handles.Length == 0 ? 0 : handles.Max(x => x.box.maxY - x.box.minY);
            float shortest = handles.Length == 0 ? 0 : handles.Min(x => x.box.maxY - x.box.minY);
            for (int i = 0; i < handles.Length; i++)
            {
                var view = Object.Instantiate(prefab);
                view.name = "Reward_preview_" + i;
                foreach (var t in view.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                SceneVisibilityManager.instance.DisablePicking(view.gameObject, true);
                state.views.Add(view);
                var box = handles[i].box;
                var local = CardPickLayout.LocalOutline(RewardStele.From(box), out var origin);
                view.transform.position = CardPickLayout.Place(origin, picker);
                view.Build(a.shapes, theme);
                view.SetOutline(mirror ? CardPickLayout.Mirror(local) : local);
                // 화면 왼쪽부터 선택지 0, 1, 2. 좌우 반전이면 순서도 뒤집힌다.
                int option = mirror ? handles.Length - 1 - i : i;
                view.Bind(faces.Length > 0 ? faces[option % faces.Length] : new CardFace { title = "카드", description = "" }, a.shapes);
                int hovered = a.rewardPreviewHover;
                CardPickLayout.HoverTargets(option, hovered, -1, hovered >= 0, out float lit, out float silhouette);
                view.SetDepth(CardPickStage.DepthFor(box.maxY - box.minY, tallest, shortest, settings.depthDarken));
                view.Present(lit, silhouette, false);
            }
            SceneView.RepaintAll();
        }

        static int Signature(MapAuthoring a, IEnumerable<MapBox> boxes, RewardStageSettings settings)
        {
            var hash = new System.HashCode();
            foreach (var b in boxes)
            {
                hash.Add(b.minX); hash.Add(b.minY); hash.Add(b.maxX); hash.Add(b.maxY);
                if (b.vertices != null) foreach (var v in b.vertices) hash.Add(v);
            }
            hash.Add(a.rewardPreviewHover); hash.Add(a.rewardPreviewMirror); hash.Add(settings.depthDarken);
            return hash.ToHashCode();
        }

        static CardFace[] SampleFaces()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CardFaceCatalog>(CatalogPath);
            return catalog != null ? catalog.Faces.ToArray() : new CardFace[0];
        }

        static void Clear(State state)
        {
            foreach (var v in state.views) if (v != null) Object.DestroyImmediate(v.gameObject);
            state.views.Clear();
        }

        static void Clear(MapAuthoring a)
        {
            if (a is object && states.TryGetValue(a, out var state)) Clear(state);
            states.Remove(a);
        }

        public static void ClearAll()
        {
            foreach (var state in states.Values) Clear(state);
            states.Clear();
        }

        /// <summary>광원(패자 위 고정)과 패자 자리를 씬 뷰에 그린다. 실제 광원 위치 계산은 실행 때와 같다.</summary>
        static void DrawMarkers(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint) return;
            foreach (var a in states.Keys)
            {
                if (a == null || a.rewardStand == null || a.definition == null) continue;
                var settings = a.definition.Reward;
                bool mirror = a.rewardPreviewMirror;
                Vector2 foot = a.rewardStand.position;
                if (mirror) foot.x = -foot.x;
                var light = CardPickLayout.FixedLight(foot, mirror ? 1 : 0, settings.lightHeight, settings.lightOffsetX);
                var gold = new Color(1f, .82f, .3f, .95f);
                Handles.color = gold;
                Handles.DrawWireDisc(light, Vector3.forward, .7f);
                Handles.DrawDottedLine(foot + Vector2.up * BodySize, light, 4);
                Handles.Label(light + new Vector2(.9f, .3f), "광원 (패자 발에서 높이 " + settings.lightHeight + ")");
                var body = new Rect(foot.x - HalfBody, foot.y, BodySize, BodySize);
                Handles.DrawSolidRectangleWithOutline(body, new Color(gold.r, gold.g, gold.b, .25f), gold);
                Handles.Label(foot + new Vector2(-1.2f, -.4f), mirror ? "패자 자리 (P2 선택, 반전)" : "패자 자리 (P1 선택)");
            }
        }

        /// <summary>모놀리스(프리팹 포함)에 편집 미리보기 색과 샘플 카드를 칠한다. 실행 때는 실제 카드로 다시 칠하므로 화면 결과는 바뀌지 않는다.</summary>
        public static void ApplySample(MonolithView view, ShapeLibrary shapes, int sample = 1)
        {
            shapes.Activate();
            view.Build(shapes, FrontendTheme.Load());
            var faces = SampleFaces();
            view.Bind(faces.Length > 0 ? faces[sample % faces.Length] : new CardFace { title = "카드", description = "" }, shapes);
            view.Present(0, 0, false);
        }

        [MenuItem("CONTEXT/MonolithView/편집 미리보기 색 · 샘플 카드 칠하기")]
        static void ApplySampleMenu(MenuCommand command)
        {
            var view = (MonolithView)command.context;
            var shapes = AssetDatabase.LoadAssetAtPath<ShapeLibrary>(ShapesPath);
            if (view == null || shapes == null) return;
            Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "모놀리스 미리보기 칠하기");
            ApplySample(view, shapes);
            foreach (var c in view.GetComponentsInChildren<Component>(true)) if (c != null) EditorUtility.SetDirty(c);
            if (!EditorSceneManager.IsPreviewScene(view.gameObject.scene) && view.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        }
    }
}
