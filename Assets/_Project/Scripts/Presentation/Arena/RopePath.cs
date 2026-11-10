using Knockbox.Simulation;
using UnityEngine;
namespace Knockbox.Presentation
{
    /// <summary>표시와 연결부 판정이 공유하는 고정 분할 줄 모양.</summary>
    public static class RopePath
    {
        public const int Segments=RopeGeometry.Segments;
        public static void Fill(Vector2 start,Vector2 end,float length,Vector2[] points)
        {
            var a=new Vec2(start.x,start.y);var b=new Vec2(end.x,end.y);
            float sag=RopeGeometry.Sag(a,b,length);
            for(int i=0;i<points.Length;i++)
            {
                var p=RopeGeometry.Curve(a,b,sag,(float)i/(points.Length-1));
                points[i]=new Vector2(p.X,p.Y);
            }
        }
    }
}
