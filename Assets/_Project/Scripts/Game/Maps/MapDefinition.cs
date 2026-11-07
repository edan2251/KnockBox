using System;
using System.Collections.Generic;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game.Maps
{
    [Serializable]
    public struct MapBox
    {
        public string id;
        public TerrainKind kind;
        public float minX, minY, maxX, maxY;
        public ArenaBox ToBox() => new ArenaBox(kind,minX,minY,maxX,maxY);
        public static MapBox From(ArenaBox b,int order) => new MapBox {id="box-"+order.ToString("D3"),
            kind=b.Kind,minX=b.MinX,minY=b.MinY,maxX=b.MaxX,maxY=b.MaxY};
    }

    /// <summary>Authoritative ordered static geometry. Conversion is lossless; no Unity physics or implicit snapping.</summary>
    [CreateAssetMenu(menuName="Knockbox/맵/맵 정의")]
    public sealed class MapDefinition : ScriptableObject
    {
        public const int SchemaVersion=1;
        [SerializeField] string stableId;
        [SerializeField] string displayName;
        [SerializeField] bool preservedBaseline;
        [SerializeField] int revision=1;
        [SerializeField] int columns=36,rows=20;
        [SerializeField] bool boundary;
        [SerializeField] Vector2 spawn0,spawn1;
        [SerializeField] MapBox[] boxes=Array.Empty<MapBox>();
        [SerializeField,Range(0,2)] int theme=2;
        [SerializeField] Knockbox.Presentation.MapThemeSettings mapTheme = new Knockbox.Presentation.MapThemeSettings();
        [SerializeField] Vector2 light=new Vector2(0,25);
        [SerializeField] bool ceiling=true;
        [SerializeField,Range(0,1)] float shadow=.65f;
        public string Id=>stableId;
        public string DisplayName=>displayName;
        public string MatchName=>preservedBaseline?"Brawl":"map:"+stableId;
        public bool PreservedBaseline=>preservedBaseline;
        public int Revision=>revision;
        public int Columns=>columns;
        public int Rows=>rows;
        public int BoxCount=>boxes.Length;
        public MapBox Box(int index)=>boxes[index];
        public Vector2 Spawn(int player)=>player==0?spawn0:spawn1;
        public ArenaMeta Metadata()=>new ArenaMeta{Theme=theme,HasLight=true,Light=light,Ceiling=ceiling,Shadow=shadow,Rotation=false,MapTheme=mapTheme};

        public ArenaDef ToArena()
        {
            var errors=Validate();
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            var ordered=new ArenaBox[boxes.Length];
            for(int i=0;i<ordered.Length;i++)ordered[i]=boxes[i].ToBox();
            return new ArenaDef(MatchName,columns,rows,boundary,ordered,new Vec2(spawn0.x,spawn0.y),new Vec2(spawn1.x,spawn1.y));
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
                if(!Enum.IsDefined(typeof(TerrainKind),b.kind)||b.kind==TerrainKind.Prop)errors.Add("현재 제작기는 고정 지형만 지원합니다. 순서: "+i);
            }
            for(int p=0;p<2;p++)
            {
                var s=Spawn(p);float h=bodySize*.5f;
                if(!Finite(s.x)||!Finite(s.y)||Mathf.Abs(s.x)+h>columns*.5f||s.y< -rows*.5f||s.y+bodySize>rows*.5f)errors.Add("P"+(p+1)+" 시작 위치가 경기장 밖입니다.");
                foreach(var b in boxes)
                    if(b.kind!=TerrainKind.OneWay && s.x+h>b.minX+.0001f && s.x-h<b.maxX-.0001f && s.y+bodySize>b.minY+.0001f && s.y<b.maxY-.0001f)
                    { errors.Add("P"+(p+1)+" 시작 위치가 지형과 겹칩니다: "+b.id);break; }
            }
            if(Vector2.Distance(spawn0,spawn1)<bodySize)errors.Add("양쪽 시작 위치가 겹칩니다.");
            return errors;
        }
        public static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
#if UNITY_EDITOR
        public void Import(ArenaDef source,ArenaMeta meta,string id,string title,bool baseline=false)
        {
            if(source.PropCount!=0)throw new NotSupportedException("움직이는 구조물 제작은 맵 호환 규격 확장 후 지원합니다.");
            // Definitions own their serialized theme; importing must not alias another map's settings.
            mapTheme=meta.MapTheme==null?new Knockbox.Presentation.MapThemeSettings()
                :JsonUtility.FromJson<Knockbox.Presentation.MapThemeSettings>(JsonUtility.ToJson(meta.MapTheme));
            stableId=id;displayName=title;preservedBaseline=baseline;revision=1;
            columns=source.Columns;rows=source.Rows;boundary=source.HasBoundary;
            spawn0=new Vector2(source.SpawnFoot0.X,source.SpawnFoot0.Y);spawn1=new Vector2(source.SpawnFoot1.X,source.SpawnFoot1.Y);
            boxes=new MapBox[source.BoxCount];for(int i=0;i<boxes.Length;i++)boxes[i]=MapBox.From(source.GetBox(i),i);
            theme=meta.Theme;light=meta.Light;ceiling=meta.Ceiling;shadow=meta.Shadow;
        }
        public void SaveGeometry(MapBox[] ordered,Vector2 p0,Vector2 p1)
        {
            if(preservedBaseline)throw new InvalidOperationException("기존 Brawl은 보호됩니다. 사본을 편집해주세요.");
            boxes=(MapBox[])ordered.Clone();spawn0=p0;spawn1=p1;revision++;
        }
        public void MakeEditableCopy(string newId,string title){stableId=newId;displayName=title;preservedBaseline=false;revision=1;}
#endif
    }
}
