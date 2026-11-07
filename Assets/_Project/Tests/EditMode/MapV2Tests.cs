using System.Collections.Generic;
using System.IO;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// 맵 v2(04-arena.md 6절). CP13(CR-002): Rounds 측정값 기준의 새 맵 6개, 구조물 선언, 경계 튕김.
    /// CP12에서 이어지는 것: 짝 파일, 라운드마다 맵 교체, 순환 경기 기록.
    /// </summary>
    public class MapV2Tests
    {
        public static readonly string[] NewMaps = { "Chimes", "Gantry", "Mobile", "Ruins", "Scales", "Totems" };
        const int MatchTicks = 7200;
        /// <summary>1단계 구조물이 흔들린다고 보는 최대 각도(원칙 9의 설계 검사, S-117). CP14에서 실제 진폭으로 다시 확인.</summary>
        const float SwingDegrees = 30f;

        static SimConfig Config() =>
            new SimConfig(MovementParams.Default, MatchParams.Default, CombatParams.Default, CombatMode.Charge, true);

        static ArenaMeta Meta(string name) =>
            ArenaMeta.Parse(File.ReadAllText(Path.Combine(ArenaFiles.Folder, name + ".json")), name + ".json");

        static ArenaDef[] LoadAll()
        {
            var arenas = new ArenaDef[NewMaps.Length];
            for (int i = 0; i < NewMaps.Length; i++)
                arenas[i] = ArenaFiles.LoadByName(NewMaps[i]);
            return arenas;
        }

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 1e-4f;

        // CP13-1 ------------------------------------------------------------------

        /// <summary>파서 검증(크기·테두리·시작 위치·문자·좌우 대칭)을 통과하고, 지형·구조물·시작 위치가 좌우로 짝을 이룬다.</summary>
        [TestCaseSource(nameof(NewMaps))]
        public void NewMap_ParsesAsMirroredBoundaryMap(string name)
        {
            var arena = ArenaFiles.LoadByName(name);
            Assert.AreEqual(36, arena.Columns);
            Assert.AreEqual(20, arena.Rows);
            Assert.IsTrue(arena.HasBoundary, "테두리 벽 대신 경계 튕김");
            Assert.Greater(arena.PropCount, 0);
            Assert.AreEqual(-arena.SpawnFoot0.X, arena.SpawnFoot1.X, 1e-5f);
            Assert.AreEqual(arena.SpawnFoot0.Y, arena.SpawnFoot1.Y, 1e-5f);
            for (int i = 0; i < arena.BoxCount; i++)
            {
                var b = arena.GetBox(i);
                Assert.Less(System.Math.Abs(b.MinX), arena.BoundaryHalfWidth + 1e-4f, $"{name}: 경계 밖 지형 {b}");
                bool mirrored = false;
                for (int j = 0; j < arena.BoxCount && !mirrored; j++)
                {
                    var m = arena.GetBox(j);
                    mirrored = m.Kind == b.Kind && Near(m.MinX, -b.MaxX) && Near(m.MaxX, -b.MinX) && Near(m.MinY, b.MinY) && Near(m.MaxY, b.MaxY);
                }
                Assert.IsTrue(mirrored, $"{name}: 좌우 짝이 없는 상자 {b}");
            }
            for (int i = 0; i < arena.PropCount; i++)
            {
                var p = arena.GetProp(i);
                bool mirrored = false;
                for (int j = 0; j < arena.PropCount && !mirrored; j++)
                {
                    var m = arena.GetProp(j);
                    mirrored = m.Kind == p.Kind && Near(m.Center.X, -p.Center.X) && Near(m.Center.Y, p.Center.Y)
                               && Near(m.HalfSize.X, p.HalfSize.X) && Near(m.HalfSize.Y, p.HalfSize.Y) && m.HasAnchor == p.HasAnchor
                               && (!p.HasAnchor || (Near(m.Anchor.X, -p.Anchor.X) && Near(m.Anchor.Y, p.Anchor.Y)));
                }
                Assert.IsTrue(mirrored, $"{name}: 좌우 짝이 없는 구조물 {p}");
            }
        }

        struct Cluster
        {
            public float MinX, MinY, MaxX, MaxY, Area;
            public bool HasProp;
            public float CenterX => (MinX + MaxX) * 0.5f;
            public float CenterY => (MinY + MaxY) * 0.5f;
        }

        /// <summary>물체 하나: 고정 지형 상자(묶음 -1) 또는 구조물(처음 놓인 모양, 묶음 번호). CP14부터 일부 구조물은 상자가 아니라 강체다.</summary>
        struct Item
        {
            public float MinX, MinY, MaxX, MaxY;
            public int Group;
            public bool Prop;
        }

        static List<Item> Items(ArenaDef arena)
        {
            var items = new List<Item>();
            for (int i = 0; i < arena.PropBoxStart; i++)
            {
                var b = arena.GetBox(i);
                items.Add(new Item { MinX = b.MinX, MinY = b.MinY, MaxX = b.MaxX, MaxY = b.MaxY, Group = -1 });
            }
            for (int i = 0; i < arena.PropCount; i++)
            {
                var p = arena.GetProp(i);
                items.Add(new Item { MinX = p.MinX, MinY = p.MinY, MaxX = p.MaxX, MaxY = p.MaxY, Group = p.Group, Prop = true });
            }
            return items;
        }

        /// <summary>
        /// 묶음: 닿거나 겹친 물체(고정 지형 상자, 구조물)와 같은 묶음 번호의 구조물(기둥, 연결 구조)을 하나로 센다. 줄은 잇지 않는다.
        /// </summary>
        static List<Cluster> Clusters(ArenaDef arena)
        {
            var items = Items(arena);
            int n = items.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            }
            for (int i = 0; i < n; i++)
            {
                var a = items[i];
                for (int j = i + 1; j < n; j++)
                {
                    var b = items[j];
                    bool sameGroup = a.Prop && b.Prop && a.Group == b.Group;
                    bool touch = a.MinX <= b.MaxX + 1e-3f && b.MinX <= a.MaxX + 1e-3f && a.MinY <= b.MaxY + 1e-3f && b.MinY <= a.MaxY + 1e-3f;
                    if (sameGroup || touch)
                        parent[Find(i)] = Find(j);
                }
            }
            var map = new Dictionary<int, Cluster>();
            for (int i = 0; i < n; i++)
            {
                var b = items[i];
                int root = Find(i);
                float area = (b.MaxX - b.MinX) * (b.MaxY - b.MinY);
                if (map.TryGetValue(root, out var c))
                {
                    c.MinX = System.Math.Min(c.MinX, b.MinX);
                    c.MinY = System.Math.Min(c.MinY, b.MinY);
                    c.MaxX = System.Math.Max(c.MaxX, b.MaxX);
                    c.MaxY = System.Math.Max(c.MaxY, b.MaxY);
                    c.Area += area;
                    c.HasProp |= b.Prop;
                }
                else
                {
                    c = new Cluster { MinX = b.MinX, MinY = b.MinY, MaxX = b.MaxX, MaxY = b.MaxY, Area = area, HasProp = b.Prop };
                }
                map[root] = c;
            }
            return new List<Cluster>(map.Values);
        }

        /// <summary>
        /// CP13-1: 밝은 고정 지형 8% 이하(테두리 제외), 물체 면적 10~16%, 묶음 14~22개, 4층 이상에 고르게(높이 3유닛 띠 6개 중 4개 이상,
        /// 한 띠에 40% 이하, S-116), 구조물 묶음 60% 이상, 가운데 중심 구조물, 가시는 바닥 전체를 덮지 않음. 면적의 기준은 경계 안쪽(34 × 18).
        /// </summary>
        [TestCaseSource(nameof(NewMaps))]
        public void NewMap_MeetsRoundsMeasurements(string name)
        {
            var arena = ArenaFiles.LoadByName(name);
            float area = 4f * arena.BoundaryHalfWidth * arena.BoundaryHalfHeight;
            float terrain = 0f, props = 0f, spikeWidth = 0f;
            for (int i = 0; i < arena.PropBoxStart; i++)
            {
                var b = arena.GetBox(i);
                terrain += (b.MaxX - b.MinX) * (b.MaxY - b.MinY);
                if (b.Kind == TerrainKind.Spike)
                    spikeWidth += b.MaxX - b.MinX;
            }
            for (int i = 0; i < arena.PropCount; i++)
            {
                var p = arena.GetProp(i);
                props += 4f * p.HalfSize.X * p.HalfSize.Y;
            }
            var clusters = Clusters(arena);
            var bands = new int[6];
            int structures = 0;
            float center = 0f;
            foreach (var c in clusters)
            {
                bands[System.Math.Min(5, System.Math.Max(0, (int)System.Math.Floor((c.CenterY + arena.BoundaryHalfHeight) / 3f)))]++;
                if (c.HasProp)
                    structures++;
                if (c.HasProp && System.Math.Abs(c.CenterX) < 0.01f)
                    center = System.Math.Max(center, c.Area);
            }
            int layers = 0, fullest = 0;
            foreach (int b in bands)
            {
                if (b > 0)
                    layers++;
                fullest = System.Math.Max(fullest, b);
            }
            string report = $"[Knockbox][CP13-1] {name}: 고정 지형 {terrain / area:P1}, 물체 {(terrain + props) / area:P1}, 묶음 {clusters.Count}"
                            + $"(구조물 {structures}, {(float)structures / clusters.Count:P0}), 띠 [{string.Join(",", bands)}], 중심 구조물 {center:F1}, 가시 {spikeWidth}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.LessOrEqual(terrain / area, 0.08f, report);
            Assert.That((terrain + props) / area, Is.InRange(0.10f, 0.16f), report);
            Assert.That(clusters.Count, Is.InRange(14, 22), report);
            Assert.GreaterOrEqual(layers, 4, report);
            Assert.LessOrEqual(fullest, clusters.Count * 0.4f, report);
            Assert.GreaterOrEqual((float)structures / clusters.Count, 0.6f, report);
            Assert.Greater(center, 0f, "가운데 중심 구조물");
            Assert.Less(spikeWidth, arena.BoundaryHalfWidth, "가시가 바닥 전체를 덮지 않음");
        }

        /// <summary>
        /// 원칙 9: 1단계에서 움직이기 시작하는 블록(매달린 블록, 회전 블록)은 ±30°로 흔들리거나(매달린 블록) 한 바퀴 돌아도(회전 블록)
        /// 서로, 그리고 다른 구조물(아직 움직이지 않는 임시 물체)과 겹치지 않는다. 블록은 외접원으로 본다.
        /// </summary>
        [TestCaseSource(nameof(NewMaps))]
        public void StageOneMovers_HaveSwingClearance(string name)
        {
            var arena = ArenaFiles.LoadByName(name);
            var envelopes = new List<(int prop, List<(float x, float y, float r)> circles)>();
            for (int i = 0; i < arena.PropCount; i++)
            {
                var p = arena.GetProp(i);
                if (PropDef.Stage(p.Kind) != 1)
                    continue;
                float r = (float)System.Math.Sqrt(p.HalfSize.X * p.HalfSize.X + p.HalfSize.Y * p.HalfSize.Y);
                var circles = new List<(float, float, float)>();
                if (p.Kind == PropKind.Spinner)
                {
                    circles.Add((p.Anchor.X, p.Anchor.Y, r + (float)System.Math.Sqrt((p.Center - p.Anchor).LengthSquared)));
                }
                else
                {
                    var d = p.Center - p.Anchor;
                    for (float deg = -SwingDegrees; deg <= SwingDegrees + 1e-3f; deg += 5f)
                    {
                        double t = deg * System.Math.PI / 180.0;
                        float c = (float)System.Math.Cos(t), s = (float)System.Math.Sin(t);
                        circles.Add((p.Anchor.X + d.X * c - d.Y * s, p.Anchor.Y + d.X * s + d.Y * c, r));
                    }
                }
                envelopes.Add((i, circles));
            }
            Assert.Greater(envelopes.Count, 0, $"{name}: 1단계 구조물 없음");
            for (int a = 0; a < envelopes.Count; a++)
            {
                for (int b = a + 1; b < envelopes.Count; b++)
                {
                    foreach (var p in envelopes[a].circles)
                    foreach (var q in envelopes[b].circles)
                    {
                        float dx = p.x - q.x, dy = p.y - q.y;
                        Assert.GreaterOrEqual(dx * dx + dy * dy, (p.r + q.r) * (p.r + q.r),
                            $"{name}: {arena.GetProp(envelopes[a].prop)} ↔ {arena.GetProp(envelopes[b].prop)}");
                    }
                }
                for (int o = 0; o < arena.PropCount; o++)
                {
                    var other = arena.GetProp(o);
                    if (PropDef.Stage(other.Kind) == 1)
                        continue;
                    foreach (var p in envelopes[a].circles)
                    {
                        float nx = System.Math.Min(System.Math.Max(p.x, other.MinX), other.MaxX);
                        float ny = System.Math.Min(System.Math.Max(p.y, other.MinY), other.MaxY);
                        float dx = p.x - nx, dy = p.y - ny;
                        Assert.GreaterOrEqual(dx * dx + dy * dy, p.r * p.r, $"{name}: {arena.GetProp(envelopes[a].prop)} ↔ {other}");
                    }
                }
            }
        }

        /// <summary>맵 구성: 맵마다 구조물 2종 이상, 6개 전체에서 모든 종류, 쌓인 상자 기둥은 3개 이상의 맵, 테마당 2개, 모두 순환.</summary>
        [Test]
        public void AllMaps_CoverEveryKind_TwoPerTheme()
        {
            var perTheme = new int[3];
            var all = new HashSet<PropKind>();
            int pillarMaps = 0;
            foreach (string name in NewMaps)
            {
                var meta = Meta(name);
                Assert.IsTrue(meta.Rotation, name);
                Assert.IsTrue(meta.HasLight, name);
                perTheme[meta.Theme]++;
                var kinds = new HashSet<PropKind>();
                foreach (var p in meta.Props)
                    kinds.Add(p.Kind);
                Assert.GreaterOrEqual(kinds.Count, 2, $"{name}: 구조물 종류 {string.Join(",", kinds)}");
                all.UnionWith(kinds);
                if (kinds.Contains(PropKind.BoxPillar))
                    pillarMaps++;
                TestContext.WriteLine($"[Knockbox][CP13-1] {name}: 테마 {(char)('A' + meta.Theme)}, 구조물 {meta.Props.Length}개, 종류 {string.Join(", ", kinds)}");
            }
            CollectionAssert.AreEqual(new[] { 2, 2, 2 }, perTheme);
            Assert.AreEqual(PropDef.KindCount, all.Count, "모든 종류가 한 번 이상");
            Assert.GreaterOrEqual(pillarMaps, 3, "쌓인 상자 기둥은 3개 이상의 맵");
            foreach (string name in new[] { "Box", "Towers", "Pit" })
                Assert.IsFalse(Meta(name).Rotation, name);
        }

        [Test]
        public void PairFile_RejectsBadValues()
        {
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("{\"theme\": \"Z\"}", "bad"));
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("{\"light\": [1]}", "bad"));
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("{\"rotation\": 1}", "bad"));
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("[]", "bad"));
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("{\"props\": [{\"kind\": \"rock\", \"pos\": [0, 0], \"size\": [1, 1]}]}", "bad"));
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("{\"props\": [{\"kind\": \"hanging\", \"pos\": [0, 0], \"size\": [1, 1]}]}", "bad"));
            Assert.Throws<System.FormatException>(() => ArenaMeta.Parse("{\"props\": [{\"kind\": \"pillar\", \"base\": [0, 0], \"boxes\": [[1, 1]]}]}", "bad"));
        }

        /// <summary>짝 파일: 거울 짝, 기둥은 아래에서 위로 쌓임, 연결 구조는 한 묶음이고 앞 부품에 이어짐.</summary>
        [Test]
        public void PairFile_ExpandsMirrorPillarAndLinks()
        {
            var meta = ArenaMeta.Parse("{\"props\": ["
                                       + "{\"kind\": \"pillar\", \"base\": [-5, -2], \"boxes\": [[2, 1], [1, 2]], \"mirror\": true},"
                                       + "{\"kind\": \"linked\", \"parts\": [{\"pos\": [0, 6], \"size\": [2, 1], \"anchor\": [0, 10]}, {\"pos\": [0, 3], \"size\": [1, 1], \"anchorPart\": 0}]}"
                                       + "]}", "ok");
            Assert.AreEqual(6, meta.Props.Length);
            Assert.AreEqual(PropKind.BoxPillar, meta.Props[0].Kind);
            Assert.AreEqual(-1.5f, meta.Props[0].Center.Y, 1e-5f);
            Assert.AreEqual(0f, meta.Props[1].Center.Y, 1e-5f);
            Assert.AreEqual(5f, meta.Props[2].Center.X, 1e-5f, "거울 짝");
            Assert.AreNotEqual(meta.Props[0].Group, meta.Props[2].Group);
            Assert.AreEqual(meta.Props[4].Group, meta.Props[5].Group, "연결 구조는 한 묶음");
            Assert.AreEqual(4, meta.Props[5].AnchorProp);
            Assert.AreEqual(5.5f, meta.Props[5].Anchor.Y, 1e-5f, "앞 부품 아래 가운데에 이어짐");
        }

        /// <summary>기둥 꼭대기 상자 위에 설 수 있다(CP13~15는 임시 물체, CP16부터 강체 상자).</summary>
        [Test]
        public void PillarTop_IsStandable()
        {
            var config = Config();
            var arena = ArenaFiles.LoadByName("Totems");
            int pillarTop = -1;
            for (int i = 0; i < arena.PropCount; i++)
            {
                var p = arena.GetProp(i);
                if (p.Kind == PropKind.BoxPillar && p.Group == 0)
                    pillarTop = i;
            }
            var box = arena.GetProp(pillarTop);
            var world = TestWorlds.CreateAt(new Vec2(box.Center.X, box.MaxY + 2f), new Vec2(12f, 0f), config, arena);
            for (int t = 0; t < 90; t++)
                world.Step(default, default);
            var f = world.State.Fighter0;
            int body = -1;
            for (int b = 0; b < arena.DynamicPropCount; b++)
            {
                if (arena.DynamicProp(b) == pillarTop)
                    body = b;
            }
            // CP16부터 기둥 상자도 강체: 꼭대기 상자 위에 선다(구조물 위 접지). 깨어난 기둥은 조금(0.1 이하) 내려앉는다.
            Assert.GreaterOrEqual(body, 0, "기둥 상자는 강체(3단계)");
            Assert.IsTrue(f.Grounded, "기둥 꼭대기 상자 위에 선다");
            Assert.AreEqual(FighterState.OnPropBox, f.GroundBox);
            Assert.AreEqual(body + 1, f.OnProp);
            Assert.AreEqual(box.MaxY + config.HalfBody, f.Position.Y, 0.1f);
        }

        // CP12-2 (맵 교체, CP13에서 그대로 유지) ------------------------------------------

        static int[] Sequence(uint seed, int count, int rounds)
        {
            var r = ArenaRotation.Create(seed, count);
            var list = new int[rounds];
            for (int i = 0; i < rounds; i++)
            {
                list[i] = r.Current;
                r.Advance();
            }
            return list;
        }

        /// <summary>같은 시드면 같은 순서, 한 바퀴(6라운드) 안에 중복 없음, 바퀴가 바뀔 때도 같은 맵이 이어지지 않음.</summary>
        [Test]
        public void Rotation_SameSeedSameOrder_NoRepeatWithinSix()
        {
            var firsts = new HashSet<string>();
            for (uint seed = 0; seed < 300; seed++)
            {
                int[] a = Sequence(seed, 6, 36), b = Sequence(seed, 6, 36);
                CollectionAssert.AreEqual(a, b, $"시드 {seed}");
                for (int cycle = 0; cycle < 6; cycle++)
                {
                    var seen = new HashSet<int>();
                    for (int k = 0; k < 6; k++)
                        Assert.IsTrue(seen.Add(a[cycle * 6 + k]), $"시드 {seed} {cycle}바퀴 중복");
                }
                for (int i = 1; i < a.Length; i++)
                    Assert.AreNotEqual(a[i - 1], a[i], $"시드 {seed} {i}라운드 같은 맵 연속");
                firsts.Add($"{a[0]}{a[1]}{a[2]}{a[3]}{a[4]}{a[5]}");
            }
            Assert.Greater(firsts.Count, 100, "시드마다 순서가 달라야 함");
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, Sequence(5, 1, 3), "맵 하나면 고정");
        }

        /// <summary>순환 경기: 라운드마다 맵이 바뀌고, 7틱 되감기 SyncTest가 라운드 교체와 경계 튕김을 지나도 일치한다.</summary>
        [Test]
        public void RotationMatch_ChangesMapEachRound_SyncTestPasses()
        {
            var config = Config();
            var arenas = LoadAll();
            var world = new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 7, MatchPhase.Countdown));
            var sync = new SyncTest<SimWorld>(world);
            var p1 = new BotInputSource(() => world, 0, 419u);
            var p2 = new BotInputSource(() => world, 1, 917u);
            var played = new List<string> { world.Arena.Name };
            int round = world.State.Match.Round;
            for (int t = 0; t < MatchTicks * 2 && played.Count < 7; t++)
            {
                int tick = world.State.Tick;
                if (!sync.Advance(p1.Poll(tick), p2.Poll(tick)))
                    Assert.Fail(sync.LastMismatch.ToString());
                if (world.State.Match.Round != round)
                {
                    round = world.State.Match.Round;
                    Assert.AreNotEqual(played[played.Count - 1], world.Arena.Name, "라운드가 바뀌면 맵도 바뀜");
                    Assert.AreEqual(world.Arena.SpawnFoot0.X, world.State.Fighter0.Position.X, 1e-4f, "새 맵의 시작 위치");
                    played.Add(world.Arena.Name);
                }
            }
            TestContext.WriteLine($"[Knockbox][CP12-2] 시드 7 순서: {string.Join(" → ", played)}");
            Assert.GreaterOrEqual(played.Count, 3, "라운드가 충분히 진행되지 않음");
            var firstSix = new HashSet<string>();
            for (int i = 0; i < System.Math.Min(6, played.Count); i++)
                Assert.IsTrue(firstSix.Add(played[i]), $"6라운드 안에 중복: {string.Join(", ", played)}");
        }

        /// <summary>순환 경기의 입력 기록은 순환 상태를 담아 같은 결과로 재생된다(파일 왕복 포함).</summary>
        [Test]
        public void RotationRecording_ReplaysToSameChecksum()
        {
            var config = Config();
            var arenas = LoadAll();
            var world = new SimWorld(config, arenas, SimState.CreateMatch(config, arenas, 11, MatchPhase.Fighting));
            var s = world.State;
            var rec = InputRecording.Begin(config, arenas, s.Match.Rotation, s.Rng.State, s.Fighter0.Position, s.Fighter1.Position, s.Match.Phase, "Charge");
            Assert.AreEqual(world.ComputeChecksum(), rec.InitialChecksum);
            var p1 = new BotInputSource(() => world, 0, 5u);
            var p2 = new BotInputSource(() => world, 1, 6u);
            int startRound = world.State.Match.Round;
            for (int t = 0; t < MatchTicks; t++)
            {
                int tick = world.State.Tick;
                var a = p1.Poll(tick);
                var b = p2.Poll(tick);
                rec.Append(a, b);
                world.Step(a, b);
            }
            Assert.Greater(world.State.Match.Round, startRound, "라운드 교체가 기록에 들어가야 함");
            rec.Finish(world.ComputeChecksum());
            var loaded = InputRecording.FromBytes(rec.ToBytes());
            Assert.AreEqual(rec.Rotation.Order, loaded.Rotation.Order);
            CollectionAssert.AreEqual(NewMaps, loaded.ArenaNames);
            Assert.AreEqual(rec.FinalChecksum, loaded.Replay(config, LoadAll()));
        }

        // CP13-3 ------------------------------------------------------------------

        /// <summary>
        /// 모드 A Smart 봇으로 맵마다 시드 6 × 2분: 라운드 평균 10~30초, 이탈 0, 경계 튕김 데미지 30% 이하, 가시 데미지 25% 이하.
        /// 지형 데미지(벽꽝 반동 등)와 구조물 쪽 벽꽝 수도 기록한다.
        /// </summary>
        [TestCaseSource(nameof(NewMaps))]
        public void SmartBots_RoundLengthAndDamageShares(string name)
        {
            var config = Config();
            var arena = ArenaFiles.LoadByName(name);
            int rounds = 0, fightingTicks = 0, outOfBounds = 0, wallSplats = 0, boundaryHits = 0, propHits = 0, boxHits = 0, knockouts = 0, clashes = 0;
            double total = 0, boundary = 0, spike = 0, other = 0, prop = 0, boxed = 0;
            for (uint seed = 1; seed <= 6; seed++)
            {
                var world = TestWorlds.Create(MatchPhase.Countdown, seed, config, arena);
                var p1 = new BotInputSource(() => world, 0, 300u + seed * 17u);
                var p2 = new BotInputSource(() => world, 1, 700u + seed * 31u);
                for (int t = 0; t < MatchTicks; t++)
                {
                    int h0 = world.State.Fighter0.Health, h1 = world.State.Fighter1.Health;
                    int tick = world.State.Tick;
                    world.Step(p1.Poll(tick), p2.Poll(tick));
                    if (world.State.Match.Phase == MatchPhase.Fighting)
                        fightingTicks++;
                    var known = new float[2];
                    var spiked = new float[2];
                    var bounded = new float[2];
                    var propped = new float[2];
                    var boxDamage = new float[2];
                    for (int i = 0; i < world.Events.Count; i++)
                    {
                        var e = world.Events[i];
                        int f = e.Fighter;
                        switch (e.Type)
                        {
                            case SimEventType.Hit:
                            case SimEventType.Throw:
                                known[f] += e.Strength;
                                break;
                            case SimEventType.Spike:
                                spiked[f] += e.Strength;
                                break;
                            case SimEventType.BoundaryHit:
                                bounded[f] += e.Strength;
                                boundaryHits++;
                                break;
                            case SimEventType.PropHit:
                                propped[f] += e.Strength;
                                propHits++;
                                break;
                            case SimEventType.BoxHit:
                                boxDamage[f] += e.Strength;
                                boxHits++;
                                break;
                            case SimEventType.BoxKnockout:
                                knockouts++;
                                break;
                            case SimEventType.WallSplat:
                                wallSplats++;
                                break;
                            case SimEventType.Clash:
                                clashes++;
                                break;
                            case SimEventType.RoundEnd:
                                rounds++;
                                break;
                            case SimEventType.OutOfBounds:
                                outOfBounds++;
                                break;
                        }
                    }
                    int[] drops = { h0 - world.State.Fighter0.Health, h1 - world.State.Fighter1.Health };
                    for (int f = 0; f < 2; f++)
                    {
                        if (drops[f] <= 0)
                            continue;
                        total += drops[f];
                        double rest = drops[f];
                        double b = System.Math.Min(rest, bounded[f]);
                        boundary += b;
                        rest -= b;
                        double s = System.Math.Min(rest, spiked[f]);
                        spike += s;
                        rest -= s;
                        double pr = System.Math.Min(rest, propped[f]);
                        prop += pr;
                        rest -= pr;
                        double bx = System.Math.Min(rest, boxDamage[f]);
                        boxed += bx;
                        rest -= bx;
                        rest -= System.Math.Min(rest, known[f]);
                        other += rest;
                    }
                }
            }
            float average = rounds > 0 ? fightingTicks / 60f / rounds : float.PositiveInfinity;
            double boundaryShare = total > 0 ? boundary / total : 0, spikeShare = total > 0 ? spike / total : 0;
            string report = $"[Knockbox][CP13-3] {name}: 라운드 {rounds}, 평균 {average:F1}초, 경계 튕김 데미지 {boundaryShare:P0}({boundaryHits}회), 가시 {spikeShare:P0}, "
                            + $"구조물에 맞음 {(total > 0 ? prop / total : 0):P0}({propHits}회), 상자에 맞음 {(total > 0 ? boxed / total : 0):P0}({boxHits}회, 상자 쳐냄 {knockouts}), 벽꽝 등 지형 {(total > 0 ? other / total : 0):P0}(벽꽝 {wallSplats}), 경기장 이탈 {outOfBounds}"
                            + $", 전투 {fightingTicks / 3600f:F1}분, 벽꽝/분 {wallSplats / (fightingTicks / 3600f):F1}, 클래시/분 {clashes / (fightingTicks / 3600f):F1}";
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log(report);
            Assert.AreEqual(0, outOfBounds, "경기장 이탈");
            Assert.That(average, Is.InRange(10f, 30f), report);
            Assert.LessOrEqual(boundaryShare, 0.30, report);
            Assert.LessOrEqual(spikeShare, 0.25, report);
            // CP16-6: 상자 데미지 비중 25% 이하(09 4절).
            Assert.LessOrEqual(total > 0 ? boxed / total : 0, 0.25, report);
        }
    }
}
