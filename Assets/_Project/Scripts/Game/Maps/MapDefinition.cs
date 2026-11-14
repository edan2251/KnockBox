using System;
using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    [Serializable]
    public struct MapBox : IEquatable<MapBox>
    {
        public string id;
        public TerrainKind kind;
        public float minX, minY, maxX, maxY;
        public Vector2[] vertices;
        public bool IsPolygon=>vertices!=null&&vertices.Length>0;
        public MapBox Copy(){var b=this;b.vertices=IsPolygon?(Vector2[])vertices.Clone():null;return b;}
        // Unity stores null arrays as empty arrays. A saved/reopened shape is a value,
        // independent of the array instance used by its defensive copy.
        public bool Equals(MapBox other)
        {
            if(id!=other.id||kind!=other.kind||!minX.Equals(other.minX)||!minY.Equals(other.minY)
                ||!maxX.Equals(other.maxX)||!maxY.Equals(other.maxY))return false;
            int count=vertices?.Length??0;if(count!=(other.vertices?.Length??0))return false;
            for(int i=0;i<count;i++)if(!vertices[i].Equals(other.vertices[i]))return false;
            return true;
        }
        public override bool Equals(object other)=>other is MapBox box&&Equals(box);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash=id==null?0:id.GetHashCode();hash=hash*31+(int)kind;
                hash=hash*31+minX.GetHashCode();hash=hash*31+minY.GetHashCode();
                hash=hash*31+maxX.GetHashCode();hash=hash*31+maxY.GetHashCode();
                if(vertices!=null)for(int i=0;i<vertices.Length;i++)hash=hash*31+vertices[i].GetHashCode();
                return hash;
            }
        }
        public ArenaBox ToBox()
        {
            if(!IsPolygon)return new ArenaBox(kind,minX,minY,maxX,maxY);
            var points=new Vec2[vertices.Length];for(int i=0;i<points.Length;i++)points[i]=new Vec2(vertices[i].x,vertices[i].y);
            return new ArenaBox(kind,new ConvexPolygon(points));
        }
        public static MapBox From(ArenaBox b,int order) => new MapBox {id="box-"+order.ToString("D3"),
            kind=b.Kind,minX=b.MinX,minY=b.MinY,maxX=b.MaxX,maxY=b.MaxY,vertices=b.IsPolygon?Array.ConvertAll(b.Polygon.CopyPoints(),p=>new Vector2(p.X,p.Y)):null};
    }

    /// <summary>Authoritative ordered terrain and physical boxes. Conversion is lossless; no Unity physics or implicit snapping.</summary>
    [CreateAssetMenu(menuName="Knockbox/맵/맵 정의")]
    public sealed class MapDefinition : ScriptableObject
    {
        public const int SchemaVersion=4;
        [SerializeField] string stableId;
        [SerializeField] string displayName;
        [SerializeField] bool preservedBaseline;
        [SerializeField] int revision=1;
        [SerializeField] int columns=36,rows=20;
        [SerializeField] bool boundary;
        [SerializeField] Vector2 spawn0,spawn1;
        [SerializeField] MapBox[] boxes=Array.Empty<MapBox>();
        [SerializeField] MapPhysicalBox[] physicalBoxes=Array.Empty<MapPhysicalBox>();
        [SerializeField] MapRope[] ropes=Array.Empty<MapRope>();
        [SerializeField,Range(0,2)] int theme=2;
        [SerializeField] Knockbox.Presentation.MapThemeSettings mapTheme = new Knockbox.Presentation.MapThemeSettings();
        [SerializeField] Vector2 light=new Vector2(0,25);
        [SerializeField] bool ceiling=true;
        [SerializeField,Range(0,1)] float shadow=.65f;
        // 보상(카드 선택) 맵 전용. 화면에만 쓰며 ToArena·지형 체크섬·온라인 목록·기록에 들어가지 않는다(일반 경기 맵은 비어 있음).
        [SerializeField] bool rewardMap;
        [SerializeField] RewardStele[] steles=Array.Empty<RewardStele>();
        [SerializeField] Vector2 loserStand=new Vector2(-13.5f,-9);
        [SerializeField] RewardStageSettings reward=new RewardStageSettings();
        public string Id=>stableId;
        public string DisplayName=>displayName;
        public string MatchName=>preservedBaseline?"Brawl":"map:"+stableId;
        public bool PreservedBaseline=>preservedBaseline;
        public int Revision=>revision;
        public int Columns=>columns;
        public int Rows=>rows;
        public int BoxCount=>boxes.Length;
        public MapBox Box(int index)=>boxes[index].Copy();
        public int PhysicalBoxCount=>physicalBoxes?.Length??0;
        public MapPhysicalBox PhysicalBox(int index)=>physicalBoxes[index];
        public int RopeCount=>ropes?.Length??0;
        public MapRope Rope(int index)=>ropes[index];
        public Vector2 Spawn(int player)=>player==0?spawn0:spawn1;
        /// <summary>보상(카드 선택) 맵인지. 보상 맵이면 비석·패자 자리·보상 장면 설정을 쓴다.</summary>
        public bool IsRewardMap=>rewardMap;
        public int SteleCount=>steles?.Length??0;
        public RewardStele Stele(int index)=>steles[index].Copy();
        /// <summary>P1이 고를 때 패자가 서는 발 위치. P2가 고르면 좌우를 뒤집는다.</summary>
        public Vector2 LoserStand=>loserStand;
        public RewardStageSettings Reward=>reward??=new RewardStageSettings();
        public ArenaMeta Metadata()=>new ArenaMeta{Theme=theme,HasLight=true,Light=light,Ceiling=ceiling,Shadow=shadow,Rotation=false,MapTheme=mapTheme};

        /// <summary>보존된 Brawl 원본의 판정 체크섬. 옛 txt "Brawl"과 같아야 과거 기록·온라인이 유지된다.</summary>
        public const uint PreservedBrawlChecksum=0x483E0206u;
        /// <summary>경기용 변환. 보존된 Brawl 원본이 바뀌었으면 경기를 시작하지 않는다.</summary>
        public ArenaDef ToMatchArena()
        {
            var arena=ToArena();
            if(preservedBaseline&&arena.ComputeChecksum()!=PreservedBrawlChecksum)
                throw new InvalidOperationException("보존된 Brawl 원본이 바뀌었습니다. 시험용 사본을 사용해주세요.");
            return arena;
        }

        public ArenaDef ToArena()
        {
            var errors=Validate();
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            var ordered=new ArenaBox[boxes.Length];
            for(int i=0;i<ordered.Length;i++)ordered[i]=boxes[i].ToBox();
            var arena=new ArenaDef(MatchName,columns,rows,boundary,ordered,new Vec2(spawn0.x,spawn0.y),new Vec2(spawn1.x,spawn1.y));
            if(PhysicalBoxCount==0)return arena;
            var props=new PropDef[PhysicalBoxCount];
            for(int i=0;i<props.Length;i++)props[i]=physicalBoxes[i].ToProp(i);
            var graph=new List<GraphRope>();
            for(int i=0;i<RopeCount;i++)
            {
                var r=ropes[i];int body=Array.FindIndex(props,b=>b.Id==r.boxId);
                if(!r.graph){props[body].Rope=r.ToRope();continue;}
                int source=string.IsNullOrEmpty(r.sourceBoxId)?-1:Array.FindIndex(props,b=>b.Id==r.sourceBoxId);
                graph.Add(new GraphRope{Id=r.id,Body=body,Joint=new PropJoint{Kind=PropJointKind.Rope,
                    OtherBody=source,OtherLocal=new Vec2(r.sourceLocalAnchor.x,r.sourceLocalAnchor.y),
                    WorldAnchor=new Vec2(r.anchor.x,r.anchor.y),LocalAnchor=new Vec2(r.localAnchor.x,r.localAnchor.y),
                    Length=r.length,UsesTension=true,RopeMode=RopeBreakMode.Unbreakable}});
            }
            return arena.WithProps(props).WithGraphRopes(graph.ToArray());
        }
        public List<string> Validate(float bodySize=.72f)
        {
            var errors=new List<string>();
            if(mapTheme!=null)errors.AddRange(mapTheme.Validate());
            if(string.IsNullOrWhiteSpace(stableId)||stableId.IndexOfAny(new[]{'/',':','\\',' '})>=0)errors.Add("맵 내부 ID에는 공백·콜론·경로 구분자를 넣을 수 없습니다.");
            if(columns<4||rows<4)errors.Add("맵 크기는 최소 4 × 4입니다.");
            if(boxes==null||boxes.Length==0){errors.Add("맵에 지형이 없습니다.");return errors;}
            var ids=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<boxes.Length;i++)
            {
                var b=boxes[i];
                if(string.IsNullOrEmpty(b.id)||!ids.Add(b.id))errors.Add("도형 내부 ID가 없거나 중복됩니다. 순서: "+i);
                if(!Finite(b.minX)||!Finite(b.minY)||!Finite(b.maxX)||!Finite(b.maxY)||b.maxX<=b.minX||b.maxY<=b.minY)errors.Add("도형 위치 또는 크기가 잘못됐습니다. 순서: "+i);
                if(!Enum.IsDefined(typeof(TerrainKind),b.kind)||b.kind==TerrainKind.Prop)errors.Add("고정 지형의 종류가 잘못됐습니다. 순서: "+i);
                if(b.IsPolygon)
                {
                    if(b.kind!=TerrainKind.Solid)errors.Add("절단은 고정 일반 지형만 지원합니다. 순서: "+i);
                    try{var shape=b.ToBox();if(shape.MinX!=b.minX||shape.MinY!=b.minY||shape.MaxX!=b.maxX||shape.MaxY!=b.maxY)errors.Add("꼭짓점과 지형 범위가 다릅니다. 순서: "+i);}
                    catch(ArgumentException e){errors.Add("지형 "+i+": "+e.Message);}
                }
            }
            if(errors.Count>0)return errors;
            if(PhysicalBoxCount>PropState.Capacity)errors.Add("물리 상자는 최대 "+PropState.Capacity+"개입니다.");
            for(int i=0;i<PhysicalBoxCount;i++)
            {
                var b=physicalBoxes[i];var bounds=b.Bounds;
                if(string.IsNullOrEmpty(b.id)||!ids.Add(b.id))errors.Add("물리 상자 ID가 없거나 중복됩니다. 순서: "+i);
                if(!Finite(b.center.x)||!Finite(b.center.y)||!Finite(b.size.x)||!Finite(b.size.y)
                    ||b.size.x<=0||b.size.y<=0||!Finite(b.mass)||b.mass<=0)
                {errors.Add("물리 상자 위치·크기·질량이 잘못됐습니다. 순서: "+i);continue;}
                if(b.connector && (b.playerPassThrough || !Finite(b.connectorSpeedLoss) || b.connectorSpeedLoss<0 || b.connectorSpeedLoss>1))
                    errors.Add("연결 상자는 플레이어 통과를 끄고 관통 감속을 0~100%로 지정해주세요. 순서: "+i);
                float inset=boundary?1:0;
                if(bounds.minX< -columns*.5f+inset || bounds.maxX>columns*.5f-inset
                    ||bounds.minY< -rows*.5f+inset || bounds.maxY>rows*.5f-inset)
                    errors.Add("물리 상자가 경기장 밖에 있습니다. 순서: "+i);
                foreach(var ground in boxes)
                    if(ground.kind!=TerrainKind.OneWay && Overlaps(bounds,ground))
                    {errors.Add("물리 상자가 고정 지형과 겹칩니다. 순서: "+i);break;}
                for(int j=0;j<i;j++)if(Overlaps(bounds,physicalBoxes[j].Bounds))
                    {errors.Add("물리 상자가 서로 겹칩니다. 순서: "+j+" / "+i);break;}
            }
            if(RopeCount>GraphRope.Capacity)errors.Add("로프는 최대 "+GraphRope.Capacity+"개입니다.");
            var targets=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<RopeCount;i++)
            {
                var rope=ropes[i];
                if(string.IsNullOrEmpty(rope.id)||!ids.Add(rope.id))errors.Add("로프 ID가 없거나 중복됩니다. 순서: "+i);
                if(string.IsNullOrEmpty(rope.boxId)||(!rope.graph&&!targets.Add(rope.boxId)))errors.Add("상자 하나에는 로프 하나만 연결할 수 있습니다. 순서: "+i);
                int target=Array.FindIndex(physicalBoxes??Array.Empty<MapPhysicalBox>(),b=>b.id==rope.boxId);
                if(target<0)errors.Add("로프의 연결 상자가 없습니다. 순서: "+i);
                var anchor=rope.anchor;
                if(rope.graph)
                {
                    if(rope.mode!=RopeBreakMode.Unbreakable)errors.Add("새 연결 로프는 일반 로프만 사용합니다. 파괴는 별도 연결 상자로 설정해주세요. 순서: "+i);
                    if(!Finite(rope.sourceLocalAnchor.x)||!Finite(rope.sourceLocalAnchor.y))errors.Add("시작 연결점이 잘못됐습니다. 순서: "+i);
                    if(!string.IsNullOrEmpty(rope.sourceBoxId))
                    {
                        int source=Array.FindIndex(physicalBoxes??Array.Empty<MapPhysicalBox>(),b=>b.id==rope.sourceBoxId);
                        if(source<0)errors.Add("로프 시작 상자가 없습니다. 순서: "+i);
                        else
                        {
                            var b=physicalBoxes[source];anchor=b.center+rope.sourceLocalAnchor;
                            if(source==target)errors.Add("같은 상자 양끝끼리 연결할 수 없습니다. 순서: "+i);
                            if(Mathf.Abs(rope.sourceLocalAnchor.x)>b.size.x*.5f+.0001f||Mathf.Abs(rope.sourceLocalAnchor.y)>b.size.y*.5f+.0001f)
                                errors.Add("로프 시작 연결점이 상자 밖입니다. 순서: "+i);
                        }
                    }
                }
                if(!Finite(rope.anchor.x)||!Finite(rope.anchor.y)||!Finite(rope.localAnchor.x)||!Finite(rope.localAnchor.y))
                    errors.Add("로프 고정점·연결점 좌표가 잘못됐습니다. 순서: "+i);
                if(!Finite(rope.length)||rope.length<.05f)errors.Add("줄 길이는 0.05 이상이어야 합니다. 순서: "+i);
                if(rope.mode==RopeBreakMode.LegacyTension && (!Finite(rope.breakForce)||rope.breakForce<0||rope.breakable&&rope.breakForce<=0))
                    errors.Add("파단 가능한 줄의 당김 강도는 양수여야 합니다. 순서: "+i);
                if((int)rope.mode<0||(int)rope.mode>(int)RopeBreakMode.Connector)
                    errors.Add("로프 종류가 잘못됐습니다. 순서: "+i);
                if(rope.mode==RopeBreakMode.Connector)
                {
                    if(!Finite(rope.connectorPosition)||rope.connectorPosition<.05f||rope.connectorPosition>.95f)
                        errors.Add("연결부 위치는 고정점부터 5~95% 사이로 지정해주세요. 순서: "+i);
                    if(!Finite(rope.connectorSize)||rope.connectorSize<.1f||rope.connectorSize>2f)
                        errors.Add("연결부 크기는 0.1~2 사이로 지정해주세요. 순서: "+i);
                    if(!Finite(rope.connectorSpeedLoss)||rope.connectorSpeedLoss<0f||rope.connectorSpeedLoss>1f)
                        errors.Add("관통 감속은 0~100% 사이로 지정해주세요. 순서: "+i);
                }
                if(target>=0)
                {
                    var b=physicalBoxes[target];
                    if(Mathf.Abs(rope.localAnchor.x)>b.size.x*.5f+.0001f||Mathf.Abs(rope.localAnchor.y)>b.size.y*.5f+.0001f)
                        errors.Add("로프 연결점이 상자 밖입니다. 순서: "+i);
                    float distance=Vector2.Distance(anchor,b.center+rope.localAnchor);
                    if(distance>rope.length+.0001f)
                        errors.Add("Rope_"+i+": 줄 길이 "+rope.length.ToString("0.###")+" / 두 점 거리 "+distance.ToString("0.###")
                            +". 짧은 로프 길이 맞추기를 눌러주세요.");
                }
            }
            for(int p=0;p<2;p++)
            {
                var s=Spawn(p);float h=bodySize*.5f;
                if(!Finite(s.x)||!Finite(s.y)||Mathf.Abs(s.x)+h>columns*.5f||s.y< -rows*.5f||s.y+bodySize>rows*.5f)errors.Add("P"+(p+1)+" 시작 위치가 경기장 밖입니다.");
                foreach(var b in boxes)
                    if(b.kind!=TerrainKind.OneWay && Overlaps(new MapBox{minX=s.x-h,maxX=s.x+h,minY=s.y,maxY=s.y+bodySize},b))
                    { errors.Add("P"+(p+1)+" 시작 위치가 지형과 겹칩니다: "+b.id);break; }
                var body=new MapBox{minX=s.x-h,maxX=s.x+h,minY=s.y,maxY=s.y+bodySize};
                for(int i=0;i<PhysicalBoxCount;i++)if(!physicalBoxes[i].playerPassThrough && Overlaps(body,physicalBoxes[i].Bounds))
                    {errors.Add("P"+(p+1)+" 시작 위치가 물리 상자와 겹칩니다: "+i);break;}
            }
            if(Vector2.Distance(spawn0,spawn1)<bodySize)errors.Add("양쪽 시작 위치가 겹칩니다.");
            if(rewardMap)errors.AddRange(ValidateReward(bodySize));
            return errors;
        }
        /// <summary>보상 맵 규칙: 비석 3개, 볼록 다각형, 바닥 위에 섬, 서로·패자 자리와 겹치지 않음, 카드 면이 들어갈 최소 크기.</summary>
        public const int RewardSteleCount=3;
        public const float RewardMinHeight=10f,RewardMinWidth=4.5f;
        List<string> ValidateReward(float bodySize)
        {
            var errors=new List<string>();
            int count=SteleCount;
            if(count!=RewardSteleCount)errors.Add("보상 맵의 비석은 "+RewardSteleCount+"개여야 합니다. 지금 "+count+"개");
            var shapes=new List<MapBox>();
            for(int i=0;i<count;i++)
            {
                var s=steles[i];string who="비석 "+(i+1);
                if(s.vertices==null||s.vertices.Length<3){errors.Add(who+": 꼭짓점이 없습니다.");continue;}
                MapBox box;
                try{box=s.ToBox();box.ToBox();}
                catch(ArgumentException e){errors.Add(who+": "+e.Message);continue;}
                if(box.maxY-box.minY<RewardMinHeight)errors.Add(who+": 높이가 "+RewardMinHeight+"보다 낮아 그림 영역이 부족합니다.");
                if(box.maxX-box.minX<RewardMinWidth)errors.Add(who+": 폭이 "+RewardMinWidth+"보다 좁아 글자가 너무 작아집니다.");
                bool grounded=false;
                foreach(var b in boxes)if(b.kind!=TerrainKind.OneWay&&Mathf.Abs(b.maxY-box.minY)<.05f&&b.minX<box.maxX&&b.maxX>box.minX){grounded=true;break;}
                if(!grounded)errors.Add(who+": 아랫변이 바닥(고정 지형) 위에 닿아야 합니다.");
                foreach(var b in boxes)if(b.kind!=TerrainKind.OneWay&&Overlaps(box,b)){errors.Add(who+": 지형과 겹칩니다: "+b.id);break;}
                foreach(var other in shapes)if(Overlaps(box,other)){errors.Add(who+": 다른 비석과 겹칩니다.");break;}
                var stand=new MapBox{minX=loserStand.x-bodySize*.5f,maxX=loserStand.x+bodySize*.5f,minY=loserStand.y,maxY=loserStand.y+bodySize};
                if(Overlaps(stand,box))errors.Add(who+": 패자 자리와 겹칩니다.");
                shapes.Add(box);
            }
            return errors;
        }
        public static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        public static bool Overlaps(MapBox a,MapBox b)
        {
            if(!(a.minX<b.maxX-.0001f&&a.maxX>b.minX+.0001f&&a.minY<b.maxY-.0001f&&a.maxY>b.minY+.0001f))return false;
            if(b.IsPolygon&&!a.IsPolygon)return b.ToBox().Polygon.OverlapsBox(new Vec2((a.minX+a.maxX)*.5f,(a.minY+a.maxY)*.5f),new Vec2((a.maxX-a.minX)*.5f,(a.maxY-a.minY)*.5f));
            if(a.IsPolygon&&!b.IsPolygon)return Overlaps(b,a);
            // Both polygon inputs are uncommon editor queries; use all edge normals.
            if(a.IsPolygon&&b.IsPolygon)
            {
                var pa=a.ToBox().Polygon;var pb=b.ToBox().Polygon;
                for(int side=0;side<2;side++){var p=side==0?pa:pb;var q=side==0?pb:pa;
                    for(int i=0;i<p.Count;i++){float nearest=float.MaxValue;for(int j=0;j<q.Count;j++)nearest=Math.Min(nearest,Vec2.Dot(p.Normal(i),q.Point(j)-p.Point(i)));if(nearest>=-.0001f)return false;}}
            }
            return true;
        }
