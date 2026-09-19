using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Resources;

/// <summary>
/// Validates script meta file integrity: GUID format, uniqueness, and presence.
/// </summary>
public sealed class ScriptMetaRule : IRule
{
    private static readonly Regex GuidRegex = new Regex(@"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    public string Id => "KERN-SCRIPT-META";
    public string Description => "Script meta GUID integrity";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");

        if (!Directory.Exists(scriptsRoot))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var seenGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Plugins", "VContainer"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metaPath = file + ".meta";
            if (!File.Exists(metaPath))
            {
                violations.Add(new RuleViolation { RuleId = Id, Message = "Скрипт без .meta: Unity назначит GUID сам, и ссылки разойдутся между машинами.", Severity = Severity, AssemblyName = SourceScanner.GetProjectRelativePath(projectRoot, file) + ".meta" });
                continue;
            }

            var meta = File.ReadAllText(metaPath);
            if (string.IsNullOrWhiteSpace(meta))
            {
                violations.Add(new RuleViolation { RuleId = Id, Message = "Мета пустая: Unity проигнорирует скрипт целиком.", Severity = Severity, AssemblyName = SourceScanner.GetProjectRelativePath(projectRoot, file) + ".meta" });
                continue;
            }

            var m = GuidRegex.Match(meta);
            if (!m.Success)
            {
                violations.Add(new RuleViolation { RuleId = Id, Message = "В мете нет корректного GUID из 32 шестнадцатеричных цифр.", Severity = Severity, AssemblyName = SourceScanner.GetProjectRelativePath(projectRoot, file) + ".meta" });
                continue;
            }

            var guid = m.Groups[1].Value;
            if (seenGuids.ContainsKey(guid))
            {
                violations.Add(new RuleViolation { RuleId = Id, Message = $"GUID {guid} уже занят файлом {seenGuids[guid]}: копия меты ломает ссылки.", Severity = Severity, AssemblyName = SourceScanner.GetProjectRelativePath(projectRoot, file) + ".meta" });
                continue;
            }
            seenGuids[guid] = SourceScanner.GetProjectRelativePath(projectRoot, file);
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
