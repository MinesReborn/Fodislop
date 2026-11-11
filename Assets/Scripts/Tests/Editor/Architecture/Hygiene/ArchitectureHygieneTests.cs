#if UNITY_EDITOR
#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Core;

[TestFixture]
public sealed class ArchitectureHygieneTests
{
    private static readonly Regex s_fileScopedNamespacePattern =
        new(@"^\s*namespace\s+[A-Za-z0-9_.]+\s*;", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_genericConstraintLine =
        new(@"^\s*where\s+.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_unityObjectTypePattern =
        new(@":\s*(MonoBehaviour|ScriptableObject|ScriptableRendererFeature|VolumeComponent)\b", RegexOptions.Compiled);

    [Test]
    public void UnityObjects_MustUseBlockNamespace()
    {
        string scriptsDir = Path.Combine(Application.dataPath, "Scripts");
        string[] files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);

        var violations = new List<string>();

        foreach (string file in files)
        {
            // Skip tests or third-party/generated code if any
            if (file.Contains("/Tests/") || file.EndsWith(".generated.cs"))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            // where T : MonoBehaviour — ограничение обобщения, а не наследование.
            string inherited = s_genericConstraintLine.Replace(text, string.Empty);
            if (s_unityObjectTypePattern.IsMatch(inherited))
            {
                if (s_fileScopedNamespacePattern.IsMatch(text))
                {
                    violations.Add(Path.GetRelativePath(Application.dataPath, file));
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "CRITICAL ARCHITECTURAL INVARIANT: Types inheriting from MonoBehaviour, ScriptableObject, " +
            "ScriptableRendererFeature, or VolumeComponent MUST use block namespace { }, " +
            "otherwise MonoScript.GetClass() can return null during Unity domain reload.\n" +
            "Violating files:\n" + string.Join("\n", violations));
    }

    [Test]
    public void LightingEngine_MustRemainAFacade()
    {
        string lightingEnginePath = Path.Combine(
            Application.dataPath,
            "Scripts/World/Lighting/Runtime/Orchestration/LightingEngine.cs");
        string text = File.ReadAllText(lightingEnginePath);

        string[] forbiddenOrchestration =
        new[] {

            "Graphics.ExecuteCommandBuffer",
            "DispatchCompute",
            "SetComputeTextureParam",
            "SetComputeBufferParam",
            "BeginSample(\"Kern.RadianceCascades\")",
            "RecordMaterialField(",

        };

        foreach (string token in forbiddenOrchestration)
        {
            Assert.That(
                text,
                Does.Not.Contain(token),
                $"LightingEngine must delegate GPU orchestration to LightingUpdateCoordinator: {token}");
        }

    }
}
#endif
