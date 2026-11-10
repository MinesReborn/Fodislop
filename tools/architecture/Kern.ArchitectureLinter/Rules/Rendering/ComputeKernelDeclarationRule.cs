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
/// Verifies that every kernel declared with '#pragma kernel <Name>' in compute shaders
/// has a corresponding entry function defined in the compute shader or its included project HLSL files.
/// </summary>
public sealed class ComputeKernelDeclarationRule : IRule
{
    private static readonly Regex KernelPragmaRegex = new(
        @"#pragma\s+kernel\s+(\w+)",
        RegexOptions.Compiled);

    public string Id => "KERN-COMPUTE-KERNEL";
    public string Description => "Compute shader kernel function definition checks";
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

        foreach (string file in Directory.EnumerateFiles(assetsRoot, "*.compute", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = File.ReadAllText(file);
            string stripped = CSharpSourceText.StripComments(content);
            string relativeSource = ProjectSourcePaths.GetProjectRelativePath(context.ProjectRoot, file);

            // Collect full text by expanding project-local includes
            string fullShaderText = ExpandProjectIncludes(file, context.ProjectRoot, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            foreach (Match match in KernelPragmaRegex.Matches(stripped))
            {
                string kernelName = match.Groups[1].Value;
                bool hasFunction = Regex.IsMatch(
                    fullShaderText,
                    $@"\bvoid\s+{Regex.Escape(kernelName)}\s*\(",
                    RegexOptions.Multiline);

                if (!hasFunction)
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Compute shader declares '#pragma kernel {kernelName}' but no matching 'void {kernelName}(...)' function was found.",
                        Severity = Severity,
                        AssemblyName = relativeSource,
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static string ExpandProjectIncludes(string file, string projectRoot, HashSet<string> visited)
    {
        if (!visited.Add(file) || !File.Exists(file))
        {
            return string.Empty;
        }

        string text = File.ReadAllText(file);
        string dir = Path.GetDirectoryName(file) ?? projectRoot;

        return Regex.Replace(text, @"#include\s+""([^""]+)""", m =>
        {
            string inc = m.Groups[1].Value;
            if (inc.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string target = inc.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(projectRoot, inc)
                : Path.GetFullPath(Path.Combine(dir, inc));

            return ExpandProjectIncludes(target, projectRoot, visited);
        }) + Environment.NewLine + text;
    }
}
