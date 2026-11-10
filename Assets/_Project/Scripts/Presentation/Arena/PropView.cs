using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 구조물 그리기(09-props-physics.md 6절, 08-visual-direction.md 2절). 테마의 `prop` 색 단색, 줄은 공통 짙은 회색,
    /// 축과 경첩은 작은 점. 상자는 별도 구분선 없이 상자마다 밝기 ±6% 이내로 구분한다.
    /// 아직 물리가 없는 구조물(임시 물체)은 맵을 그릴 때 한 번 놓고, 강체 구조물(CP14부터)은 매 프레임 직전·현재 상태를
    /// 보간해(회전은 (cos, sin)을 보간해 정규화) 움직이며 줄이 따라간다.
    /// </summary>
    public sealed class PropView : MonoBehaviour
    {
        public const float RopeWidth = 0.12f;
        public static Color ConnectorColor => PaletteAsset.ConnectorBoxColor;
        const float PivotSize = 0.24f;
        /// <summary>상자 밝기 차의 최대 비율(08 2절: ±6% 이내).</summary>
        public const float BoxShadeRange = 0.06f;
        const float PivotDarkness = 0.6f;
        public static Color PassThroughColor => PaletteAsset.PassThroughPropColor;
        public static Color BoxColor(Color prop, int index, bool playerPassThrough) =>
            playerPassThrough ? PassThroughColor : BoxShade(prop, index);

        ArenaDef current;
        string currentTheme;
        readonly SpriteRenderer[] bodies = new SpriteRenderer[PropState.Capacity];
        readonly SpriteRenderer[] ropes = new SpriteRenderer[PropState.Capacity];
        readonly SpriteRenderer[][] authoredRopes = new SpriteRenderer[PropState.Capacity][];
        readonly SpriteRenderer[][] connectors = new SpriteRenderer[PropState.Capacity][];
        readonly Vector2[] ropePoints = new Vector2[RopePath.Segments+1];
        int dynamicCount;
        SpriteRenderer[][] graphLines=System.Array.Empty<SpriteRenderer[]>();
        readonly float[] touchLeft = new float[PropState.Capacity];
        readonly float[] touchCooldown = new float[PropState.Capacity];
        readonly Vector3[] bodyScale = new Vector3[PropState.Capacity];
        readonly SpriteRenderer[][] nodeOutline = new SpriteRenderer[PropState.Capacity][];
        float[] releaseAge = System.Array.Empty<float>();
        int[] releasedNode = System.Array.Empty<int>();
        Vector2[] releasedPoint = System.Array.Empty<Vector2>();
        Color contactFlash;
        const float TouchDuration = .14f;
        const float RopeReleaseDuration = .16f;

        public void ResetFeedback()
        {
            System.Array.Clear(touchLeft, 0, touchLeft.Length);
            System.Array.Clear(touchCooldown, 0, touchCooldown.Length);
            for (int i = 0; i < releaseAge.Length; i++) releaseAge[i] = RopeReleaseDuration;
        }

        public void TickFeedback(float dt)
        {
            for (int i = 0; i < dynamicCount; i++)
            {
                touchLeft[i] = Mathf.Max(0, touchLeft[i] - dt);
                touchCooldown[i] = Mathf.Max(0, touchCooldown[i] - dt);
            }
            for (int i = 0; i < releaseAge.Length; i++) releaseAge[i] += dt;
        }

        // Only presentation timers are changed; repeated resting contact cannot spam audio/camera cues.
        public bool OnConnectorEvent(in SimEvent e, in PropState state, Color flash)
        {
            int b = e.PropIndex;
            if (current == null || b < 0 || b >= dynamicCount) return false;
            if (e.Type == SimEventType.ConnectorContact)
            {
                if (state.IsDestroyed(b) || touchCooldown[b] > 0) return false;
                contactFlash = flash;
                touchLeft[b] = TouchDuration; touchCooldown[b] = .18f; return true;
            }
            if (e.Type != SimEventType.ConnectorBreak) return false;
            for (int i = 0; i < current.GraphRopeCount; i++)
            {
                ref readonly var edge = ref current.GetGraphRope(i);
                if (edge.Body != b && edge.Joint.OtherBody != b) continue;
                releasedNode[i] = b; releaseAge[i] = 0;
                var local = edge.Body == b ? edge.Joint.LocalAnchor : edge.Joint.OtherLocal;
                releasedPoint[i] = ToVector(PropState.At(state, b).ToWorld(local));
            }
            return true;
        }

        /// <summary>강체 구조물 i가 지금 그려진 위치·회전(그림자, 테스트·캡처 확인용).</summary>
        public Vector2 BodyPosition(int body) => bodies[body].transform.localPosition;
        public Transform BodyTransform(int body) => bodies[body].transform;
        readonly Vector2[] connectorPositions=new Vector2[PropState.Capacity];
        public Vector2 ConnectorPosition(int body) => connectorPositions[body];
        public bool ConnectorVisible(int body) => connectors[body]!=null && connectors[body][0].enabled;

        public void Build(ArenaDef arena, ArenaTheme theme, ShapeLibrary shapes)
        {
            if (arena == current && theme.name == currentTheme)
                return;
            current = arena;
            currentTheme = theme.name;
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            // 임시 물체(아직 움직이지 않는 단계의 구조물).
            for (int box = arena.PropBoxStart; box < arena.BoxCount; box++)
            {
                int i = arena.PropOfBox(box);
                ref readonly var p = ref arena.GetProp(i);
                bool isBox = p.Kind == PropKind.BoxPillar || p.Kind == PropKind.LooseBox;
                var color = isBox ? BoxColor(theme.prop, i, p.PlayerPassThrough) : theme.prop;
                Rect(shapes, color, 3, p.MinX, p.MinY, p.MaxX, p.MaxY);
                if (p.HangsByRope)
                    Line(Rope(shapes), ToVector(p.Anchor), new Vector2(p.Center.X, p.MaxY));
                else if (p.HasAnchor)
                    Dot(shapes, PaletteAsset.Scaled(theme.prop, PivotDarkness), 5, ToVector(p.Anchor), PivotSize);

            }

            // 강체 구조물: 몸 스프라이트(가운데 기준, 회전), 줄 또는 축 점.
            graphLines=new SpriteRenderer[arena.GraphRopeCount][];
            releaseAge = new float[arena.GraphRopeCount];
            releasedNode = new int[arena.GraphRopeCount];
            releasedPoint = new Vector2[arena.GraphRopeCount];
            ResetFeedback();
            for(int i=0;i<graphLines.Length;i++)
            {
                graphLines[i]=new SpriteRenderer[RopePath.Segments];
                for(int s=0;s<RopePath.Segments;s++)graphLines[i][s]=Rope(shapes);
            }
            dynamicCount = arena.DynamicPropCount;
            for (int b = 0; b < dynamicCount; b++)
            {
                ref readonly var p = ref arena.GetProp(arena.DynamicProp(b));
                ref readonly var joint = ref arena.GetJoint(b);
                bool isBox = p.Kind == PropKind.BoxPillar || p.Kind == PropKind.LooseBox;
                bodies[b] = ShapeLibrary.Create(transform, "PropBody", shapes.Square, SortingLayers.Terrain, 3,
                    isBox ? BoxColor(theme.prop, arena.DynamicProp(b), p.PlayerPassThrough) : theme.prop);
                ShapeLibrary.SetSize(bodies[b], p.HalfSize.X * 2f, p.HalfSize.Y * 2f);
                bodies[b].transform.localPosition = ToVector(p.Center);
                ConnectorBoxVisual.Apply(bodies[b],shapes,p.IsConnector,ToVector(p.HalfSize)*2);
                bodyScale[b] = bodies[b].transform.localScale;
                nodeOutline[b] = p.IsConnector ? bodies[b].GetComponentsInChildren<SpriteRenderer>() : null;
                ropes[b] = joint.Kind == PropJointKind.Rope ? Rope(shapes) : null;
                authoredRopes[b]=null;connectors[b]=null;
                if(joint.RopeMode==RopeBreakMode.Connector)
                {
                    connectors[b]=new SpriteRenderer[4];
                    for(int edge=0;edge<4;edge++)connectors[b][edge]=ShapeLibrary.Create(transform,"Rope_Connector_"+b+"_"+edge,
                        shapes.Square,SortingLayers.Terrain,6,ConnectorColor);
                }
                if(joint.UsesTension)
                {
                    var segments=authoredRopes[b]=new SpriteRenderer[RopePath.Segments];
                    segments[0]=ropes[b];for(int i=1;i<segments.Length;i++)segments[i]=Rope(shapes);
                    DrawAuthoredRope(b,ToVector(joint.WorldAnchor),ToVector(p.Center+joint.LocalAnchor),joint.Length,true);
                    Dot(shapes,theme.prop,4,ToVector(joint.WorldAnchor),.16f);
                }
                if (joint.Pinned)
                    Dot(shapes, PaletteAsset.Scaled(theme.prop, PivotDarkness), 5, ToVector(joint.WorldAnchor), PivotSize);
            }
        }

        void DrawGraph(in PropState before,in PropState now,float alpha)
        {
            for(int i=0;i<current.GraphRopeCount;i++)
            {
                ref readonly var r=ref current.GetGraphRope(i);bool visible=r.Active(now);
                foreach(var sr in graphLines[i])sr.enabled=visible;
                if (!visible)
                {
                    if (releaseAge[i] < RopeReleaseDuration)
                    {
                        int other = r.Body == releasedNode[i] ? r.Joint.OtherBody : r.Body;
                        var anchor = other < 0 ? ToVector(r.Joint.WorldAnchor)
                            : GraphPoint(before,now,other,other == r.Body ? r.Joint.LocalAnchor : r.Joint.OtherLocal,alpha);
                        float t = Mathf.Clamp01(releaseAge[i] / RopeReleaseDuration);
                        float pull = 1 - (1-t)*(1-t)*(1-t);
                        var tail = Vector2.Lerp(releasedPoint[i], anchor, pull);
                        RopePath.Fill(anchor, tail, Vector2.Distance(anchor,tail), ropePoints);
                        for(int k=0;k<RopePath.Segments;k++)
                        {
                            var sr=graphLines[i][k];sr.enabled=true;Line(sr,ropePoints[k],ropePoints[k+1]);
                            var color=sr.color;color.a=1-t;sr.color=color;
                        }
                    }
                    continue;
                }
                foreach(var sr in graphLines[i]) { var color=sr.color;color.a=1;sr.color=color; }
                var start=ToVector(r.Joint.WorldAnchor);
                if(r.Joint.OtherBody>=0)start=GraphPoint(before,now,r.Joint.OtherBody,r.Joint.OtherLocal,alpha);
                var end=GraphPoint(before,now,r.Body,r.Joint.LocalAnchor,alpha);
                RopePath.Fill(start,end,r.Joint.Length,ropePoints);
                for(int s=0;s<RopePath.Segments;s++)Line(graphLines[i][s],ropePoints[s],ropePoints[s+1]);
            }
        }
        static Vector2 GraphPoint(in PropState previous,in PropState now,int body,Vec2 local,float alpha)
        {
            var b=PropState.At(now,body);var a=body<previous.Count?PropState.At(previous,body):b;
            Interpolate(a,b,alpha,out var p,out var q);return p+Rotate(q,local);
        }

        /// <summary>강체 구조물을 직전·현재 상태 사이로 보간해 놓는다(연출 시간, 할당 없음).</summary>
        public void Render(in PropState previous, in PropState state, float alpha)
        {
            if (current == null)
                return;
            DrawGraph(previous,state,alpha);
            int count = Mathf.Min(dynamicCount, state.Count);
            for (int b = 0; b < count; b++)
            {
                bodies[b].gameObject.SetActive(!state.IsDestroyed(b));
                ref readonly var now = ref PropState.At(in state, b);
                var before = b < previous.Count ? PropState.At(in previous, b) : now;
                Interpolate(before, now, alpha, out var position, out var rotation);
                var tr = bodies[b].transform;
                tr.localPosition = position;
                float touch = touchLeft[b] / TouchDuration;
                tr.localScale = bodyScale[b] * (1 + .07f * Mathf.Sin(touch * Mathf.PI) * touch);
                if (nodeOutline[b] != null)
                    foreach (var sr in nodeOutline[b]) if (sr != bodies[b])
                        sr.color = Color.Lerp(ConnectorColor, contactFlash, touch * touch);
                tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(rotation.y, rotation.x) * Mathf.Rad2Deg);
                if (ropes[b] != null)
                {
                    ref readonly var joint = ref current.GetJoint(b);
                    // 끊어진 줄은 그리지 않는다(CP15).
                    ropes[b].enabled = now.Broken == 0;
                    if(authoredRopes[b]!=null)foreach(var segment in authoredRopes[b])segment.enabled=now.Broken==0;
                    if(connectors[b]!=null)foreach(var edge in connectors[b])edge.enabled=now.Broken==0;
                    if (now.Broken != 0)
                        continue;
                    var end = position + Rotate(rotation, joint.LocalAnchor);
                    var start = ToVector(joint.WorldAnchor);
                    if (joint.OtherBody >= 0)
                    {
                        // 다른 몸에 이어진 줄(연결 구조): 그 몸이 지금 그려진 자리에서 시작.
                        var other = bodies[joint.OtherBody].transform;
                        float angle = other.localEulerAngles.z * Mathf.Deg2Rad;
                        start = (Vector2)other.localPosition + Rotate(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), joint.OtherLocal);
                    }
                    if(joint.UsesTension)DrawAuthoredRope(b,start,end,joint.Length,true);
                    else Line(ropes[b], start, end);
                }
            }
        }

        void DrawAuthoredRope(int body,Vector2 start,Vector2 end,float length,bool enabled)
        {
            RopePath.Fill(start,end,length,ropePoints);
            for(int i=0;i<RopePath.Segments;i++)
            {
                var sr=authoredRopes[body][i];sr.enabled=enabled;Line(sr,ropePoints[i],ropePoints[i+1]);
            }
            if(connectors[body]==null)return;
            ref readonly var joint=ref current.GetJoint(body);
            var p=RopeGeometry.Point(new Vec2(start.x,start.y),new Vec2(end.x,end.y),length,joint.ConnectorPosition);
            var center=connectorPositions[body]=new Vector2(p.X,p.Y);
            float size=joint.ConnectorSize,half=size*.5f,thickness=Mathf.Min(.08f,size*.18f);
            for(int edge=0;edge<4;edge++)
            {
                var sr=connectors[body][edge];sr.enabled=enabled;
                bool horizontal=edge<2;float sign=(edge&1)==0?-1f:1f;
                sr.transform.localPosition=center+(horizontal?Vector2.up:Vector2.right)*(sign*(half-thickness*.5f));
                ShapeLibrary.SetSize(sr,horizontal?size:thickness,horizontal?thickness:size);
            }
        }

        /// <summary>위치는 선형 보간, 회전은 (cos, sin)을 보간해 정규화(09 6절).</summary>
        public static void Interpolate(in PropBody a, in PropBody b, float t, out Vector2 position, out Vector2 rotation)
        {
            position = Vector2.LerpUnclamped(ToVector(a.Position), ToVector(b.Position), t);
            rotation = Vector2.LerpUnclamped(ToVector(a.Rotation), ToVector(b.Rotation), t);
            float length = rotation.magnitude;
            rotation = length > 1e-5f ? rotation / length : Vector2.right;
        }

        /// <summary>상자마다 정해진 밝기 배율(1 ± 0.06 이내). 번호로 정하므로 매번 같다.</summary>
        public static Color BoxShade(Color prop, int index)
        {
            float k = ((index * 37 + 11) % 13) / 12f * 2f - 1f;
            return PaletteAsset.Scaled(prop, 1f + BoxShadeRange * k);
        }

        static Vector2 ToVector(Vec2 v) => new Vector2(v.X, v.Y);

        static Vector2 Rotate(Vector2 q, Vec2 local) => new Vector2(q.x * local.X - q.y * local.Y, q.y * local.X + q.x * local.Y);

        void Rect(ShapeLibrary shapes, Color color, int order, float minX, float minY, float maxX, float maxY)
        {
            var sr = ShapeLibrary.Create(transform, "Prop", shapes.Square, SortingLayers.Terrain, order, color);
            sr.transform.localPosition = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
            ShapeLibrary.SetSize(sr, maxX - minX, maxY - minY);
        }

        SpriteRenderer Rope(ShapeLibrary shapes) =>
            ShapeLibrary.Create(transform, "Rope", shapes.Square, SortingLayers.Terrain, 1, PaletteAsset.RopeColor);

        static void Line(SpriteRenderer sr, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            sr.transform.localPosition = (Vector3)((from + to) * 0.5f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            ShapeLibrary.SetSize(sr, delta.magnitude, RopeWidth);
        }

        void Dot(ShapeLibrary shapes, Color color, int order, Vector2 at, float size)
        {
            var sr = ShapeLibrary.Create(transform, "Pivot", shapes.Circle, SortingLayers.Terrain, order, color);
            sr.transform.localPosition = at;
            ShapeLibrary.SetSize(sr, size, size);
        }
    }
}
