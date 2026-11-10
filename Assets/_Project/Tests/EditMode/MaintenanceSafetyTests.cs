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

        // Validation 기준 맵의 지형·몸체·로프 해시. 2026-11-11 HEAD 60d605a(맵 파일 무변경)에서 기록했고,
        // CP3·CP4 여섯 값은 PROGRESS의 당시 기록과 같다. 맵을 의도적으로 바꾸려면 사용자 승인 후 같은 변경에서 값과 기록을 함께 고친다.
        [TestCase("CP2_1_Adjacent_Stack",0x4616BEA6u)] [TestCase("CP2_1_Pass_Through_Adjacent",0x8184DA82u)]
        [TestCase("CP2_1_Pass_Through_Single",0xBFFB6F2Au)] [TestCase("CP2_1_Single_Stack",0x9A173423u)]
        [TestCase("CP3_Connector_Body_Trial",0x4146C65Du)] [TestCase("CP3_Rope_Connector_Trial",0xD6A9CE11u)]
        [TestCase("CP3_Rope_Fixed",0x65D57D92u)] [TestCase("CP3_Rope_Graph_Trial",0xFBE64AA0u)]
        [TestCase("CP3_Rope_Pull",0x3A040F7Du)] [TestCase("CP4_Cut_Rope_Trial",0xF3262F09u)]
        [TestCase("CP5_Integrated_Trial",0x7D89F13Cu)]
        public void ValidationReferenceMaps_KeepRecordedGeometry(string name,uint expected)
        {
            var d=UnityEditor.AssetDatabase.LoadAssetAtPath<Knockbox.Game.Maps.MapDefinition>(Knockbox.EditorTools.MapAuthoringOperations.ReferenceFolder+name+".asset");
            Assert.NotNull(d,name);
            Assert.AreEqual(expected.ToString("X8"),d.ToArena().ComputeGeometryChecksum().ToString("X8"),name+" 기준 맵이 바뀌었습니다. 의도한 변경인지 확인하세요.");
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
