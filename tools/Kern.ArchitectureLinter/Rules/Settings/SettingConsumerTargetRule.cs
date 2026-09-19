#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Settings;

/// <summary>
/// Every [SettingConsumer] attribute must reference a real class and member.
/// Ported from check-architecture.js checkSettingConsumerTargetMembers().
/// </summary>
public sealed class SettingConsumerTargetRule : IRule
{
    public string Id => "KERN-SETTING-CONSUMER-TARGET";
    public string Description => "SettingConsumer target validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly Regex ConsumerPattern = new(
        @"\[SettingConsumer\s*\(\s*SettingConsumerTarget\.([A-Za-z0-9_]+)\s*,\s*""([^""]+)""\s*\)\]",
        RegexOptions.Compiled);

    private static readonly Regex MemberRef = new(@"\b([A-Z][A-Za-z0-9_]+)\.([A-Za-z0-9_]+)\b", RegexOptions.Compiled);

    private static readonly HashSet<string> KnownExternals = new()
    {
        "Screen", "QualitySettings", "Application", "HDROutput", "Math", "Mathf", "Shader"
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var settingsDir = Path.Combine(projectRoot, "Assets", "Scripts", "Core", "Interfaces", "Contracts", "Settings");

        if (!Directory.Exists(settingsDir))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        // Build map of class names to files
        var classFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");
        foreach (var file in SourceScanner.EnumerateAllCsFiles(scriptsRoot))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!classFiles.ContainsKey(name))
                classFiles[name] = file;
        }

        foreach (var file in Directory.EnumerateFiles(settingsDir, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                var match = ConsumerPattern.Match(lines[i]);
                if (!match.Success) continue;

                var mechanism = match.Groups[2].Value;
                var refs_ = MemberRef.Matches(mechanism);

                foreach (Match r in refs_)
                {
                    var className = r.Groups[1].Value;
                    var memberName = r.Groups[2].Value;

                    if (KnownExternals.Contains(className)) continue;

                    if (!classFiles.TryGetValue(className, out var targetFile))
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"[SettingConsumer] ссылается на класс '{className}' в mechanism '{mechanism}', но {className}.cs не найден.",
                            Severity = Severity,
                            TypeName = $"{relative}:{i + 1}"
                        });
                        continue;
                    }

                    var targetContent = File.ReadAllText(targetFile);
                    if (!Regex.IsMatch(targetContent, $@"\b{memberName}\b"))
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"[SettingConsumer] ссылается на '{className}.{memberName}', но член '{memberName}' не определён в {targetFile}.",
                            Severity = Severity,
                            TypeName = $"{relative}:{i + 1}"
                        });
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
