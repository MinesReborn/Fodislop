#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.USS;

/// <summary>
/// Tracks design system debt: inline styles, literals, raw colors.
/// Debt has a ceiling (DEBT_BUDGET) — it must not grow.
/// Ported from check-architecture.js checkDesignSystemRatchet().
/// </summary>
public sealed class DesignSystemDebtRule : IRule
{
    public string Id => "KERN-DESIGN-SYSTEM-DEBT";
    public string Description => "Design system debt ceiling enforcement";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Dictionary<string, int> Budgets = new()
    {
        ["inline вне main game"] = 42,
        ["inline в main game"] = 208,
        ["литерал в общем слое"] = 225,
        ["литерал в main game"] = 321,
    };

    private static readonly HashSet<string> MainGameDirs = new() { "HUD", "Map", "Chat", "Programmator", "Settings", "Overlays", "Inventory" };
    private static readonly HashSet<string> MainGameUss = new() { "HUD.uss", "Inventory.uss", "Chat.uss", "chat-input.uss", "Programmator.uss", "PauseMenu.uss", "Modal.uss" };
    private static readonly HashSet<string> GeneratedUss = new() { "ThemeTokens.uss", "TokenUtilities.uss" };

    private static readonly Regex HexColor = new(@"#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex RGBA = new(@"\brgba?\(", RegexOptions.Compiled);
    private static readonly Regex NamedColor = new(@":\s*(?:white|black|red|green|blue|yellow|magenta|cyan|gray|grey|silver|maroon|olive|lime|teal|navy|fuchsia|purple|aquamarine)\s*[;}]", RegexOptions.Compiled);
    private static readonly Regex PxValues = new(@"(?<![\w-])\d+(?:\.\d+)?px", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        var counts = new Dictionary<string, int>
        {
            ["inline вне main game"] = 0,
            ["inline в main game"] = 0,
            ["литерал в общем слое"] = 0,
            ["литерал в main game"] = 0,
        };

        // Count inline styles in C# UI files
        var uiRoot = Path.Combine(projectRoot, "Assets", "Scripts", "UI");
        if (Directory.Exists(uiRoot))
        {
            foreach (var file in SourceScanner.EnumerateCsFiles(uiRoot, "Tests"))
            {
                var relative = Path.GetRelativePath(uiRoot, file).Replace('\\', '/');
                var top = relative.Split('/')[0];
                var key = MainGameDirs.Contains(top) ? "inline в main game" : "inline вне main game";

                var code = SourceScanner.StripComments(File.ReadAllText(file));
                counts[key] += Regex.Matches(code, @"\.style\b").Count;
            }
        }

        // Count literals in USS files
        var stylesDir = Path.Combine(projectRoot, "Assets", "Resources", "Styles");
        var uxmlDir = Path.Combine(projectRoot, "Assets", "Resources", "UI");

        var ussFiles = Directory.Exists(stylesDir) ? Directory.EnumerateFiles(stylesDir, "*.uss") : Enumerable.Empty<string>();
        var uxmlUssFiles = Directory.Exists(uxmlDir) ? Directory.EnumerateFiles(uxmlDir, "*.uss") : Enumerable.Empty<string>();

        foreach (var file in ussFiles.Concat(uxmlUssFiles))
        {
            var name = Path.GetFileName(file);
            if (GeneratedUss.Contains(name)) continue;

            var key = MainGameUss.Contains(name) ? "литерал в main game" : "литерал в общем слое";
            var code = StripUssComments(File.ReadAllText(file));

            counts[key] += HexColor.Matches(code).Count;
            counts[key] += RGBA.Matches(code).Count;
            counts[key] += NamedColor.Matches(code).Count;
            counts[key] += PxValues.Matches(code).Count;
        }

        // Check budgets
        foreach (var (name, budget) in Budgets)
        {
            var actual = counts[name];
            if (actual > budget)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"долг «{name}»: {actual} при потолке {budget} — долг вырос на {actual - budget}",
                    Severity = Severity,
                    TypeName = "Assets/Resources/Styles"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static string StripUssComments(string text)
    {
        return Regex.Replace(text, @"/\*[\s\S]*?\*/", m => "\n".Repeat(m.Value.Split('\n').Length - 1));
    }
}

internal static class StringRepeatExtension
{
    public static string Repeat(this string s, int count) => string.Concat(Enumerable.Repeat(s, Math.Max(0, count)));
}
