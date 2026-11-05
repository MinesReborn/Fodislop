using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;
using Mono.Cecil;

namespace Kern.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Verifies that all #include directives in shaders, compute shaders, and HLSL files
/// resolve to valid existing files on disk.
/// </summary>
public sealed class ShaderIncludeIntegrityRule : IRule
{
    private static readonly Regex IncludeRegex = new(
        @"#include\s+""([^""]+)""",
        RegexOptions.Compiled);

    public string Id => "KERN-SHADER-INCLUDE";
    public string Description => "Shader and compute include target existence";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        string assetsRoot = Path.Combine(context.ProjectRoot, "Assets");
        if (!Directory.Exists(assetsRoot))
        {
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        foreach (string file in UnitySourceFiles.EnumerateShaderFiles(assetsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string code = CSharpSourceText.StripComments(File.ReadAllText(file));
            string fileDir = Path.GetDirectoryName(file) ?? assetsRoot;
            string relativeSource = ProjectSourcePaths.GetProjectRelativePath(context.ProjectRoot, file);

            foreach (Match match in IncludeRegex.Matches(code))
            {
                string includePath = match.Groups[1].Value;

                // Built-in Unity / package includes
                if (includePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                    includePath.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Project-relative path (e.g. Assets/...)
                string resolvedPath;
                if (includePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedPath = Path.Combine(context.ProjectRoot, includePath);
                }
                else
                {
                    // File-relative path (e.g. "TerrainColorAnimation.hlsl" or "../Foo.hlsl")
                    resolvedPath = Path.GetFullPath(Path.Combine(fileDir, includePath));
                }

                if (!File.Exists(resolvedPath))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Shader includes '{includePath}' which does not exist on disk.",
                        Severity = Severity,
                        AssemblyName = relativeSource,
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
