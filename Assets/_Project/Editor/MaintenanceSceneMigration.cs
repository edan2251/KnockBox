using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Knockbox.Game;
using Knockbox.Game.Maps;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Knockbox.EditorTools
{
    /// <summary>Explicit one-time migration. Never invoked at startup or after team edits.</summary>
    public static class MaintenanceSceneMigration
    {
        public const string Prefabs="Assets/_Project/Prefabs/Frontend";
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
            var scene=EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var runner=UnityEngine.Object.FindFirstObjectByType<MatchRunner>();var presenter=runner.GetComponent<MatchPresenter>();
            if(presenter.AuthoredOwners)throw new InvalidOperationException("Main is already authored. Refusing to regenerate edited scene objects.");
            EnsureFolder(MapAuthoringOperations.Folder);EnsureFolder(Prefabs);
            var palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            var shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");shapes.Activate();
            var arena=ArenaFiles.LoadByName("Brawl");var meta=ArenaMeta.Parse(File.ReadAllText("Assets/_Project/Data/Arenas/Brawl.json"),"Brawl.json");
            if(arena.ComputeChecksum()!=0x483E0206u)throw new InvalidOperationException("Brawl baseline hash changed before migration.");
            var original=ScriptableObject.CreateInstance<MapDefinition>();original.Import(arena,meta,"brawl-original","Brawl (preserved)",true);
            CreateAsset(original,MapAuthoringOperations.Folder+"/BrawlOriginal.asset");
            var trial=ScriptableObject.CreateInstance<MapDefinition>();trial.Import(arena,meta,"brawl-authoring-trial","Brawl authoring trial");
            CreateAsset(trial,MapAuthoringOperations.Folder+"/BrawlTrial.asset");
            var catalog=ScriptableObject.CreateInstance<MapCatalog>();catalog.Register(original);catalog.Register(trial);
            CreateAsset(catalog,MapAuthoringOperations.CatalogPath);
            Set(runner,"mapCatalog",catalog);Set(runner,"selectedMap",original);runner.ArenaName="Brawl";runner.Rules=RuleSet.Brawl;
            Set(presenter,"runner",runner);
            var owners=new (string field,string name,Type type)[]{
                ("background","Background",typeof(BackgroundView)),("arenaView","Arena",typeof(ArenaView)),
                ("propView","Props",typeof(PropView)),("propShadows","PropShadows",typeof(PropShadowView)),
                ("fighterShadows","FighterShadows",typeof(FighterShadowView)),("fighter0","Fighter_1",typeof(FighterView)),("fighter1","Fighter_2",typeof(FighterView)),
                ("dust","Dust",typeof(DustPool)),("projectileView","Projectiles",typeof(ProjectileView)),("brawlWeaponView","Brawl_Weapon",typeof(BrawlWeaponView)),
                ("boundaryView","Boundary",typeof(BoundaryView)),("fx","Impact_Fx",typeof(ImpactFx)),("koShatter","KO_Shatter",typeof(KoShatter)),
                ("hud","Hud",typeof(HudView)),("pickPanel","PickPanel",typeof(PickPanelView)),("sfxPlayer","Sfx",typeof(SfxPlayer)),
                ("labels","Debug_Labels",typeof(DebugStateLabels)),("overlay","Hitbox_Overlay",typeof(HitboxOverlay)),("aimLine","Aim_Line",typeof(AimLineView)),
                ("help","Help",typeof(HelpOverlay)),("postFx","Post_Fx",typeof(PostFx))};
            foreach(var o in owners){var go=new GameObject(o.name);go.transform.SetParent(runner.transform,false);Set(presenter,o.field,go.AddComponent(o.type));}
            var cam=Camera.main;var rig=cam.GetComponent<CameraRig>()??cam.gameObject.AddComponent<CameraRig>();Set(presenter,"cameraRig",rig);
            if(cam.GetComponent<OutputLimiter>()==null)cam.gameObject.AddComponent<OutputLimiter>();
            var config=runner.PreviewConfig(true);runner.Begin(config,arena,SimState.CreateMatch(config,new[]{arena},runner.Seed,MatchPhase.Countdown));runner.Running=false;
            for(int p=0;p<2;p++)
            {
                var actor=presenter.Fighter(p);actor.Setup(p,palette,shapes);
                var fighter=runner.World.State.GetFighter(p);actor.Render(fighter,fighter,1,0,arena,config);
            }
            Get<ArenaView>(presenter,"arenaView").Build(arena,palette.Theme(meta.Theme),palette,shapes,meta.Ceiling);
            rig.FitArena(arena);cam.orthographicSize=arena.HalfHeight;cam.backgroundColor=palette.bgBase;
            var online=runner.gameObject.AddComponent<OnlineMatch>();var lobby=runner.gameObject.AddComponent<OnlineLobby>();
            Set(online,"runner",runner);Set(online,"presenter",presenter);Set(online,"hotkeys",runner.GetComponent<DebugHotkeys>());
            Set(lobby,"runner",runner);Set(lobby,"presenter",presenter);Set(lobby,"online",online);
            var controller=runner.gameObject.AddComponent<FrontendController>();
            Set(controller,"runner",runner);Set(controller,"presenter",presenter);Set(controller,"online",online);Set(controller,"lobby",lobby);
            var ui=new GameObject("Frontend___edit_these_screens");var view=ui.AddComponent<FrontendView>();view.Build(runner,presenter);
            view.CaptureAuthoredLayout();Set(controller,"<View>k__BackingField",view);
            foreach(var motion in ui.GetComponentsInChildren<FrontendButtonMotion>(true))
            {
                Set(motion,"button",motion.GetComponent<UnityEngine.UI.Button>());Set(motion,"label",motion.GetComponentInChildren<UnityEngine.UI.Text>(true));
            }
            // Nested prefabs share the actual references held by FrontendView; no second runtime screen is generated.
            SaveNested(view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Main_and_lobby")).gameObject,"MainAndLobby");
            SaveNested(view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Shared_VS_and_cards")).gameObject,"DuelCardsResult");
            SaveNested(view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path("Acquired_cards")).gameObject,"AcquiredCards");
            SaveNested(ui,"Frontend");
            Set(presenter,"authoredOwners",true);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            CreateMapAuthoring();
            Debug.Log("[Maintenance] Main authored UI/owners and MapAuthoring created. Legacy Brawl hash: "+arena.ComputeChecksum().ToString("X8"));
        }

        public static void CreateMapAuthoring()
        {
            if(File.Exists(MapAuthoringOperations.ScenePath))throw new InvalidOperationException("MapAuthoring already exists; refusing to replace it.");
            var authoringScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // Closing Main can unload assets. Resolve persistent references after creating the new scene.
            var trial=AssetDatabase.LoadAssetAtPath<MapDefinition>(MapAuthoringOperations.Folder+"/BrawlTrial.asset");
            var catalog=AssetDatabase.LoadAssetAtPath<MapCatalog>(MapAuthoringOperations.CatalogPath);
            var shapes=AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_Project/Art/ShapeLibrary.asset");
            var palette=AssetDatabase.LoadAssetAtPath<PaletteAsset>("Assets/_Project/Art/Palette.asset");
            var authoring=new GameObject("Map_Authoring___definition_is_source").AddComponent<MapAuthoring>();
            authoring.definition=trial;authoring.catalog=catalog;authoring.shapes=shapes;authoring.palette=palette;
            authoring.terrainRoot=new GameObject("Terrain___explicit_box_order").transform;authoring.terrainRoot.SetParent(authoring.transform,false);
            authoring.spawn0=Spawn("P1_spawn_FOOT",authoring.transform,palette.FighterBody(0),shapes);
            authoring.spawn1=Spawn("P2_spawn_FOOT",authoring.transform,palette.FighterBody(1),shapes);
            MapAuthoringOperations.Open(authoring);
            var mapCamera=new GameObject("Authoring_camera").AddComponent<Camera>();mapCamera.orthographic=true;mapCamera.orthographicSize=trial.Rows*.5f+1;mapCamera.transform.position=new Vector3(0,0,-10);mapCamera.backgroundColor=palette.bgBase;
            EditorSceneManager.SaveScene(authoringScene,MapAuthoringOperations.ScenePath);
            AssetDatabase.SaveAssets();EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            Debug.Log("[Maintenance] MapAuthoring saved with separate Brawl trial. Main default remains Brawl.");
        }
        public static void PopulateStaticVisuals()
        {
            var scene=EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var presenter=UnityEngine.Object.FindFirstObjectByType<MatchPresenter>();
            if(!presenter.AuthoredOwners)throw new InvalidOperationException("Authored Main is required.");
            var shapes=presenter.Shapes;shapes.Activate();
            var hud=presenter.Hud;hud.Setup(presenter.Palette,shapes);
            var aim=Get<AimLineView>(presenter,"aimLine");aim.Setup(presenter.Palette,shapes);
            var weapon=Get<BrawlWeaponView>(presenter,"brawlWeaponView");weapon.Setup(presenter.Palette,shapes);
            foreach(var r in hud.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            foreach(var r in aim.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            SaveNested(hud.gameObject,"GameplayHud");SaveNested(weapon.gameObject,"WeaponVisual");
            var post=presenter.PostFx;var volume=post.GetComponent<Volume>()??post.gameObject.AddComponent<Volume>();
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            CreateAsset(profile,Prefabs+"/PostFxProfile.asset");
            var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(0);AssetDatabase.AddObjectToAsset(bloom,profile);
            var chromatic=profile.Add<ChromaticAberration>(true);chromatic.intensity.Override(0);AssetDatabase.AddObjectToAsset(chromatic,profile);
            var vignette=profile.Add<Vignette>(true);vignette.intensity.Override(PostFx.VignetteIntensity);vignette.smoothness.Override(.45f);AssetDatabase.AddObjectToAsset(vignette,profile);
            volume.sharedProfile=profile;volume.isGlobal=true;volume.priority=10;
            Set(post,"volume",volume);Set(post,"baseProfile",profile);EditorUtility.SetDirty(profile);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("[Maintenance] HUD, aim dots, weapon art and post-processing are placed and referenced before Play.");
        }
        public static void CaptureMainTerrain()
        {
            var scene=EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var arena=UnityEngine.Object.FindFirstObjectByType<ArenaView>();arena.CaptureDefinitionVisuals();
            EditorUtility.SetDirty(arena);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        static Transform Spawn(string name,Transform parent,Color color,ShapeLibrary shapes)
        {
            var sr=ShapeLibrary.Create(parent,name,shapes.Circle,SortingLayers.UI,0,color);ShapeLibrary.SetSize(sr,.3f,.3f);return sr.transform;
        }
        static void SaveNested(GameObject root,string name)
        {
            string path=Prefabs+"/"+name+".prefab";
            if(File.Exists(path))throw new InvalidOperationException("Refusing to overwrite "+path);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,path,InteractionMode.AutomatedAction);
        }
        static void CreateAsset(UnityEngine.Object obj,string path)
        {if(File.Exists(path))throw new InvalidOperationException("Refusing to overwrite "+path);AssetDatabase.CreateAsset(obj,path);}
        public static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
        public static void Set(UnityEngine.Object target,string field,object value)
        {target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(target,value);EditorUtility.SetDirty(target);}
        static T Get<T>(object target,string field)=>(T)target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    }
}
