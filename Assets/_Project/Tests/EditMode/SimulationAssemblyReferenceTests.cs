using System;
using System.Collections.Generic;
using System.IO;
using Knockbox.Simulation;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace Knockbox.Tests.EditMode
{
    /// <summary>
    /// CP0-4: 시뮬레이션 어셈블리가 UnityEngine을 참조하지 않는지 세 단계로 확인한다.
    /// asmdef 설정, Unity가 넘기는 컴파일 참조 목록, 실제로 만들어진 DLL의 참조 목록.
    /// </summary>
    public class SimulationAssemblyReferenceTests
    {
        [Serializable]
        class AsmdefData
        {
            public string name;
            public string[] references;
            public string[] precompiledReferences;
            public bool overrideReferences;
            public bool noEngineReferences;
        }

        static bool IsUnityAssemblyName(string name)
        {
            return name.StartsWith("UnityEngine", StringComparison.Ordinal)
                || name.StartsWith("UnityEditor", StringComparison.Ordinal)
                || name.StartsWith("Unity.", StringComparison.Ordinal);
        }

        [Test]
        public void Asmdef_DisablesEngineReferencesAndHasNoReferences()
        {
            var path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(SimulationAssembly.Name);
            Assert.IsFalse(string.IsNullOrEmpty(path), "Knockbox.Simulation.asmdef를 찾을 수 없음");

            var data = JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path));
            Assert.AreEqual(SimulationAssembly.Name, data.name);
            Assert.IsTrue(data.noEngineReferences, "noEngineReferences가 true가 아님");
            // false면 '자동 참조'가 켜진 패키지 DLL이 몰래 들어온다.
            Assert.IsTrue(data.overrideReferences, "overrideReferences가 true가 아님");
            Assert.That(data.references ?? Array.Empty<string>(), Is.Empty, "다른 어셈블리를 참조함");
            Assert.That(data.precompiledReferences ?? Array.Empty<string>(), Is.Empty, "미리 컴파일된 DLL을 참조함");
        }

        [Test]
        public void CompilationPipeline_PassesNoUnityReferences()
        {
            var assembly = FindCompilationAssembly(SimulationAssembly.Name);
            Assert.IsNotNull(assembly, "컴파일 파이프라인에서 Knockbox.Simulation을 찾을 수 없음");

            Assert.That(assembly.assemblyReferences, Is.Empty, "다른 asmdef 어셈블리를 참조함");

            var offenders = new List<string>();
            foreach (var reference in assembly.compiledAssemblyReferences)
            {
                var file = Path.GetFileNameWithoutExtension(reference);
                if (IsUnityAssemblyName(file))
                    offenders.Add(reference);
            }
            Assert.That(offenders, Is.Empty, "Unity DLL이 컴파일 참조에 들어 있음:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void CompiledDll_ReferencesOnlyBaseClassLibrary()
        {
            var offenders = new List<string>();
            foreach (var reference in typeof(SimulationAssembly).Assembly.GetReferencedAssemblies())
            {
                var name = reference.Name;
                var isBcl = name == "mscorlib" || name == "netstandard" || name == "System"
                    || name.StartsWith("System.", StringComparison.Ordinal);
                if (!isBcl)
                    offenders.Add(reference.FullName);
            }
            Assert.That(offenders, Is.Empty, "기본 클래스 라이브러리 밖의 참조:\n" + string.Join("\n", offenders));
        }

        static UnityEditor.Compilation.Assembly FindCompilationAssembly(string name)
        {
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
            {
                if (assembly.name == name)
                    return assembly;
            }
            return null;
        }
    }
}
