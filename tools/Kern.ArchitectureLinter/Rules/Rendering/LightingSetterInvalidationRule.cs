#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Every public setter in LightingEngine that mutates lighting parameters must
/// call _configHolder.Set* and invalidate at least one lighting dirty flag.
/// Ported from check-architecture.js checkLightingEngineSetterInvalidations().
/// </summary>
public sealed class LightingSetterInvalidationRule : IRule
{
    public string Id => "KERN-LIGHTING-SETTER-INVALIDATION";
    public string Description => "LightingEngine setter invalidation validation";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private const string LightingPath = "Assets/Scripts/World/Lighting/Core/LightingEngine.cs";

    private static readonly Regex SetterPattern = new(@"^\s*public\s+void\s+(Set[A-Za-z0-9_]+)\s*\(", RegexOptions.Multiline);
    private static readonly Regex DirtyFlagPattern = new(
        @"(?:_ambientOcclusionDirty|_bounceDirty|_compositeDirty|_fieldDirty|CompositeDirty|FieldDirty|BounceDirty|AmbientOcclusionDirty|MarkDirty|_nextDynamicLightingUpdateTime|_hasStaticRadianceState|HasRenderedLightState|HasStaticRadianceState|HasDynamicRadianceState)\s*=",
        RegexOptions.IgnoreCase);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var path = Path.Combine(context.ProjectRoot, LightingPath);

        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var content = File.ReadAllText(path);
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var match = SetterPattern.Match(lines[i]);
            if (!match.Success) continue;

            var methodName = match.Groups[1].Value;

            // Find method body (up to 40 lines)
            var depth = 0;
            var started = false;
            var bodyLines = new List<string>();
            for (var j = i; j < Math.Min(i + 40, lines.Length); j++)
            {
                depth += lines[j].Count(c => c == '{');
                depth -= lines[j].Count(c => c == '}');
                if (lines[j].Contains("{")) started = true;
                bodyLines.Add(lines[j]);
                if (started && depth <= 0) break;
            }

            var body = string.Join("\n", bodyLines);
            if (!DirtyFlagPattern.IsMatch(body))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"{methodName} не инвалидирует ни одного lighting dirty flag (_compositeDirty, _fieldDirty, _bounceDirty, _ambientOcclusionDirty). Сеттер без инвалидации — мёртвая настройка.",
                    Severity = Severity,
                    TypeName = $"{LightingPath}:{i + 1}"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
