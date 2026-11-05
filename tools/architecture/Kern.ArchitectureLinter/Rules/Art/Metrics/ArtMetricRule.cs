#nullable enable

using Mono.Cecil;
using Kern.ArchitectureLinter.Core;

namespace Kern.ArchitectureLinter.Rules.Art;

/// <summary>
/// Метрика семейств арта и целостность набора состояний одного контрола.
/// </summary>
///
/// Размер — договор, а не вкус: один исходный пиксель обязан занимать целое
/// число экранных, иначе ровная линия идёт ступеньками, а состояния одного
/// контрола дёргаются при переключении. Числа взяты из решения по арту;
/// правила, требующие вкуса (палитра, контур, поля), здесь не пишутся — их
/// место в арт-библии.
///
/// Долг под потолком: число ассетов вне метрики — снимок, а не норма. Рост
/// нарушением считается и вниз: иначе отвоёванное можно молча вернуть.
public sealed class ArtMetricRule : IRule
{
    public string Id => "KERN-ART-METRIC";
    public string Description => "Art family metric and state set consistency";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private const int OutOfMetricBudget = 9;
    private const int StateSetBudget = 1;

    // Root — префикс пути, дальше ровно одна метрика семейства:
    // кратность клетке, точный размер или единый размер набора.
    private static readonly (string Root, int? Multiple, int? Width, int? Height, bool Uniform)[] Families =
    {
        ("Assets/Textures/Cells", 32, null, null, false),
        ("Assets/Textures/Pack", 32, null, null, false),
        ("Assets/Textures/Items", null, 42, 42, false),
        ("Assets/Resources/UI/HUD/Player/Skills/Icons", null, 73, 73, false),
        ("Assets/Textures/Crystals", null, null, null, true),
    };

    // Слова состояний. Имя контрола берётся как остаток после токена, поэтому
    // `unchecked.png` и `panel_hover.png` попадают в разные наборы.
    private static readonly string[] StateTokens =
    {
        "checked", "unchecked", "deselected", "selected",
        "normal", "hover", "hovered", "pressed", "active", "inactive",
        "disabled", "enabled", "focused", "opened", "closed",
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        var families = new Dictionary<string, List<(string Path, int Width, int Height)>>(StringComparer.Ordinal);
        var all = new List<(string Path, int Width, int Height, string Family)>();

        foreach (var file in ArtFiles.EnumeratePngs(
            Path.Combine(projectRoot, "Assets/Textures"),
            Path.Combine(projectRoot, "Assets/Resources")))
        {
            var relative = Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            var size = ArtFiles.TryReadSize(file);
            if (size is not { } measured)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"PNG не читается как PNG: {relative}",
                    Severity = Severity,
                    TypeName = relative
                });
                continue;
            }

            var family = ResolveFamily(relative);
            all.Add((relative, measured.Width, measured.Height, family ?? string.Empty));
            if (family is not null)
            {
                if (!families.TryGetValue(family, out var items))
                {
                    items = new List<(string, int, int)>();
                    families[family] = items;
                }

                items.Add((relative, measured.Width, measured.Height));
            }
        }

        var outOfMetric = new List<string>();
        foreach (var (root, multiple, width, height, uniform) in Families)
        {
            if (!families.TryGetValue(root, out var items))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"семейство арта не найдено: {root}",
                    Severity = Severity,
                    TypeName = root
                });
                continue;
            }

            if (uniform)
            {
                var mode = items
                    .GroupBy(item => (item.Width, item.Height))
                    .OrderByDescending(group => group.Count())
                    .First()
                    .Key;
                foreach (var item in items)
                {
                    if (item.Width != mode.Width || item.Height != mode.Height)
                    {
                        outOfMetric.Add($"{item.Path} {item.Width}x{item.Height} (не {mode.Width}x{mode.Height})");
                    }
                }

                continue;
            }

            foreach (var item in items)
            {
                if (multiple is int step)
                {
                    if (item.Width % step != 0 || item.Height % step != 0)
                    {
                        outOfMetric.Add($"{item.Path} {item.Width}x{item.Height} (не кратно {step})");
                    }
                }
                else if (width is int expectedWidth && height is int expectedHeight &&
                    (item.Width != expectedWidth || item.Height != expectedHeight))
                {
                    outOfMetric.Add($"{item.Path} {item.Width}x{item.Height} (не {expectedWidth}x{expectedHeight})");
                }
            }
        }

        var stateSets = FindStateSetDefects(all);
        ReportBudget(
            violations,
            "ассеты вне метрики",
            outOfMetric.Count,
            OutOfMetricBudget,
            outOfMetric);
        ReportBudget(
            violations,
            "наборы состояний разного размера",
            stateSets.Count,
            StateSetBudget,
            stateSets);

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void ReportBudget(
        List<RuleViolation> violations,
        string name,
        int actual,
        int budget,
        List<string> offenders)
    {
        if (actual == budget)
        {
            return;
        }

        string direction = actual > budget
            ? $"вырос на {actual - budget}"
            : $"упал на {budget - actual} — опустите потолок";
        violations.Add(new RuleViolation
        {
            RuleId = Id,
            Message = $"потолок «{name}»: {actual} при потолке {budget} — долг {direction}. " +
                $"Потолок — снимок, а не норма. Список: {string.Join("; ", offenders)}",
            Severity = Severity,
            TypeName = "Assets"
        });
    }

    private static string? ResolveFamily(string relative)
    {
        string? best = null;
        foreach (var (root, _, _, _, _) in Families)
        {
            if (relative == root || relative.StartsWith(root + "/", StringComparison.Ordinal))
            {
                if (best is null || root.Length > best.Length)
                {
                    best = root;
                }
            }
        }

        return best;
    }

    private static List<string> FindStateSetDefects(List<(string Path, int Width, int Height, string Family)> all)
    {
        var defects = new List<string>();
        var sets = new Dictionary<string, List<(string Path, int Width, int Height)>>(StringComparer.Ordinal);
        foreach (var item in all)
        {
            var directory = item.Path[..item.Path.LastIndexOf('/')];
            var stem = item.Path[(item.Path.LastIndexOf('/') + 1)..^4];
            var baseName = StripStateToken(stem);
            if (baseName is null)
            {
                continue;
            }

            var key = $"{directory}/{baseName}";
            if (!sets.TryGetValue(key, out var members))
            {
                members = new List<(string, int, int)>();
                sets[key] = members;
            }

            members.Add((item.Path, item.Width, item.Height));
        }

        foreach (var (key, members) in sets)
        {
            if (members.Count < 2)
            {
                continue;
            }

            var sizes = members.Select(member => $"{member.Width}x{member.Height}").Distinct().ToList();
            if (sizes.Count > 1)
            {
                defects.Add($"{key}: {string.Join(", ", sizes)}");
            }
        }

        return defects;
    }

    private static string? StripStateToken(string stem)
    {
        foreach (var token in StateTokens)
        {
            if (stem == token)
            {
                return string.Empty;
            }

            foreach (var separator in new[] { '_', '-' })
            {
                if (stem.EndsWith(separator + token, StringComparison.Ordinal))
                {
                    return stem[..^(token.Length + 1)];
                }
            }
        }

        return null;
    }
}
