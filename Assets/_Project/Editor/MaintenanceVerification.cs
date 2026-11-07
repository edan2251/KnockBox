using System;
using System.Collections.Generic;
using System.IO;
using Knockbox.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Knockbox.EditorTools
{
    /// <summary>Explicit command-line evidence only. Never rewrites frozen replay expectations or builds.</summary>
    public static class MaintenanceVerification
    {
        static string Output()
        {
            var root=Environment.GetEnvironmentVariable("KNOCKBOX_TASK_OUTPUT");
            if(string.IsNullOrEmpty(root))throw new InvalidOperationException("Set an explicit maintenance output folder.");
            return root;
        }
        public static void FinalReplayAndBuild()
        {
            string root=Output(), evidence=Path.Combine(root,"Evidence");
            string baseline=Environment.GetEnvironmentVariable("KNOCKBOX_FROZEN_BASELINE");
            if(string.IsNullOrEmpty(baseline))throw new InvalidOperationException("Set the immutable baseline root.");
            MaintenanceSceneMigration.CaptureMainTerrain();
            EditorSceneManager.OpenScene(MapAuthoringOperations.MainPath,OpenSceneMode.Single);
            var runner=UnityEngine.Object.FindFirstObjectByType<MatchRunner>();
            foreach(string stem in new[]{"online_P1_20261103_220316","online_P2_20261103_222655"})
            {
                string output=Path.Combine(evidence,"FinalEditor-"+stem);
                if(Directory.Exists(output))throw new IOException("Refusing to overwrite evidence: "+output);
                string record=Path.Combine(baseline,"Snapshot","Project_Brawl","Builds","Knockbox_OnlineTest","Replays",stem+".kbrp");
                string expected=Path.Combine(baseline,"Evidence","CurrentSourceReplay-"+stem,"trace.txt");
                var result=ReplayCheck.Run(runner,record,output,expected,new List<int>());
                if(result.ExitCode!=0)throw new InvalidOperationException("Same-environment replay regression: "+stem);
            }
            string build=Path.Combine(root,"FinalMaintenanceBuild","Knockbox.exe");
            if(Directory.Exists(Path.GetDirectoryName(build)))throw new IOException("Refusing to overwrite final build.");
            var options=new BuildPlayerOptions{scenes=new[]{MapAuthoringOperations.MainPath},locationPathName=build,
                target=BuildTarget.StandaloneWindows64,targetGroup=BuildTargetGroup.Standalone,options=BuildOptions.Development};
            var summary=BuildPipeline.BuildPlayer(options).summary;
            File.WriteAllText(Path.Combine(evidence,"final-maintenance-build.txt"),$"Result={summary.result}\nErrors={summary.totalErrors}\nWarnings={summary.totalWarnings}\nSeconds={summary.totalTime.TotalSeconds}\nBytes={summary.totalSize}\nUnity={Application.unityVersion}\nOutput={build}\n");
            if(summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Final build failed.");
        }
    }
}