#if UNITY_EDITOR
        public void Import(ArenaDef source,ArenaMeta meta,string id,string title,bool baseline=false)
        {
            if(source.PropCount!=0)throw new NotSupportedException("기존 텍스트 맵의 구조물 자동 가져오기는 지원하지 않습니다. 제작기에서 물리 상자를 배치해주세요.");
            // Definitions own their serialized theme; importing must not alias another map's settings.
            mapTheme=meta.MapTheme==null?new Knockbox.Presentation.MapThemeSettings()
                :JsonUtility.FromJson<Knockbox.Presentation.MapThemeSettings>(JsonUtility.ToJson(meta.MapTheme));
            physicalBoxes=Array.Empty<MapPhysicalBox>();ropes=Array.Empty<MapRope>();
            stableId=id;displayName=title;preservedBaseline=baseline;revision=1;
            columns=source.Columns;rows=source.Rows;boundary=source.HasBoundary;
            spawn0=new Vector2(source.SpawnFoot0.X,source.SpawnFoot0.Y);spawn1=new Vector2(source.SpawnFoot1.X,source.SpawnFoot1.Y);
            boxes=new MapBox[source.BoxCount];for(int i=0;i<boxes.Length;i++)boxes[i]=MapBox.From(source.GetBox(i),i);
            theme=meta.Theme;light=meta.Light;ceiling=meta.Ceiling;shadow=meta.Shadow;
        }
        public void SaveGeometry(MapBox[] ordered,Vector2 p0,Vector2 p1)
        {
            if(preservedBaseline)throw new InvalidOperationException("기존 Brawl은 보호됩니다. 사본을 편집해주세요.");
            boxes=Array.ConvertAll(ordered,b=>b.Copy());spawn0=p0;spawn1=p1;revision++;
        }
        public void SaveLayout(MapBox[] ordered,MapPhysicalBox[] physics,Vector2 p0,Vector2 p1)
        {
            SaveLayout(ordered,physics,ropes??Array.Empty<MapRope>(),p0,p1);
        }
        public void SaveLayout(MapBox[] ordered,MapPhysicalBox[] physics,MapRope[] connections,Vector2 p0,Vector2 p1)
        {
            if(connections==null)throw new ArgumentNullException(nameof(connections));
            if(physics==null)throw new ArgumentNullException(nameof(physics));
            SaveGeometry(ordered,p0,p1);
            physicalBoxes=(MapPhysicalBox[])physics.Clone();ropes=(MapRope[])connections.Clone();
        }
        /// <summary>보상 맵 배치 저장: 비석(화면 왼쪽부터 정렬)과 패자 자리. 지형 배치(SaveLayout)와 별개다.</summary>
        public void SaveReward(bool isReward,RewardStele[] ordered,Vector2 stand)
        {
            if(preservedBaseline)throw new InvalidOperationException("기존 Brawl은 보호됩니다. 사본을 편집해주세요.");
            rewardMap=isReward;
            var copy=Array.ConvertAll(ordered??Array.Empty<RewardStele>(),x=>x.Copy());
            Array.Sort(copy,(a,b)=>a.Bounds.center.x.CompareTo(b.Bounds.center.x));
            steles=copy;loserStand=stand;
        }
        public bool SameReward(MapDefinition other)
        {
            if(other==null||other.rewardMap!=rewardMap||other.SteleCount!=SteleCount||other.loserStand!=loserStand)return false;
            for(int i=0;i<SteleCount;i++)
            {
                var a=steles[i].vertices;var b=other.steles[i].vertices;
                if(a.Length!=b.Length)return false;
                for(int k=0;k<a.Length;k++)if(a[k]!=b[k])return false;
            }
            return true;
        }
        public void MakeEditableCopy(string newId,string title){stableId=newId;displayName=title;preservedBaseline=false;revision=1;}
#endif
    }
}
