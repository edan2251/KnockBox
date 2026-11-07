using System;
using System.IO;
using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>입력 기록 파일 저장·읽기. 프로젝트 루트의 Replays/(Git 제외)에 둔다.</summary>
    public static class ReplayFiles
    {
        public const string Extension = ".kbrp";

        public static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Replays");

        public static string Save(InputRecording recording, string name = null)
        {
            Directory.CreateDirectory(Folder);
            string fileName = (name ?? DateTime.Now.ToString("yyyyMMdd_HHmmss")) + Extension;
            string path = Path.Combine(Folder, fileName);
            File.WriteAllBytes(path, recording.ToBytes());
            return path;
        }

        public static InputRecording Load(string path) => InputRecording.FromBytes(File.ReadAllBytes(path));

        /// <summary>가장 최근에 저장한 기록. 없으면 null.</summary>
        public static string FindLatest()
        {
            if (!Directory.Exists(Folder))
                return null;
            string latest = null;
            DateTime latestTime = DateTime.MinValue;
            foreach (string path in Directory.GetFiles(Folder, "*" + Extension))
            {
                var time = File.GetLastWriteTimeUtc(path);
                if (time > latestTime)
                {
                    latestTime = time;
                    latest = path;
                }
            }
            return latest;
        }
    }
}
