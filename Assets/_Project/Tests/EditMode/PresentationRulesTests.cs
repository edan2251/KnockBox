using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>CP3-7(연출 코드에 색 하드코딩 금지)과 CP3-8(효과음 합성 재현성).</summary>
    public class PresentationRulesTests
    {
        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>색 토큰은 PaletteAsset에만 둔다. ShapeSprites는 흰 마스크 텍스처의 픽셀 값이라 색 토큰이 아니다.</summary>
        static readonly string[] Allowed = { "PaletteAsset.cs", "ShapeSprites.cs" };
        static readonly Regex ColorLiteral = new Regex(
            @"new\s+Color(32)?\s*\(|Color\.(white|black|red|green|blue|yellow|cyan|magenta|gray|grey|clear)\b|ColorUtility\.TryParseHtmlString");

        [Test]
        public void PresentationCode_HasNoHardcodedColors()
        {
            var folders = new[]
            {
                Path.Combine(Application.dataPath, "_Project", "Scripts", "Presentation"),
                Path.Combine(Application.dataPath, "_Project", "Scripts", "Game", "UI"),
                Path.Combine(Application.dataPath, "_Project", "Scripts", "Game", "Bootstrap"),
            };
            var violations = new List<string>();
            foreach (string folder in folders)
            {
                foreach (string path in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    if (Array.IndexOf(Allowed, Path.GetFileName(path)) >= 0)
                        continue;
                    string[] lines = File.ReadAllLines(path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (ColorLiteral.IsMatch(lines[i]))
                            violations.Add($"{Path.GetFileName(path)}:{i + 1} {lines[i].Trim()}");
                    }
                }
            }
            Assert.That(violations, Is.Empty, "색은 PaletteAsset에서 읽어야 함:\n" + string.Join("\n", violations));
        }

        [Test]
        public void Checker_CatchesColorLiterals()
        {
            Assert.IsTrue(ColorLiteral.IsMatch("sr.color = new Color(1f, 0f, 0f);"));
            Assert.IsTrue(ColorLiteral.IsMatch("var c = new Color32(255, 0, 0, 255);"));
            Assert.IsTrue(ColorLiteral.IsMatch("sr.color = Color.white;"));
            Assert.IsFalse(ColorLiteral.IsMatch("sr.color = palette.ink;"));
        }

        static void RunSynth(string outDir)
        {
            var info = new ProcessStartInfo("python", $"-I \"{Path.Combine(ProjectRoot, "Tools", "sfx_synth.py")}\" --out \"{outDir}\"")
            {
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info);
            Assert.IsNotNull(process, "python을 실행할 수 없음");
            string stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(60000), "sfx_synth.py가 60초 안에 끝나지 않음");
            Assert.AreEqual(0, process.ExitCode, stderr);
        }

        [Test]
        public void SfxSynth_RegeneratesIdenticalFiles()
        {
            string a = Path.Combine(Path.GetTempPath(), "knockbox_sfx_a");
            string b = Path.Combine(Path.GetTempPath(), "knockbox_sfx_b");
            foreach (string dir in new[] { a, b })
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            RunSynth(a);
            RunSynth(b);

            string committed = Path.Combine(Application.dataPath, "_Project", "Audio", "Sfx");
            var generated = Directory.GetFiles(a, "*.wav");
            Assert.Greater(generated.Length, 0);
            foreach (string path in generated)
            {
                string name = Path.GetFileName(path);
                byte[] first = File.ReadAllBytes(path);
                CollectionAssert.AreEqual(first, File.ReadAllBytes(Path.Combine(b, name)), $"{name}: 두 번 실행한 결과가 다름");
                CollectionAssert.AreEqual(first, File.ReadAllBytes(Path.Combine(committed, name)), $"{name}: 커밋된 파일과 다름(스크립트를 바꿨다면 다시 생성해 커밋)");
            }
        }
    }
}
