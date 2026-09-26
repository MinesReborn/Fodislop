#nullable enable

using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;
using Mono.Cecil;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>Prevents source dependencies between Terrain and Lighting implementations.</summary>
public sealed class TerrainLightingBoundaryRule : IRule
{
    private static readonly Regex UsingDirective = new(
        @"\b(?:global\s+)?using\s+(?:(?<static>static)\s+)?(?:(?<alias>[A-Za-z_]\w*)\s*=\s*)?(?<target>[A-Za-z_]\w*(?:(?:::|\.)[A-Za-z_]\w*)*)\s*;",
        RegexOptions.Compiled);
    private static readonly Regex QualifiedReference = new(
        @"(?<![A-Za-z0-9_])(?:global::)?Kern\.World\.(?:Lighting|Terrain)(?:\.[A-Za-z_]\w*)+",
        RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new(
        "@?\"(?:\"\"|\\\\.|[^\"])*\"|'(?:\\\\.|[^'])*'",
        RegexOptions.Compiled);

    public string Id => "KERN-TERRAIN-LIGHTING-BOUNDARY";
    public string Description => "Terrain and Lighting implementation boundary";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        List<RuleViolation> violations = [];
        ScanDomain(context, "Assets/Scripts/World/Terrain", "Kern.World.Lighting", violations, cancellationToken);
        ScanDomain(context, "Assets/Scripts/World/Lighting", "Kern.World.Terrain", violations, cancellationToken);
        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void ScanDomain(
        LinterContext context,
        string domainRoot,
        string forbiddenRoot,
        List<RuleViolation> violations,
        CancellationToken cancellationToken)
    {
        string absoluteRoot = Path.Combine(context.ProjectRoot, domainRoot);
        foreach (string sourcePath in SourceScanner.EnumerateCsFiles(absoluteRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = SourceScanner.GetProjectRelativePath(context.ProjectRoot, sourcePath);
            string source = SourceScanner.StripComments(File.ReadAllText(sourcePath));
            source = StringLiteral.Replace(source, match => new string(' ', match.Length));
            List<(int Start, int End)> usingRanges = [];
            Dictionary<string, string> aliases = UsingDirective.Matches(source)
                .Cast<Match>()
                .Where(directive => directive.Groups["alias"].Success)
                .ToDictionary(
                    directive => directive.Groups["alias"].Value,
                    directive => directive.Groups["target"].Value,
                    StringComparer.Ordinal);

            foreach (Match directive in UsingDirective.Matches(source))
            {
                usingRanges.Add((directive.Index, directive.Index + directive.Length));
                string target = directive.Groups["target"].Value;
                bool forbidden = ResolvesForbiddenTarget(target, forbiddenRoot, aliases, []);
                if (!forbidden)
                {
                    continue;
                }

                int line = 1 + source.AsSpan(0, directive.Index).Count('\n');
                violations.Add(CreateViolation(relativePath, line,
                    $"using directive imports forbidden cross-domain namespace/type '{target}' (boundary: {forbiddenRoot})."));
            }

            foreach (Match reference in QualifiedReference.Matches(source))
            {
                if (usingRanges.Any(range => reference.Index >= range.Start && reference.Index < range.End))
                {
                    continue;
                }

                string target = reference.Value.Replace("global::", string.Empty, StringComparison.Ordinal);
                if (!IsForbiddenTarget(target, forbiddenRoot))
                {
                    continue;
                }

                int line = 1 + source.AsSpan(0, reference.Index).Count('\n');
                violations.Add(CreateViolation(relativePath, line,
                    $"fully qualified reference '{target}' crosses the Terrain/Lighting implementation boundary."));
            }
        }
    }

    private static bool IsForbiddenTarget(string target, string forbiddenRoot) =>
        target.Equals(forbiddenRoot, StringComparison.Ordinal) ||
        target.StartsWith(forbiddenRoot + ".", StringComparison.Ordinal);

    private static bool ResolvesForbiddenTarget(
        string target,
        string forbiddenRoot,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<string> visitedAliases)
    {
        target = target.Replace("global::", string.Empty, StringComparison.Ordinal);
        if (IsForbiddenTarget(target, forbiddenRoot))
        {
            return true;
        }

        int separator = target.IndexOf('.');
        string alias = separator < 0 ? target : target[..separator];
        if (!aliases.TryGetValue(alias, out string? resolved) || !visitedAliases.Add(alias))
        {
            return false;
        }

        string suffix = separator < 0 ? string.Empty : target[separator..];
        return ResolvesForbiddenTarget(resolved + suffix, forbiddenRoot, aliases, visitedAliases);
    }

    private RuleViolation CreateViolation(string path, int line, string message) => new()
    {
        RuleId = Id,
        Message = message,
        Severity = Severity,
        TypeName = path,
        Line = line,
    };
}
