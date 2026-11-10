namespace Knockbox.Simulation
{
    public static partial class PropPhysics
    {
        static Vec2 PolygonBoxVertex(in PropBody b,Vec2 half,int i)
            =>b.ToWorld(new Vec2(i==0||i==3?-half.X:half.X,i<2?-half.Y:half.Y));
        static Vec2 PolygonBoxNormal(in PropBody b,int i)
        {var u=b.Rotation;return i==0?new Vec2(u.Y,-u.X):i==1?u:i==2?new Vec2(-u.Y,u.X):-u;}

        // Same SAT + two clipped contact solver as rectangular terrain, without per-tick allocation.
        static void CollidePolygon(int body,in PropBody b,Vec2 half,int terrain,ConvexPolygon polygon,float speculative,PropScratch scratch)
        {
            int polyFace=0,bodyFace=0;float polySep=float.NegativeInfinity,bodySep=float.NegativeInfinity;
            for(int f=0;f<polygon.Count;f++)
            {
                var n=polygon.Normal(f);float nearest=float.MaxValue;
                for(int v=0;v<4;v++)nearest=SimMath.Min(nearest,Vec2.Dot(n,PolygonBoxVertex(b,half,v)-polygon.Point(f)));
                if(nearest>speculative)return;
                if(nearest>polySep+1e-6f){polySep=nearest;polyFace=f;}
            }
            for(int f=0;f<4;f++)
            {
                var n=PolygonBoxNormal(b,f);var point=PolygonBoxVertex(b,half,f);float nearest=float.MaxValue;
                for(int v=0;v<polygon.Count;v++)nearest=SimMath.Min(nearest,Vec2.Dot(n,polygon.Point(v)-point));
                if(nearest>speculative)return;
                if(nearest>bodySep+1e-6f){bodySep=nearest;bodyFace=f;}
            }
            bool bodyReference=bodySep>polySep+ReferenceTolerance;int face=bodyReference?bodyFace:polyFace;
            var normal=bodyReference?PolygonBoxNormal(b,face):polygon.Normal(face);
            var start=bodyReference?PolygonBoxVertex(b,half,face):polygon.Point(face);
            var end=bodyReference?PolygonBoxVertex(b,half,(face+1)%4):polygon.Point((face+1)%polygon.Count);
            var tangent=(end-start)/SimMath.Sqrt((end-start).LengthSquared);
            float length=SimMath.Sqrt((end-start).LengthSquared);
            int incident=0,count=bodyReference?polygon.Count:4;float dot=float.MaxValue;
            for(int f=0;f<count;f++)
            {
                float facing=Vec2.Dot(normal,bodyReference?polygon.Normal(f):PolygonBoxNormal(b,f));
                if(facing<dot-1e-6f){dot=facing;incident=f;}
            }
            var a=bodyReference?polygon.Point(incident):PolygonBoxVertex(b,half,incident);
            var z=bodyReference?polygon.Point((incident+1)%count):PolygonBoxVertex(b,half,(incident+1)%count);
            float s0=Vec2.Dot(a-start,tangent),s1=Vec2.Dot(z-start,tangent);int e0=0,e1=1;
            if(s0>s1){(a,z)=(z,a);(s0,s1)=(s1,s0);e0=1;e1=0;}
            var outNormal=bodyReference?-normal:normal;
            // Distinct contact-key range: keep every old warm-start key unchanged.
            int feature=0x1000000+terrain*8192+(bodyReference?4096:0)+face*128+incident*2;
            if(s1<0||s0>length)
            {AddPolygonPoint(scratch,body,feature+(s1<0?e1:e0),normal,outNormal,start,s1<0?z:a,speculative);return;}
            var p0=s0<0?a+(z-a)*(-s0/(s1-s0)):a;
            var p1=s1>length?z+(a-z)*((s1-length)/(s1-s0)):z;
            int before=scratch.ContactCount;
            AddPolygonPoint(scratch,body,feature+e0,normal,outNormal,start,p0,speculative);
            if((p1-p0).LengthSquared>1e-10f)AddPolygonPoint(scratch,body,feature+e1,normal,outNormal,start,p1,speculative);
            if(scratch.ContactCount==before+2)scratch.Contacts[before].Twin=true;
        }
        static void AddPolygonPoint(PropScratch scratch,int body,int id,Vec2 n,Vec2 outNormal,Vec2 origin,Vec2 point,float speculative)
        {
            float separation=Vec2.Dot(point-origin,n);if(separation>speculative)return;
            Add(scratch,body,-1,id,outNormal,point-n*(.5f*separation),separation,-1);
        }
    }
}
