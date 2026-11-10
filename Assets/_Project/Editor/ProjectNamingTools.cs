using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Knockbox.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Knockbox.EditorTools
{
    /// <summary>명시적으로 승인한 프로젝트 에셋/오브젝트 이름만 이전한다. 식별자와 외부 원본은 보존한다.</summary>
    public static class ProjectNamingTools
    {
        const string Root = "Assets/_Project/";
        [Serializable] public class FileMove { public string from,to,guid; }
        [Serializable] public class NameChange { public string from,to; }
        [Serializable] public class FileProof { public string path,sha256; }
        [Serializable] public class Plan
        {
            public string project,reportFolder;
            public List<FileMove> moves=new List<FileMove>();
            public List<NameChange> names=new List<NameChange>();
            public List<string> containers=new List<string>();
            public List<FileProof> files=new List<FileProof>();
        }
        static string Project => Directory.GetParent(Application.dataPath).FullName;
        static bool Owned(string path) => path.StartsWith(Root,StringComparison.Ordinal)
            && !path.StartsWith(Root+"LocalOnly/",StringComparison.Ordinal);
        static readonly Regex ObjectBlock = new Regex(@"(?ms)^--- !u!1 &.*?(?=^---|\z)");
        static readonly Regex NameLine = new Regex(@"(?m)^  m_Name: (.+?)\r?$");
        static readonly Regex NameOverride = new Regex(@"(?m)^\s+propertyPath: m_Name\r?\n\s+value: (.+?)\r?$");
        static readonly Regex StringLiteral = new Regex("\"(?:\\\\.|[^\"\\\\])*\"");

        [MenuItem("Knockbox/이름 정리/변경 목록 확인")]
        public static void Preview()
        {
            string report=Prepare(Path.Combine(Project,"Logs","Naming_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Debug.Log("이름 변경 계획을 저장했습니다. 아직 원본은 바꾸지 않았습니다: "+report);
        }

        public static string Prepare(string reportFolder)
        {
            EnsureEditorReady();
            reportFolder=Path.GetFullPath(reportFolder);
            if(Directory.Exists(reportFolder))throw new IOException("새 보고 폴더를 지정하세요. 기존 결과를 덮어쓰지 않습니다.");
            var plan=new Plan{project=Project,reportFolder=reportFolder};
            var paths=AssetDatabase.GetAllAssetPaths().Where(Owned).Where(File.Exists).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            foreach(string path in paths)
            {
                string stem=Path.GetFileNameWithoutExtension(path),clean=SceneObjectName.FileStem(stem);
                if(stem!=clean)
                {
                    string target=(Path.GetDirectoryName(path)+"/"+clean+Path.GetExtension(path)).Replace('\\','/');
                    if(File.Exists(target))throw new IOException("이름이 겹칩니다: "+target);
                    plan.moves.Add(new FileMove{from=path,to=target,guid=AssetDatabase.AssetPathToGUID(path)});
                }
                if(path.EndsWith(".unity")||path.EndsWith(".prefab"))plan.containers.Add(path);
            }
            var names=new SortedDictionary<string,string>(StringComparer.Ordinal);
            foreach(string path in plan.containers)
            {
                string yaml=File.ReadAllText(path);
                foreach(Match block in ObjectBlock.Matches(yaml))
                    foreach(Match match in NameLine.Matches(block.Value))CollectName(match.Groups[1].Value,names);
                foreach(Match match in NameOverride.Matches(yaml))CollectName(match.Groups[1].Value,names);
            }
            foreach(var pair in names)plan.names.Add(new NameChange{from=pair.Key,to=pair.Value});
            ValidateContainers(plan.containers);
            var protectedFiles=new HashSet<string>(StringComparer.Ordinal);
            foreach(var move in plan.moves){protectedFiles.Add(move.from);protectedFiles.Add(move.from+".meta");}
            foreach(string path in plan.containers){protectedFiles.Add(path);protectedFiles.Add(path+".meta");}
            foreach(string path in paths.Where(p=>p.EndsWith(".cs")))
                if(RewriteSource(path,File.ReadAllText(path),plan)!=File.ReadAllText(path))protectedFiles.Add(path);
            foreach(string path in protectedFiles.OrderBy(p=>p,StringComparer.Ordinal))
                plan.files.Add(new FileProof{path=path,sha256=Hash(path)});
            Directory.CreateDirectory(reportFolder);
            string report=Path.Combine(reportFolder,"Naming_Plan.json");
            File.WriteAllText(report,JsonUtility.ToJson(plan,true),new UTF8Encoding(false));
            SessionState.SetString("Knockbox.NamingPlan",report);
            return report;
        }

        static void CollectName(string raw,IDictionary<string,string> names)
        {
            string value=raw.Trim();
            if(value.StartsWith("\""))return; // Unicode-escaped YAML is handled by the Unity object traversal.
            string clean=SceneObjectName.Normalize(value);
            if(value!=clean)names[value]=clean;
        }

        [MenuItem("Knockbox/이름 정리/확인한 목록 적용")]
        public static void ApplyLast()
        {
            string report=SessionState.GetString("Knockbox.NamingPlan","");
            if(string.IsNullOrEmpty(report))throw new InvalidOperationException("먼저 변경 목록을 확인하세요.");
            if(EditorUtility.DisplayDialog("프로젝트 이름 정리","확인한 에셋과 오브젝트 이름을 바꿉니다. 변경 전 사본을 보고 폴더에 보존합니다.","적용","취소"))Apply(report);
        }

        public static void Apply(string reportPath)
        {
            EnsureEditorReady();
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(reportPath));
            if(plan.project!=Project)throw new InvalidOperationException("다른 프로젝트의 이름 변경 계획입니다.");
            string before=Path.Combine(plan.reportFolder,"Before");
            if(Directory.Exists(before))throw new IOException("이미 적용을 시작한 계획입니다. 상태를 확인한 뒤 새 계획을 만드세요.");
            foreach(var file in plan.files)
            {
                if(!Owned(file.path)||Hash(file.path)!=file.sha256)throw new InvalidOperationException("계획 이후 파일이 바뀌었습니다. 다시 확인하세요: "+file.path);
            }
            foreach(var move in plan.moves)
                if(!Owned(move.from)||!Owned(move.to)||File.Exists(move.to))throw new IOException("변경 대상이나 새 이름을 확인하세요: "+move.to);
            ValidateContainers(plan.containers);
            foreach(var file in plan.files)
            {
                string target=Path.Combine(before,file.path);Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file.path,target,false);
                if(Hash(target)!=file.sha256)throw new IOException("변경 전 사본 해시가 다릅니다. 중단: "+file.path);
            }
            var setup=EditorSceneManager.GetSceneManagerSetup();
            // Script/asmdef renames can request a domain reload. Finish the proof and
            // restore the user's scene before allowing that reload to interrupt us.
            EditorApplication.LockReloadAssemblies();
            try
            {
                foreach(string path in plan.containers.Where(p=>p.EndsWith(".prefab")))
                {
                    var root=PrefabUtility.LoadPrefabContents(path);
                    try{if(Rename(root.transform))PrefabUtility.SaveAsPrefabAsset(root,path);}
                    finally{PrefabUtility.UnloadPrefabContents(root);}
                }
                foreach(string path in plan.containers.Where(p=>p.EndsWith(".unity")))
                {
                    var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);bool changed=false;
                    foreach(var root in scene.GetRootGameObjects())changed|=Rename(root.transform);
                    if(changed){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
                }
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach(var file in plan.files.Where(f=>f.path.EndsWith(".cs")))
                    {
                        string original=File.ReadAllText(file.path),updated=RewriteSource(file.path,original,plan);
                        if(updated!=original)File.WriteAllText(file.path,updated,new UTF8Encoding(false));
                    }
                    foreach(var move in plan.moves)
                    {
                        string error=AssetDatabase.MoveAsset(move.from,move.to);
                        if(!string.IsNullOrEmpty(error))throw new IOException(error);
                        // AssetPathToGUID can be stale while imports are suspended. The moved
                        // .meta is the immediate authority; check the database after resuming.
                        string meta=File.ReadAllText(move.to+".meta");
                        if(!Regex.IsMatch(meta,@"(?m)^guid: "+Regex.Escape(move.guid)+@"\s*$"))throw new IOException("GUID 보존 실패: "+move.to);
                    }
                }
                finally{AssetDatabase.StopAssetEditing();}
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach(var move in plan.moves)
                    if(AssetDatabase.AssetPathToGUID(move.to)!=move.guid)throw new IOException("가져오기 후 GUID 보존 실패: "+move.to);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Path.Combine(plan.reportFolder,"Naming_Applied.json"),JsonUtility.ToJson(plan,true),new UTF8Encoding(false));
                Debug.Log("이름 정리 완료. GUID와 변경 전 사본을 보존했습니다. 컴파일과 실제 화면 검사를 진행하세요: "+plan.reportFolder);
            }
            catch(Exception e)
            {
                File.WriteAllText(Path.Combine(plan.reportFolder,"Naming_Failed.txt"),e.ToString());
                throw;
            }
            finally
            {
                try{EditorSceneManager.RestoreSceneManagerSetup(setup);}
                finally{EditorApplication.UnlockReloadAssemblies();}
            }
        }

        static void EnsureEditorReady()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("실행과 컴파일을 마친 뒤 사용하세요.");
            if(PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new InvalidOperationException("프리팹 편집을 저장하고 닫은 뒤 사용하세요.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("열린 씬에 미저장 변경이 있습니다. 먼저 저장하거나 취소하세요.");
        }
        static void ValidateContainers(IEnumerable<string> paths)
        {
            foreach(string path in paths)
            {
                if(path.EndsWith(".prefab"))
                {
                    var root=PrefabUtility.LoadPrefabContents(path);
                    try{ValidateSiblings(new[]{root.transform},path);ValidateTree(root.transform,path);}
                    finally{PrefabUtility.UnloadPrefabContents(root);}
                }
                else
                {
                    var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
                    if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    try
                    {
                        var roots=scene.GetRootGameObjects();ValidateSiblings(roots.Select(r=>r.transform),path);
                        foreach(var root in roots)ValidateTree(root.transform,path);
                    }
                    finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
                }
            }
        }
        static void ValidateTree(Transform parent,string path)
        {ValidateSiblings(parent.Cast<Transform>(),path);foreach(Transform child in parent)ValidateTree(child,path);}
        static void ValidateSiblings(IEnumerable<Transform> siblings,string path)
        {
            var seen=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var child in siblings)
            {
                string normalized=SceneObjectName.Normalize(child.name);
                if(seen.TryGetValue(normalized,out var old)&&old!=child.name)throw new InvalidOperationException("이름 정리 후 같은 부모 아래 이름이 겹칩니다: "+path+" / "+normalized);
                seen[normalized]=child.name;
            }
        }
        static bool Rename(Transform root)
        {
            bool changed=false;
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                string name=SceneObjectName.Normalize(t.name);
                if(t.name==name)continue;
                t.gameObject.name=name;EditorUtility.SetDirty(t.gameObject);
                if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                changed=true;
            }
            return changed;
        }
        public static string RewriteSource(string path,string source,Plan plan)
        {
            if(path.EndsWith("ProjectNamingTools.cs")||path.EndsWith("NamingConventionTests.cs"))return source;
            var names=plan.names.ToDictionary(p=>p.from,p=>p.to,StringComparer.Ordinal);
            string result=StringLiteral.Replace(source,match=>
            {
                string text=match.Value.Substring(1,match.Value.Length-2);
                if(names.TryGetValue(text,out var clean))return "\""+clean+"\"";
                string[] parts=text.Split('/');bool changed=false;
                for(int i=0;i<parts.Length;i++)if(names.TryGetValue(parts[i],out clean)){parts[i]=clean;changed=true;}
                return changed?"\""+string.Join("/",parts)+"\"":match.Value;
            });
            foreach(var move in plan.moves)
            {
                if(!move.from.EndsWith(".ttf"))continue;
                int index=move.from.IndexOf("/Resources/",StringComparison.Ordinal);
                if(index<0)continue;
                string old=move.from.Substring(index+11),next=move.to.Substring(index+11);
                result=result.Replace("\""+Path.ChangeExtension(old,null)+"\"","\""+Path.ChangeExtension(next,null)+"\"");
            }
            return result;
        }
        static string Hash(string path)
        {using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
    }
}
