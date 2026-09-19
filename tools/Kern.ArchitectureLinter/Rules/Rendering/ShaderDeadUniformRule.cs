using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;
using Mono.Cecil;
using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Flags shader globals that are declared but never read in any project
/// shader source. Covers the write-only plumbing case: C# fills a uniform
/// (pass data → SetCompute*Param → uniform) while no kernel reads it
/// (precedent: _DisplayGradePathPower after the path-to-white removal).
/// Only names declared in project shader files are checked; Unity built-ins
/// from packages are never flagged.
/// </summary>
public sealed class ShaderDeadUniformRule : IRule
{
    private static readonly string[] ShaderRoots =
    [
        "Assets/Resources/Shaders",
        "Assets/Shaders",
    ];

    // Global scalar/vector uniform, optionally static/const/array/initialized.
    private static readonly Regex UniformRegex = new(
        @"(?:static\s+)?(?:const\s+)?(?:float|half|int|uint|bool)\d?(?:x\d+)?\s+(\w+)\s*(?:\[[^\]]*\])?\s*(?:=\s*[^;]+)?;",
        RegexOptions.Compiled);

    // Texture/sampler bindings.
    private static readonly Regex ResourceRegex = new(
        @"(?:Texture\d?D(?:Array)?(?:<[^>]+>)?|SamplerState|SamplerComparisonState)\s+(\w+)\s*;",
        RegexOptions.Compiled);

    public string Id => "KERN-SHADER-DEAD-UNIFORM";
    public string Description => "Shader globals declared but never read";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var sources = new List<(string Relative, string Code)>();
        foreach (string root in ShaderRoots)
        {
            string full = Path.Combine(context.ProjectRoot, root);
            if (!Directory.Exists(full))
                continue;
            foreach (string file in SourceScanner.EnumerateShaderFiles(full))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string code = SourceScanner.StripComments(File.ReadAllText(file));
                sources.Add((SourceScanner.GetProjectRelativePath(context.ProjectRoot, file), code));
            }
        }

        if (sources.Count == 0)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        // Declarations at brace depth 0 only: struct fields, cbuffer-adjacent
        // locals and function bodies must not count as globals. CBUFFER_START
        // blocks use macros (no braces), so their members stay at depth 0.
        var declarations = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        foreach (var (relative, code) in sources)
        {
            int depth = 0;
            string[] lines = code.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (depth == 0)
                {
                    Match uniform = UniformRegex.Match(line);
                    if (uniform.Success)
                        Record(declarations, uniform.Groups[1].Value, relative, index + 1);
                    else
                    {
                        Match resource = ResourceRegex.Match(line);
                        if (resource.Success)
                            Record(declarations, resource.Groups[1].Value, relative, index + 1);
                    }
                }

                foreach (char c in line)
                {
                    if (c == '{')
                        depth++;
                    else if (c == '}')
                        depth = Math.Max(0, depth - 1);
                }
            }
        }

        string haystack = string.Join("\n", sources.Select(s => s.Code));
        foreach (var (name, place) in declarations)
        {
            if (name.Length < 3)
                continue;
            int uses = Regex.Matches(haystack, $@"(?<!\w){Regex.Escape(name)}(?!\w)").Count;
            if (uses <= 1)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Shader global '{name}' is declared but never read in any project shader source (write-only plumbing).",
                    Severity = Severity,
                    AssemblyName = place.File,
                    TypeName = place.File,
                    MemberName = name,
                    Line = place.Line,
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static void Record(
        Dictionary<string, (string File, int Line)> declarations,
        string name,
        string relative,
        int lineNumber)
    {
        if (name.Length < 3 || declarations.ContainsKey(name))
            return;
        declarations[name] = (relative, lineNumber);
    }
}
