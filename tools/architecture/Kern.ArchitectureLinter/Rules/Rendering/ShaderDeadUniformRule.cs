using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    // Член общего UnityPerMaterial объявляется ради совпадения раскладки между
    // пассами: SRP Batcher склеивает вызовы только там, где набор одинаков, и
    // такой член может никем не читаться — он не plumbing, а раскладка, и
    // удалять его нельзя (см. TerrainMaterialCBuffer.hlsl). Пометка ставится на
    // строке объявления: комментарии правило вырезает, поэтому смотрит оно
    // сырую строку, а не ту, которую разбирает.
    // Единственная форма исключения — «<идентификатор правила>: <причина>».
    // Причина обязательна: пометка без неё превращает правило в молчание, а
    // молчание неотличимо от проверенной тишины.
    private const string LayoutMarker = "KERN-SHADER-DEAD-UNIFORM:";

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
        var sources = new List<(string Relative, string Code, string[] RawLines)>();
        foreach (string root in ShaderRoots)
        {
            string full = Path.Combine(context.ProjectRoot, root);
            if (!Directory.Exists(full))
                continue;
            foreach (string file in UnitySourceFiles.EnumerateShaderFiles(full))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string raw = File.ReadAllText(file);
                string code = CSharpSourceText.StripComments(raw);
                sources.Add((
                    ProjectSourcePaths.GetProjectRelativePath(context.ProjectRoot, file),
                    code,
                    raw.Split('\n')));
            }
        }

        if (sources.Count == 0)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        // Declarations at brace depth 0 only: struct fields, cbuffer-adjacent
        // locals and function bodies must not count as globals. CBUFFER_START
        // blocks use macros (no braces), so their members stay at depth 0.
        var declarations = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var constants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (relative, code, rawLines) in sources)
        {
            int depth = 0;
            string[] lines = code.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (depth == 0 && !HasLayoutMarker(rawLines, index))
                {
                    Match uniform = UniformRegex.Match(line);
                    if (uniform.Success)
                    {
                        string name = uniform.Groups[1].Value;
                        Record(declarations, name, relative, index + 1);
                        if (Regex.IsMatch(uniform.Value, @"\bconst\b"))
                        {
                            constants.Add(name);
                        }
                    }
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

        // Индексирование таблицы констант из C# — чтение контракта шейдера.
        // Строки в комментариях и сообщениях не являются такими ссылками.
        var hostReads = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in CSharpSourceFiles.EnumerateAllCsFiles(
            Path.Combine(context.ProjectRoot, "Assets/Scripts"),
            Path.Combine(context.ProjectRoot, "Assets/Editor")))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (ElementAccessExpressionSyntax access in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
            {
                if (access.ArgumentList.Arguments.Count == 1 &&
                    access.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                    literal.RawKind == (int)SyntaxKind.StringLiteralExpression)
                {
                    hostReads.Add(literal.Token.ValueText);
                }
            }
        }

        string haystack = string.Join("\n", sources.Select(s => s.Code));
        foreach (var (name, place) in declarations)
        {
            if (name.Length < 3)
                continue;
            int uses = Regex.Matches(haystack, $@"(?<!\w){Regex.Escape(name)}(?!\w)").Count;
            if (uses <= 1 && !(constants.Contains(name) && hostReads.Contains(name)))
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

    /// <summary>Пометка «это раскладка, а не plumbing» — обязана нести причину.</summary>
    private static bool HasLayoutMarker(string[] rawLines, int index)
    {
        if (index >= rawLines.Length)
        {
            return false;
        }

        int at = rawLines[index].IndexOf(LayoutMarker, StringComparison.Ordinal);
        return at >= 0 && rawLines[index][(at + LayoutMarker.Length)..].Trim().Length > 0;
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
