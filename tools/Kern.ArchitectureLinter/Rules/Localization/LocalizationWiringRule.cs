#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Localization;

/// <summary>
/// Localization wiring validation:
/// - No manual OnLanguageChanged subscription (use RegisterLocalizable)
/// - CloneTree/Instantiate must have UILocalizer.Apply in the same method
/// - ILocalizableUI must register with RegisterLocalizable
/// Ported from check-architecture.js checkLocalizationWiring().
/// </summary>
public sealed class LocalizationWiringRule : IRule
{
    public string Id => "KERN-LOCALIZATION-WIRING";
    public string Description => "Localization wiring validation";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex OnLanguageChanged = new(@"OnLanguageChanged\s*[-+]?=", RegexOptions.Compiled);
    private static readonly Regex CloneTree = new(@"\.CloneTree\(\)|\.Instantiate\(\)", RegexOptions.Compiled);
    private static readonly Regex UsesLocalization = new(@"\b_loc\b|ILocalizationService|ILocalizableUI", RegexOptions.Compiled);
    private static readonly Regex ApplyLocalization = new(@"UILocalizer\.Apply|ApplyLocalizedText\(\)", RegexOptions.Compiled);
    private static readonly Regex RegisterLocalizable = new(@"RegisterLocalizable", RegexOptions.Compiled);
    private static readonly Regex ImplementsLocalizableUI = new(@"ILocalizableUI", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var uiSrc = Path.Combine(context.ProjectRoot, "Assets", "Scripts", "UI");

        if (!Directory.Exists(uiSrc))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        foreach (var file in SourceScanner.EnumerateCsFiles(uiSrc, "Tests"))
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);

            // Rule A: no manual OnLanguageChanged
            for (var i = 0; i < lines.Length; i++)
            {
                var codePart = lines[i].Split(new[] { "//" }, StringSplitOptions.None)[0];
                if (string.IsNullOrWhiteSpace(codePart)) continue;

                if (OnLanguageChanged.IsMatch(codePart))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"строка {i + 1}: ручная подписка на OnLanguageChanged. Используйте _loc.RegisterLocalizable(this).",
                        Severity = Severity,
                        TypeName = $"{relative}:{i + 1}"
                    });
                }
            }

            // Rule B: CloneTree must have UILocalizer.Apply in same method
            if (UsesLocalization.IsMatch(content))
            {
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!CloneTree.IsMatch(lines[i])) continue;

                    var depth = 0;
                    var end = i;
                    for (var j = i; j < lines.Length; j++)
                    {
                        depth += lines[j].Count(c => c == '{');
                        depth -= lines[j].Count(c => c == '}');
                        if (depth < 0) { end = j; break; }
                    }

                    var methodBody = string.Join("\n", lines[i..(end + 1)]);
                    if (!ApplyLocalization.IsMatch(methodBody))
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"строка {i + 1}: дерево строится (CloneTree/Instantiate), но в методе нет UILocalizer.Apply. Применяйте локализацию в методе сборки.",
                            Severity = Severity,
                            TypeName = $"{relative}:{i + 1}"
                        });
                    }
                }
            }

            // Rule C: ILocalizableUI must register
            if (ImplementsLocalizableUI.IsMatch(content) && !RegisterLocalizable.IsMatch(content))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "класс реализует ILocalizableUI, но не вызывает RegisterLocalizable. Смена языка до него не дойдёт.",
                    Severity = Severity,
                    TypeName = relative
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
