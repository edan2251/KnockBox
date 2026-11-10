namespace Knockbox.Simulation
{
    public enum TerrainKind : byte
    {
        Solid,
        OneWay,
        Bounce,
        Spike,
        /// <summary>
        /// 구조물의 임시 물체(CR-002, CP13): 아직 물리가 없는 구조물이 움직이지 않는 단단한 상자로 충돌한다.
        /// 동작은 Solid와 같고, 연출과 맵 측정만 구분한다.
        /// </summary>
        Prop,
    }

    /// <summary>축 정렬 지형 상자. 좌표는 경기장 중심이 원점, 위가 +y.</summary>
    public struct ArenaBox
    {
        public TerrainKind Kind;
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;
        public ConvexPolygon Polygon;
        public bool IsPolygon=>Polygon!=null;

        public ArenaBox(TerrainKind kind, float minX, float minY, float maxX, float maxY)
        {
            Kind = kind;
            Polygon = null;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public ArenaBox(TerrainKind kind,ConvexPolygon polygon)
        {Kind=kind;Polygon=polygon;MinX=polygon.Min.X;MinY=polygon.Min.Y;MaxX=polygon.Max.X;MaxY=polygon.Max.Y;}

        /// <summary>단방향 발판을 제외한 막는 지형(옆과 아래에서도 막는다).</summary>
        public bool BlocksAllSides => Kind != TerrainKind.OneWay;

        /// <summary>벽 타기·벽 점프가 되는 단단한 상자(고정 지형 또는 구조물 임시 물체).</summary>
        public bool IsSolidBlock => Kind == TerrainKind.Solid || Kind == TerrainKind.Prop;

        public override string ToString() => $"{Kind} [{MinX}, {MinY}] - [{MaxX}, {MaxY}]";
    }

    /// <summary>
    /// 경기장(04-arena.md). 항상 벽으로 닫혀 있다. 크기는 맵마다: v1(테스트 고정 자료) 32 × 18, v2(경기 순환) 36 × 20.
    /// 시뮬레이션과 연출이 같은 상자 목록을 쓴다. 상태가 아니라 설정처럼 라운드 동안 바뀌지 않는다.
    /// </summary>
    public sealed class ArenaDef
    {
        public const int ClassicColumns = 32;
        public const int ClassicRows = 18;

        readonly ArenaBox[] boxes;
        readonly GraphRope[] graphRopes;
        public int GraphRopeCount => graphRopes.Length;
        public ref readonly GraphRope GetGraphRope(int index) => ref graphRopes[index];
        public bool HasConnectorBodies { get { foreach(var p in props) if(p.IsConnector)return true;return false; } }
        public bool HasPropGraph => GraphRopeCount>0 || HasConnectorBodies;
        readonly bool hasConvexTerrain;
        readonly PropDef[] props = new PropDef[0];
        /// <summary>강체로 움직이는 구조물(지금 구현된 단계 이하)의 번호와 연결. 나머지는 임시 물체 상자.</summary>
        readonly int[] dynamicProps = new int[0];
        readonly PropJoint[] joints = new PropJoint[0];
        /// <summary>PropBoxStart 뒤 상자 k가 어느 구조물의 임시 물체인지.</summary>
        readonly int[] staticProps = new int[0];

        /// <param name="spawnFoot0">P1 시작 칸의 가로 중심과 칸 바닥 높이. 파이터는 이 점 위에 선다.</param>
        public ArenaDef(string name, ArenaBox[] boxes, Vec2 spawnFoot0, Vec2 spawnFoot1)
            : this(name, ClassicColumns, ClassicRows, boxes, spawnFoot0, spawnFoot1)
        {
        }

        public ArenaDef(string name, int columns, int rows, ArenaBox[] boxes, Vec2 spawnFoot0, Vec2 spawnFoot1)
            : this(name, columns, rows, false, boxes, spawnFoot0, spawnFoot1)
        {
        }

        /// <param name="hasBoundary">
        /// 맵 v2(CR-002): 바깥 테두리 칸은 지형이 아니고, 테두리 안쪽 면이 경계 튕김(03-combat.md 3절)의 경계가 된다.
        /// </param>
        public ArenaDef(string name, int columns, int rows, bool hasBoundary, ArenaBox[] boxes, Vec2 spawnFoot0, Vec2 spawnFoot1)
            : this(name, columns, rows, hasBoundary, boxes, spawnFoot0, spawnFoot1, new PropDef[0], new int[0], new int[0], new PropJoint[0])
        {
        }

        ArenaDef(string name, int columns, int rows, bool hasBoundary, ArenaBox[] boxes, Vec2 spawnFoot0, Vec2 spawnFoot1, PropDef[] props,
            int[] staticProps, int[] dynamicProps, PropJoint[] joints, GraphRope[] graphRopes = null)
        {
            this.graphRopes = graphRopes ?? System.Array.Empty<GraphRope>();
            this.props = props;
            this.staticProps = staticProps;
            this.dynamicProps = dynamicProps;
            this.joints = joints;
            Name = name;
            Columns = columns;
            Rows = rows;
            HasBoundary = hasBoundary;
            this.boxes = boxes;hasConvexTerrain=System.Array.Exists(boxes,b=>b.IsPolygon);
            SpawnFoot0 = spawnFoot0;
            SpawnFoot1 = spawnFoot1;
        }

        ArenaDef[] rotated;
        int[] pieceOfBox;

        /// <summary>
        /// CR-004 E(SpiderMovement)용 자료를 한 번 만든다: 90° 단위로 돌린 맵 사본 셋(중력 오른쪽·위·왼쪽이 아래가 되도록)과,
        /// 프레임에 닿지 않는 내부 고체 상자를 맞닿음으로 묶은 지형 조각 번호. Step 밖(SimWorld 생성)에서 부른다.
        /// </summary>
        public void PrepareSurfaceData()
        {
            if (rotated != null)
                return;
            rotated = new ArenaDef[4];
            rotated[0] = this;
            for (int g = 1; g < 4; g++)
            {
                var list = new ArenaBox[boxes.Length];
                for (int i = 0; i < boxes.Length; i++)
                {
                    var b = boxes[i];
                    if(b.IsPolygon)
                    {
                        var vertices=b.Polygon.CopyPoints();for(int v=0;v<vertices.Length;v++)vertices[v]=SpiderMovement.ToLocal(vertices[v],g);
                        list[i]=new ArenaBox(b.Kind,new ConvexPolygon(vertices));continue;
                    }
                    var p = SpiderMovement.ToLocal(new Vec2(b.MinX, b.MinY), g);
                    var q = SpiderMovement.ToLocal(new Vec2(b.MaxX, b.MaxY), g);
                    list[i] = new ArenaBox(b.Kind, SimMath.Min(p.X, q.X), SimMath.Min(p.Y, q.Y), SimMath.Max(p.X, q.X), SimMath.Max(p.Y, q.Y));
                }
                bool swap = g % 2 == 1;
                rotated[g] = new ArenaDef(Name, swap ? Rows : Columns, swap ? Columns : Rows, HasBoundary, list, SpawnFoot0, SpawnFoot1);
            }
            pieceOfBox = new int[boxes.Length];
            float fx = HalfWidth - 1f - 1e-3f, fy = HalfHeight - 1f - 1e-3f;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                bool frame = b.MinX <= -fx || b.MaxX >= fx || b.MinY <= -fy || b.MaxY >= fy;
                pieceOfBox[i] = b.IsSolidBlock && !frame ? i : -1;
            }
            // 맞닿은 내부 상자끼리 같은 조각(가장 작은 번호)으로 묶는다.
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < boxes.Length; i++)
                {
                    for (int j = 0; j < boxes.Length; j++)
                    {
                        if (pieceOfBox[i] < 0 || pieceOfBox[j] < 0 || pieceOfBox[j] <= pieceOfBox[i])
                            continue;
                        var a = boxes[i];
                        var b = boxes[j];
                        bool touch = a.MinX <= b.MaxX + 1e-3f && b.MinX <= a.MaxX + 1e-3f && a.MinY <= b.MaxY + 1e-3f && b.MinY <= a.MaxY + 1e-3f;
                        if(touch&&(a.IsPolygon||b.IsPolygon))touch=!(a.IsPolygon&&a.Polygon.SeparatedFrom(b,.001f))&&!(b.IsPolygon&&b.Polygon.SeparatedFrom(a,.001f));
                        if (touch)
                        {
                            pieceOfBox[j] = pieceOfBox[i];
                            changed = true;
                        }
                    }
                }
            }
        }

        /// <summary>중력 g가 아래가 되도록 돌린 맵(PrepareSurfaceData 뒤). g = 0이면 자기 자신.</summary>
        public ArenaDef Rotated(int g) => g == 0 || rotated == null ? this : rotated[g];

        /// <summary>상자가 속한 떠 있는 지형 조각 번호. 프레임·비고체는 −1.</summary>
        public int PieceOf(int box) => pieceOfBox == null || box < 0 || box >= pieceOfBox.Length ? -1 : pieceOfBox[box];

        public string Name { get; }
        public int Columns { get; }
        public int Rows { get; }
        public float HalfWidth => Columns * 0.5f;
        public float HalfHeight => Rows * 0.5f;
        /// <summary>경계 튕김을 쓰는 맵(테두리 벽 없음).</summary>
        public bool HasBoundary { get; }
        /// <summary>경계 사각형의 반폭·반높이: 테두리 칸의 안쪽 면. 36 × 20이면 ±17 × ±9(16:9 화면 ±17.8 × ±10 안쪽).</summary>
        public float BoundaryHalfWidth => HalfWidth - 1f;
        public float BoundaryHalfHeight => HalfHeight - 1f;
        public Vec2 SpawnFoot0 { get; }
        public Vec2 SpawnFoot1 { get; }
        public int BoxCount => boxes.Length;
        public bool HasConvexTerrain=>hasConvexTerrain;

        /// <summary>구조물(짝 파일, 09-props-physics.md 5절). 상자 목록의 PropBoxStart부터 같은 순서로 임시 물체 상자가 있다.</summary>
        public int PropCount => props.Length;
        public ref readonly PropDef GetProp(int index) => ref props[index];
        public bool RequiresExtendedMapChecksum
        {
            get { if(HasPropGraph || HasConvexTerrain)return true;foreach (var p in props) if (p.HasAuthoringData) return true; return false; }
        }
        public bool HasPlayerPassThroughProps
        {
            get { foreach (var p in props) if (p.PlayerPassThrough) return true; return false; }
        }
        public bool HasAuthoredRopes
        {
            get { foreach (var p in props) if (p.Rope.Enabled) return true; return false; }
        }
        public bool HasRopeConnectors
        {
            get { foreach(var p in props) if(p.Rope.Enabled && p.Rope.RopeMode==RopeBreakMode.Connector) return true; return false; }
        }
        public bool HasRopeModes
        {
            get { foreach(var p in props) if(p.Rope.Enabled && p.Rope.RopeMode!=RopeBreakMode.LegacyTension) return true; return false; }
        }
        public int PropBoxStart => boxes.Length - staticProps.Length;

        /// <summary>임시 물체 상자(PropBoxStart 이후)가 나타내는 구조물 번호.</summary>
        public int PropOfBox(int boxIndex) => staticProps[boxIndex - PropBoxStart];

        /// <summary>강체 구조물 수와 그 구조물 번호·연결. SimState.Props의 몸 순서와 같다.</summary>
        public int DynamicPropCount => dynamicProps.Length;
        public int DynamicProp(int body) => dynamicProps[body];
        public ref readonly PropJoint GetJoint(int body) => ref joints[body];

        /// <summary>
        /// 같은 맵에 구조물을 더한 새 맵. 지금 구현된 물리 단계(PropPhysics.ImplementedStage) 이하의 종류는 강체가 되고,
        /// 나머지는 그 단계가 올 때까지 움직이지 않는 임시 물체 상자(TerrainKind.Prop)로 지형 뒤에 붙는다(D-030).
        /// </summary>
        public ArenaDef WithProps(PropDef[] newProps) => WithProps(newProps, PropPhysics.ImplementedStage);

        /// <summary>maxStage 이하의 종류를 강체로(물리 단계를 맵보다 먼저 시험하는 테스트 장면용).</summary>
        public ArenaDef WithProps(PropDef[] newProps, int maxStage)
        {
            int terrain = PropBoxStart;
            var all = new System.Collections.Generic.List<ArenaBox>(terrain + newProps.Length);
            for (int i = 0; i < terrain; i++)
                all.Add(boxes[i]);
            var statics = new System.Collections.Generic.List<int>();
            var dynamics = new System.Collections.Generic.List<int>();
            var bodyOf = new int[newProps.Length];
            for (int i = 0; i < newProps.Length; i++)
            {
                ref readonly var p = ref newProps[i];
                if (PropDef.Stage(p.Kind) <= maxStage)
                {
                    bodyOf[i] = dynamics.Count;
                    dynamics.Add(i);
                    continue;
                }
                bodyOf[i] = -1;
                statics.Add(i);
                all.Add(new ArenaBox(TerrainKind.Prop, p.MinX, p.MinY, p.MaxX, p.MaxY));
            }
            var jointList = new System.Collections.Generic.List<PropJoint>(dynamics.Count);
            foreach (int i in dynamics)
                jointList.Add(JointOf(newProps, i, bodyOf));
            if (dynamics.Count > PropState.Capacity)
                throw new System.InvalidOperationException($"{Name}: 강체 구조물 {dynamics.Count}개(용량 {PropState.Capacity})");
            return new ArenaDef(Name, Columns, Rows, HasBoundary, all.ToArray(), SpawnFoot0, SpawnFoot1, (PropDef[])newProps.Clone(),
                statics.ToArray(), dynamics.ToArray(), jointList.ToArray());
        }

        static PropJoint JointOf(PropDef[] all, int index, int[] bodyOf)
        {
            ref readonly var p = ref all[index];
            if (p.Rope.Enabled)
                return new PropJoint { Kind=PropJointKind.Rope, WorldAnchor=p.Rope.WorldAnchor,
                    LocalAnchor=p.Rope.LocalAnchor, Length=p.Rope.Length, OtherBody=-1,
                    UsesTension=true, BreakForce=p.Rope.RopeMode==RopeBreakMode.LegacyTension && p.Rope.Breakable?p.Rope.BreakForce:0,
                    RopeMode=p.Rope.RopeMode, ConnectorPosition=p.Rope.ConnectorPosition,
                    ConnectorSize=p.Rope.ConnectorSize, ConnectorSpeedLoss=p.Rope.ConnectorSpeedLoss };
            if (!p.HasAnchor)
                return new PropJoint { Kind = PropJointKind.None, OtherBody = -1 };
            if (p.HangsByRope)
            {
                var local = new Vec2(0f, p.HalfSize.Y);
                int other = p.AnchorProp >= 0 ? bodyOf[p.AnchorProp] : -1;
                // 다른 강체에 이어진 줄: 그 몸의 아랫면 가운데에서 이 몸의 윗면 가운데까지.
                var otherLocal = other >= 0 ? new Vec2(0f, -all[p.AnchorProp].HalfSize.Y) : Vec2.Zero;
                var anchor = other >= 0 ? all[p.AnchorProp].Center + otherLocal : p.Anchor;
                var d = p.Center + local - anchor;
                return new PropJoint
                {
                    Kind = PropJointKind.Rope, WorldAnchor = anchor, LocalAnchor = local, Length = SimMath.Sqrt(d.LengthSquared),
                    OtherBody = other, OtherLocal = otherLocal, BreakImpulse = p.BreakImpulse,
                };
            }
            var kind = p.Kind == PropKind.Hinged ? PropJointKind.Hinge : p.Kind == PropKind.Seesaw ? PropJointKind.Seesaw : PropJointKind.Axis;
            return new PropJoint { Kind = kind, WorldAnchor = p.Anchor, LocalAnchor = p.Anchor - p.Center, OtherBody = -1, BreakImpulse = p.BreakImpulse };
        }

        public ref readonly ArenaBox GetBox(int index) => ref boxes[index];

        public Vec2 SpawnFoot(int fighter) => fighter == 0 ? SpawnFoot0 : SpawnFoot1;

        public int CountBoxes(TerrainKind kind)
        {
            int n = 0;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i].Kind == kind)
                    n++;
            }
            return n;
        }

        /// <summary>기존 schema 2~6은 고정한다. 새 로프 종류가 있을 때만 schema 7로 연결부 설정까지 검증한다.</summary>
        public ArenaDef WithGraphRopes(GraphRope[] connections)
        {
            if(connections==null || connections.Length>GraphRope.Capacity)throw new System.ArgumentException("로프는 최대 96개입니다.");
            for(int i=0;i<connections.Length;i++)
            {
                var r=connections[i];
                if(string.IsNullOrEmpty(r.Id)||DuplicateRopeId(connections,i)||r.Body<0||r.Body>=DynamicPropCount
                    ||r.Joint.OtherBody < -1 || r.Joint.OtherBody>=DynamicPropCount || r.Body==r.Joint.OtherBody
                    ||r.Joint.Kind!=PropJointKind.Rope || r.Joint.Length<.05f)
                    throw new System.ArgumentException("로프 ID·연결점·길이를 확인해주세요.");
            }
            return new ArenaDef(Name,Columns,Rows,HasBoundary,boxes,SpawnFoot0,SpawnFoot1,props,staticProps,dynamicProps,joints,(GraphRope[])connections.Clone());
        }

        /// <summary>앞선 로프에 같은 ID가 있는지(대소문자 구분). 최대 96개라 HashSet 없이 비교한다.</summary>
        static bool DuplicateRopeId(GraphRope[] connections,int index)
        {
            for(int j=0;j<index;j++)
                if(string.Equals(connections[j].Id,connections[index].Id,System.StringComparison.Ordinal))return true;
            return false;
        }

        public uint ComputeGeometryChecksum()
        {
            uint legacy=ComputePreGraphGeometryChecksum();
            if(!HasPropGraph)return legacy;
            var h=Hasher.Create();h.AddInt(8);h.AddUInt(legacy);
            foreach(var p in props){h.AddBool(p.IsConnector);if(p.IsConnector)h.AddFloat(p.ConnectorSpeedLoss);}
            h.AddInt(GraphRopeCount);
            foreach(var r in graphRopes)
            {
                h.AddInt(r.Id.Length);foreach(char c in r.Id)h.AddInt(c);
                h.AddInt(r.Body);h.AddInt(r.Joint.OtherBody);
                h.AddFloat(r.Joint.WorldAnchor.X);h.AddFloat(r.Joint.WorldAnchor.Y);
                h.AddFloat(r.Joint.OtherLocal.X);h.AddFloat(r.Joint.OtherLocal.Y);
                h.AddFloat(r.Joint.LocalAnchor.X);h.AddFloat(r.Joint.LocalAnchor.Y);h.AddFloat(r.Joint.Length);
            }
            return h.Hash;
        }

        uint ComputePreGraphGeometryChecksum()
        {
            var h = Hasher.Create();
            bool extended = RequiresExtendedMapChecksum;
            bool passThrough = HasPlayerPassThroughProps;
            bool authoredRopes = HasAuthoredRopes;
            bool polygons=HasConvexTerrain;
            bool ropeModes=HasRopeModes;
            h.AddInt(ropeModes ? 7 : polygons ? 6 : authoredRopes ? 5 : passThrough ? 4 : extended ? 3 : 2);
            h.AddUInt(ComputeChecksum());
            if(ropeModes){h.AddBool(extended);h.AddBool(passThrough);h.AddBool(authoredRopes);h.AddBool(polygons);}
            if(polygons)
            {
                if(!ropeModes){h.AddBool(extended);h.AddBool(passThrough);h.AddBool(authoredRopes);}
                foreach(var box in boxes)
                {
                    h.AddInt(box.IsPolygon?box.Polygon.Count:0);
                    if(box.IsPolygon)for(int v=0;v<box.Polygon.Count;v++){var p=box.Polygon.Point(v);h.AddFloat(p.X);h.AddFloat(p.Y);}
                }
            }
            for (int i = 0; i < props.Length; i++)
            {
                h.AddFloat(props[i].Center.X); h.AddFloat(props[i].Center.Y);
                h.AddFloat(props[i].HalfSize.X); h.AddFloat(props[i].HalfSize.Y);
                if (extended)
                {
                    string id = props[i].Id ?? "";
                    h.AddInt(id.Length); foreach (char c in id) h.AddInt(c);
                    h.AddFloat(props[i].Mass); h.AddBool(props[i].StartAwake);
                    if (passThrough || authoredRopes) h.AddBool(props[i].PlayerPassThrough);
                    if (authoredRopes)
                    {
                        var rope=props[i].Rope;h.AddBool(rope.Enabled);
                        if(rope.Enabled)
                        {
                            string ropeId=rope.Id??"";h.AddInt(ropeId.Length);foreach(char c in ropeId)h.AddInt(c);
                            h.AddFloat(rope.WorldAnchor.X);h.AddFloat(rope.WorldAnchor.Y);
                            h.AddFloat(rope.LocalAnchor.X);h.AddFloat(rope.LocalAnchor.Y);
                            h.AddFloat(rope.Length);h.AddBool(rope.Breakable);h.AddFloat(rope.BreakForce);
                            if(ropeModes)
                            {
                                h.AddByte((byte)rope.RopeMode);h.AddFloat(rope.ConnectorPosition);
                                h.AddFloat(rope.ConnectorSize);h.AddFloat(rope.ConnectorSpeedLoss);
                            }
                        }
                    }
                }
            }
            h.AddInt(dynamicProps.Length);
            for (int i = 0; i < dynamicProps.Length; i++) h.AddInt(dynamicProps[i]);
            return h.Hash;
        }

        /// <summary>Legacy schema 1: do not change historical recording or protocol-8 hashes.</summary>
        public uint ComputeChecksum()
        {
            var h = Hasher.Create();
            h.AddInt(Columns);
            h.AddInt(Rows);
            h.AddBool(HasBoundary);
            h.AddInt(boxes.Length);
            for (int i = 0; i < boxes.Length; i++)
            {
                h.AddByte((byte)boxes[i].Kind);
                h.AddFloat(boxes[i].MinX);
                h.AddFloat(boxes[i].MinY);
                h.AddFloat(boxes[i].MaxX);
                h.AddFloat(boxes[i].MaxY);
            }
            h.AddInt(props.Length);
            for (int i = 0; i < props.Length; i++)
            {
                h.AddByte((byte)props[i].Kind);
                h.AddInt(props[i].Group);
                h.AddBool(props[i].HasAnchor);
                h.AddFloat(props[i].Anchor.X);
                h.AddFloat(props[i].Anchor.Y);
                h.AddInt(props[i].AnchorProp);
                h.AddFloat(props[i].BreakImpulse);
            }
            h.AddFloat(SpawnFoot0.X);
            h.AddFloat(SpawnFoot0.Y);
            h.AddFloat(SpawnFoot1.X);
            h.AddFloat(SpawnFoot1.Y);
            return h.Hash;
        }
    }
}
