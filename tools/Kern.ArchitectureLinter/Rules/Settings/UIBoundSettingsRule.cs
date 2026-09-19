#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Settings;

/// <summary>
/// UI Settings tab builders must use bound factory methods (CreateBoundSlider,
/// CreateBoundCycleButton, CreateBoundToggle) and register refreshers.
/// Ported from check-architecture.js checkUiBoundSettings().
/// </summary>
public sealed class UIBoundSettingsRule : IRule
{
    public string Id => "KERN-UI-BOUND-SETTINGS";
    public string Description => "UI settings bound control validation";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex CreateSlider = new(@"PauseMenuUIFactory\.CreateSlider\(", RegexOptions.Compiled);
    private static readonly Regex ClickedHandler = new(@"\.clicked\s*\+=", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var settingsUiDir = Path.Combine(context.ProjectRoot, "Assets", "Scripts", "UI", "Settings");

        if (!Directory.Exists(settingsUiDir))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        foreach (var file in Directory.EnumerateFiles(settingsUiDir, "*.cs"))
        {
            var name = Path.GetFileName(file);
            if (name == "PauseMenuUIFactory.cs") continue;

            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                // Check for unbound CreateSlider
                if (CreateSlider.IsMatch(lines[i]))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"строка {i + 1}: PauseMenuUIFactory.CreateSlider() не привязан и не регистрирует refreshers. Используйте CreateBoundSlider<TSection>().",
                        Severity = Severity,
                        TypeName = $"{relative}:{i + 1}"
                    });
                }

                // Check for click handlers that mutate settings without refresh
                if (ClickedHandler.IsMatch(lines[i]) && !relative.Contains("PauseMenu.cs"))
                {
                    var depth = 0;
                    var started = false;
                    var handlerLines = new List<string>();
                    for (var j = i; j < Math.Min(i + 60, lines.Length); j++)
                    {
                        depth += lines[j].Count(c => c == '{');
                        depth -= lines[j].Count(c => c == '}');
                        if (lines[j].Contains("{")) started = true;
                        handlerLines.Add(lines[j]);
                        if (started && depth <= 0) break;
                    }

                    var handlerText = string.Join("\n", handlerLines);
                    var mutatesSettings = Regex.IsMatch(handlerText, @"ApplyCustomTechnicalSettings|_graphicsSettings|_clientConfig|_lightingEngine|_displayManager");
                    var hasRefresh = Regex.IsMatch(handlerText, @"Refresh|Update|_refreshAll");

                    if (mutatesSettings && !hasRefresh)
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"строка {i + 1}: Click handler меняет настройки но не вызывает refresh. Метка кнопки не обновится. Используйте CreateBoundCycleButton или вызовите refresh delegate.",
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
