using Knockbox.Presentation;
using NUnit.Framework;

namespace Knockbox.Tests.EditMode
{
    public class NamingConventionTests
    {
        [TestCase("Terrain box 1 Solid", "Terrain_box_1_Solid")]
        [TestCase("P1 spawn FOOT", "P1_spawn_FOOT")]
        [TestCase("선택-플레이어", "선택_플레이어")]
        [TestCase("Set 0:1", "Set_0_1")]
        public void ObjectNamesKeepLettersNumbersAndUnderscores(string input,string expected)
        { Assert.AreEqual(expected,SceneObjectName.Normalize(input)); }

        [Test]
        public void HierarchyPathsKeepSeparatorsAndDynamicPlayerSuffixes()
        {
            string path="Shared VS and cards/P"+2+" name";
            Assert.AreEqual("Shared_VS_and_cards/P2_name",SceneObjectName.Path(path));
            Assert.AreEqual("Already_Clean/Body",SceneObjectName.Path("Already_Clean/Body"));
        }

        [Test]
        public void MigrationRewritesOnlyKnownNamesAndResourcePaths_NotAssemblyOrMapIdentifiers()
        {
            var plan=new Knockbox.EditorTools.ProjectNamingTools.Plan();
            plan.names.Add(new Knockbox.EditorTools.ProjectNamingTools.NameChange{from="Main and lobby",to="Main_and_lobby"});
            plan.moves.Add(new Knockbox.EditorTools.ProjectNamingTools.FileMove{from="Assets/_Project/Resources/UI/Paperlogy-4Regular.ttf",to="Assets/_Project/Resources/UI/Paperlogy_4Regular.ttf"});
            string source="Find(\"Main and lobby/Menu\"); Load(\"UI/Paperlogy-4Regular\"); string map=\"map:brawl-authoring-trial\"; string assembly=\"Knockbox.Game\";";
            string result=Knockbox.EditorTools.ProjectNamingTools.RewriteSource("Assets/_Project/Scripts/Example.cs",source,plan);
            Assert.That(result,Does.Contain("\"Main_and_lobby/Menu\""));
            Assert.That(result,Does.Contain("\"UI/Paperlogy_4Regular\""));
            Assert.That(result,Does.Contain("\"map:brawl-authoring-trial\""));
            Assert.That(result,Does.Contain("\"Knockbox.Game\""));
        }
    }
}
