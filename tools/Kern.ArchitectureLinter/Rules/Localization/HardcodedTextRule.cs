#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Localization;

/// <summary>
/// Detects hardcoded Cyrillic text in UXML and UI code.
/// UXML must not carry Cyrillic literals; UI code must not assign Cyrillic
/// string literals to displayed text.
/// Ported from check-architecture.js checkHardcodedText().
/// </summary>
public sealed class HardcodedTextRule : IRule
{
    public string Id => "KERN-HARDCODED-TEXT";
    public string Description => "Hardcoded text detection";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex Cyrillic = new(@"[А-Яа-яЁё]", RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new(@"""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.Compiled);
    private static readonly Regex LogLine = new(@"Debug\.(Log|LogWarning|LogError|LogException|Assert)\s*\(|throw new", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // 1. UXML hardcoded text
        var uiDir = Path.Combine(projectRoot, "Assets", "Resources", "UI");
        if (Directory.Exists(uiDir))
        {
            foreach (var file in Directory.EnumerateFiles(uiDir, "*.uxml"))
            {
                var content = File.ReadAllText(file);
                var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);

                foreach (Match m in Regex.Matches(content, @"(?:text|tooltip)=""([^""]*[А-Яа-яЁё][^""]*)"""))
                {
                    var attr = m.Value.Split('=')[0];
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"'{m.Groups[1].Value}' — {attr}-атрибут в UXML захардкожен. Задайте ключ и переведите в словарь.",
                        Severity = Severity,
                        TypeName = relative
                    });
                }
            }
        }

        // 2. UI code hardcoded text
        var uiSrc = Path.Combine(projectRoot, "Assets", "Scripts", "UI");
        if (Directory.Exists(uiSrc))
        {
            foreach (var file in SourceScanner.EnumerateCsFiles(uiSrc, "Tests"))
            {
                var content = File.ReadAllText(file);
                var lines = content.Split('\n');
                var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
                var inLogContext = false;

                for (var i = 0; i < lines.Length; i++)
                {
                    var raw = lines[i];
                    var trimmed = raw.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                        continue;

                    var codePart = raw.Split(new[] { "//" }, StringSplitOptions.None)[0];
                    if (LogLine.IsMatch(codePart))
                        inLogContext = true;

                    if (!inLogContext)
                    {
                        // Strip L("key", fallback) calls
                        var stripped = Regex.Replace(codePart, @"L\([^)]*\)", "");
                        foreach (Match m in StringLiteral.Matches(stripped))
                        {
                            if (Cyrillic.IsMatch(m.Groups[1].Value))
                            {
                                violations.Add(new RuleViolation
                                {
                                    RuleId = Id,
                                    Message = $"строка {i + 1}: '{m.Groups[1].Value[..Math.Min(60, m.Groups[1].Value.Length)]}' — текст задаётся литералом. Используйте _loc.Get(\"...\").",
                                    Severity = Severity,
                                    TypeName = $"{relative}:{i + 1}"
                                });
                            }
                        }
                    }

                    if (codePart.TrimEnd().EndsWith(";"))
                        inLogContext = false;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
