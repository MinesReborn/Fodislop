using System.Text;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class InventoryTool
{
    public static int Run(string[] args)
    {
        Console.WriteLine("ИНВЕНТАРИЗАЦИЯ ДИЗАЙН-СИСТЕМЫ KERN");
        Console.WriteLine("Только измерения. Приговор выносит lint-design-system.py.");

        string root = GetRoot();
        var cssFiles = Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories).ToList();
        cssFiles.Add(Path.Combine(root, "styles.css"));
        var markup = new[] { Path.Combine(root, "index.html"), Path.Combine(root, "styleguide.html") };
        var scripts = new[] { Path.Combine(root, "app.js"), Path.Combine(root, "js", "styleguide.js") };
        var allFiles = cssFiles.Concat(markup).Concat(scripts).ToList();

        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in cssFiles)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(--[a-z0-9-]+)\s*:", RegexOptions.IgnoreCase))
            {
                declared[m.Groups[1].Value] = Path.GetFileName(f);
            }
        }

        var uses = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in allFiles)
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"var\(\s*(--[a-z0-9-]+)", RegexOptions.IgnoreCase))
            {
                string token = m.Groups[1].Value;
                if (!uses.TryGetValue(token, out var counter))
                {
                    counter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    uses[token] = counter;
                }
                counter[Path.GetFileName(f)] = counter.GetValueOrDefault(Path.GetFileName(f)) + 1;
            }
        }

        Console.WriteLine($"\n1. ТОКЕНЫ ПО СЛОЯМ\n{new string('=', 70)}");
        Console.WriteLine($"  всего объявлено: {declared.Count}");

        var layers = new (string label, Func<string, bool> test)[]
        {
            ("примитив", n => n.StartsWith("--rgb-") || n.StartsWith("--hex-")),
            ("материал", n => n.StartsWith("--mat-")),
            ("семантика", n => n.StartsWith("--surface-") || n.StartsWith("--border-") || n.StartsWith("--text-") || n.StartsWith("--accent-") || n.StartsWith("--state-") || n.StartsWith("--rarity-") || n.StartsWith("--focus-")),
            ("шкала", n => n.StartsWith("--space-") || n.StartsWith("--size-") || n.StartsWith("--radius-") || n.StartsWith("--dur-") || n.StartsWith("--ease-") || n.StartsWith("--blur-") || n.StartsWith("--leading-") || n.StartsWith("--tracking-") || n.StartsWith("--weight-") || n.StartsWith("--layer-") || n.StartsWith("--alpha-")),
            ("гарнитура", n => n.StartsWith("--face-") || n.StartsWith("--font-")),
        };

        foreach (var (label, test) in layers)
        {
            int count = declared.Keys.Count(test);
            int useCount = uses.Where(kv => test(kv.Key)).Sum(kv => kv.Value.Values.Sum());
            Console.WriteLine($"  {label,-10} объявлено {count,3}   использований {useCount,4}");
        }

        int otherCount = declared.Keys.Count(n => !layers.Any(l => l.test(n)));
        int otherUseCount = uses.Where(kv => !layers.Any(l => l.test(kv.Key))).Sum(kv => kv.Value.Values.Sum());
        Console.WriteLine($"  прочее     объявлено {otherCount,3}   использований {otherUseCount,4}");

        return 0;
    }

    private static string GetRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "index.html")))
        {
            dir = Path.GetDirectoryName(dir) ?? dir;
            if (Path.GetPathRoot(dir) == dir) break;
        }
        return dir;
    }
}
