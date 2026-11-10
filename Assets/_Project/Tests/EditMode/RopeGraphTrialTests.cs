using System;
using Knockbox.Game.Maps;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Knockbox.Tests.EditMode
{
    public class RopeGraphTrialTests
    {
        [TestCase("CP3_Rope_Graph_Trial",9,12)]
        [TestCase("CP3_Connector_Body_Trial",3,4)]
        public void SuppliedUnregisteredTrialStaysIntactAndKeepsRopeLengths(string name,int bodies,int edges)
        {
            var d=AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/Validation/"+name+".asset");
            Assert.NotNull(d);Assert.IsEmpty(d.Validate());Assert.AreEqual(bodies,d.PhysicalBoxCount);Assert.AreEqual(edges,d.RopeCount);
            var a=d.ToArena();var w=RopeGraphTests.World(a);float peak=0,drift=0;
            for(int t=0;t<3600;t++)
            {
                w.Step(default,default);
                for(int b=0;b<bodies;b++)Assert.IsFalse(w.State.Props.IsDestroyed(b));
                for(int e=0;e<edges;e++)
                {
                    var r=a.GetGraphRope(e);var start=r.Joint.OtherBody<0?r.Joint.WorldAnchor:PropState.At(w.State.Props,r.Joint.OtherBody).ToWorld(r.Joint.OtherLocal);
                    float stretch=SimMath.Sqrt((PropState.At(w.State.Props,r.Body).ToWorld(r.Joint.LocalAnchor)-start).LengthSquared)-r.Joint.Length;
                    peak=Math.Max(peak,stretch);
                }
                for(int b=0;b<bodies;b++)drift=Math.Max(drift,SimMath.Sqrt((PropState.At(w.State.Props,b).Position-a.GetProp(b).Center).LengthSquared));
            }
            Assert.Less(peak,.08f);Assert.Less(drift,.15f);
            TestContext.WriteLine(name+" hash="+a.ComputeGeometryChecksum().ToString("X8")+" 3600-tick peak stretch="+peak+" drift="+drift);
        }
        [Test] public void TrialBranchKeepsOtherSupportsAfterOneNodeBreaks()
        {
            var d=AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Data/Maps/Validation/CP3_Rope_Graph_Trial.asset");
            var a=d.ToArena();var w=RopeGraphTests.World(a);ConnectorBodies.Destroy(ref w.State.Props,a,3);
            for(int e=0;e<a.GraphRopeCount;e++)Assert.AreEqual(e!=3&&e!=6,a.GetGraphRope(e).Active(w.State.Props),"edge "+e);
            for(int t=0;t<240;t++)w.Step(default,default);
            Assert.That(w.State.Props.B06.Position.Y,Is.GreaterThan(-5.2f));
            ConnectorBodies.Destroy(ref w.State.Props,a,4);ConnectorBodies.Destroy(ref w.State.Props,a,5);
            for(int t=0;t<30;t++)w.Step(default,default);
            Assert.Less(w.State.Props.B06.Position.Y,-5.3f);Assert.IsTrue(a.GetGraphRope(10).Active(w.State.Props));Assert.IsTrue(a.GetGraphRope(11).Active(w.State.Props));
        }
    }
}
