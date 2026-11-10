using System;

namespace Knockbox.Simulation
{
    /// <summary>Immutable counter-clockwise convex terrain. All queries use the same saved vertices.</summary>
    public sealed class ConvexPolygon
    {
        public const int MaxVertices=32;
        readonly Vec2[] points,normals;
        public int Count=>points.Length;
        public Vec2 Min { get; }
        public Vec2 Max { get; }
        public Vec2 Point(int i)=>points[i];
        public Vec2 Normal(int i)=>normals[i];
        public Vec2[] CopyPoints()=>(Vec2[])points.Clone();
        public ConvexPolygon(Vec2[] source)
        {
            if(source==null||source.Length<3||source.Length>MaxVertices)throw new ArgumentException("볼록 지형 꼭짓점은 3~32개여야 합니다.");
            points=(Vec2[])source.Clone();normals=new Vec2[Count];
            var min=new Vec2(float.MaxValue,float.MaxValue);var max=-min;
            for(int i=0;i<Count;i++)
            {
                var a=points[i];var d=points[(i+1)%Count]-a;
                if(!Finite(a.X)||!Finite(a.Y)||d.LengthSquared<1e-8f)throw new ArgumentException("겹치거나 잘못된 꼭짓점입니다.");
                min.X=SimMath.Min(min.X,a.X);min.Y=SimMath.Min(min.Y,a.Y);max.X=SimMath.Max(max.X,a.X);max.Y=SimMath.Max(max.Y,a.Y);
                normals[i]=new Vec2(d.Y,-d.X)/SimMath.Sqrt(d.LengthSquared);
                // Every other vertex must be strictly on the inside of each directed edge.
                for(int j=0;j<Count;j++)if(j!=i&&j!=(i+1)%Count&&Cross(d,points[j]-a)<=1e-7f)
                    throw new ArgumentException("반시계 방향의 볼록 지형만 저장할 수 있습니다. 겹친 변·오목한 모양은 지원하지 않습니다.");
            }
            Min=min;Max=max;
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        public static float Cross(Vec2 a,Vec2 b)=>a.X*b.Y-a.Y*b.X;

        /// <summary>Exact axis-aligned box Minkowski slice. Bounds add the box's own separating axes.</summary>
        public bool Slice(Vec2 center,Vec2 half,bool vertical,out float low,out float high,out Vec2 lowNormal,out Vec2 highNormal)
        {
            low=vertical?Min.Y-half.Y:Min.X-half.X;high=vertical?Max.Y+half.Y:Max.X+half.X;
            lowNormal=vertical?new Vec2(0,-1):new Vec2(-1,0);highNormal=-lowNormal;
            float fixedValue=vertical?center.X:center.Y;
            if(fixedValue<(vertical?Min.X-half.X:Min.Y-half.Y)-1e-6f||fixedValue>(vertical?Max.X+half.X:Max.Y+half.Y)+1e-6f)return false;
            for(int i=0;i<Count;i++)
            {
                var n=normals[i];float c=Vec2.Dot(n,points[i])+SimMath.Abs(n.X)*half.X+SimMath.Abs(n.Y)*half.Y;
                float axis=vertical?n.Y:n.X;float remainder=c-(vertical?n.X:n.Y)*fixedValue;
                if(SimMath.Abs(axis)<1e-7f){if(remainder< -1e-6f)return false;continue;}
                float limit=remainder/axis;
                if(axis>0&&limit<high){high=limit;highNormal=n;}
                if(axis<0&&limit>low){low=limit;lowNormal=n;}
                if(low>high+1e-6f)return false;
            }
            return true;
        }
        public bool OverlapsBox(Vec2 center,Vec2 half,float epsilon=1e-4f)
        {
            if(center.X+half.X<=Min.X+epsilon||center.X-half.X>=Max.X-epsilon||center.Y+half.Y<=Min.Y+epsilon||center.Y-half.Y>=Max.Y-epsilon)return false;
            for(int i=0;i<Count;i++)
                if(Vec2.Dot(normals[i],center-points[i])-SimMath.Abs(normals[i].X)*half.X-SimMath.Abs(normals[i].Y)*half.Y>=-epsilon)return false;
            return true;
        }
        public bool Contains(Vec2 p,float epsilon=0)
        {for(int i=0;i<Count;i++)if(Vec2.Dot(normals[i],p-points[i])>epsilon)return false;return true;}
        public Vec2 ClosestBoundary(Vec2 p)
        {
            Vec2 nearest=points[0];float best=float.MaxValue;
            for(int i=0;i<Count;i++)
            {
                var edge=points[(i+1)%Count]-points[i];float t=SimMath.Clamp(Vec2.Dot(p-points[i],edge)/edge.LengthSquared,0,1);
                var q=points[i]+edge*t;float d=(p-q).LengthSquared;if(d<best){best=d;nearest=q;}
            }
            return nearest;
        }
        public bool SeparatedFrom(in ArenaBox other,float margin)
        {
            for(int i=0;i<Count;i++)
            {
                float nearest=float.MaxValue;
                if(other.IsPolygon)
                    for(int j=0;j<other.Polygon.Count;j++)nearest=SimMath.Min(nearest,Vec2.Dot(normals[i],other.Polygon.Point(j)-points[i]));
                else
                {
                    var v=new Vec2(normals[i].X>=0?other.MinX:other.MaxX,normals[i].Y>=0?other.MinY:other.MaxY);
                    nearest=Vec2.Dot(normals[i],v-points[i]);
                }
                if(nearest>margin)return true;
            }
            return false;
        }
        public bool SegmentHits(Vec2 a,Vec2 b)
        {
            float low=0,high=1;var delta=b-a;
            for(int i=0;i<Count;i++)
            {
                var n=normals[i];float distance=Vec2.Dot(n,points[i]-a),speed=Vec2.Dot(n,delta);
                if(SimMath.Abs(speed)<1e-8f){if(distance<0)return false;continue;}
                float t=distance/speed;
                if(speed>0)high=SimMath.Min(high,t);else low=SimMath.Max(low,t);
                if(high<=low+1e-6f)return false;
            }
            return true;
        }
    }
}
