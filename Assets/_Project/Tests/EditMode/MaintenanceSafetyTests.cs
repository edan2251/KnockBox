using System.Reflection;
using Knockbox.Game;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    public class MaintenanceSafetyTests
    {
        [Test]
        public void BrawlLegacyMapHash_RemainsRecordedBaseline()
        {
            Assert.AreEqual(0x483E0206u, ArenaFiles.LoadByName("Brawl").ComputeChecksum());
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void GeometrySchema2DetectsDynamicPositionAndSize_WithoutRewritingLegacyHash(int field)
        {
            var p = new PropDef { Kind=PropKind.LooseBox, Center=new Vec2(0,4), HalfSize=new Vec2(.5f,.5f), AnchorProp=-1 };
            var q=p;
            if(field==0)q.Center.X+=.25f;
            if(field==1)q.Center.Y+=.25f;
            if(field==2)q.HalfSize.X+=.25f;
            if(field==3)q.HalfSize.Y+=.25f;
            var terrain=ArenaFiles.LoadByName("Brawl");
            var a=terrain.WithProps(new[]{p},3);var b=terrain.WithProps(new[]{q},3);
            Assert.AreEqual(0xF3DC4C05u,a.ComputeChecksum());
            Assert.AreEqual(a.ComputeChecksum(),b.ComputeChecksum(),"Historical schema remains unchanged, including its documented gap.");
            Assert.AreNotEqual(a.ComputeGeometryChecksum(),b.ComputeGeometryChecksum());
        }

        [TestCase("online")] [TestCase("recording")] [TestCase("local")]
        public void RoundTransitionTuning_IsFrozenForNetworkAndRecordings(string mode)
        {
            var go=new GameObject("Tuning freeze");go.SetActive(false);
            var tuning=ScriptableObject.CreateInstance<MovementTuning>();
            try
            {
                var r=go.AddComponent<MatchRunner>();r.Tuning=tuning;r.Rules=RuleSet.Brawl;r.BrawlExperiment=true;
                r.ArenaName="Brawl";r.RandomizeSeed=false;r.OnlineRematch=mode=="online";
                r.StartNewMatch();
                if(mode=="recording"){r.StartRecording(r.World.Config.Rules.Format());Assert.IsNotNull(r.Recording);}
                uint before=r.World.Config.ComputeChecksum();
                tuning.Movement.BodySize+=.125f;
                r.World.State.Match.Phase=MatchPhase.RoundTransition;
                typeof(MatchRunner).GetMethod("ApplyTuningAtRoundTransition",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(r,null);
                if(mode=="local")Assert.AreNotEqual(before,r.World.Config.ComputeChecksum());
                else Assert.AreEqual(before,r.World.Config.ComputeChecksum());
            }
            finally { Object.DestroyImmediate(go);Object.DestroyImmediate(tuning); }
        }
    }
}
