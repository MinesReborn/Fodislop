using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class CheckFitTool
{
    private static readonly Dictionary<string, Dictionary<string, string>> Required = new(StringComparer.Ordinal)
    {
        ["wrap"] = new() { ["white-space"] = "normal" },
        ["atomic"] = new() { ["white-space"] = "nowrap", ["flex-shrink"] = "0" },
        ["clip"] = new() { ["white-space"] = "nowrap", ["overflow"] = "hidden", ["text-overflow"] = "ellipsis" },
        ["clamp"] = new() { ["white-space"] = "normal", ["overflow"] = "hidden", ["text-overflow"] = "ellipsis" },
        ["shrink"] = new() { ["-unity-text-auto-size"] = "" },
    };

    private const int Floor = 8;
    private static readonly HashSet<string> Utility = new() { "fit-wrap", "fit-atomic", "fit-clip", "fit-clamp", "fit-shrink" };

    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string mapPath = Path.Combine(root, "component-map.json");
        string gameStylesDir = Path.Combine(repo, "Assets", "Resources", "Styles");
        string uxmlDir = Path.Combine(repo, "Assets", "Resources", "UI");

        var map = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(mapPath)) ?? new();
        var toGame = map.SelectMany(s => s.Value).ToDictionary(kv => kv.Key, kv => kv.Value);

        var ussFiles = Directory.GetFiles(gameStylesDir, "*.uss");
        var uss = ParseUssRules(ussFiles);

        var uxml = new Dictionary<string, HashSet<string>>();
        foreach (string uxmlFile in Directory.GetFiles(uxmlDir, "*.uxml", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(uxmlFile), @"class=""([^""]+)"""))
            {
                string[] names = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string n in names)
                {
                    if (!uxml.TryGetValue(n, out var set))
                    {
                        set = new HashSet<string>();
                        uxml[n] = set;
                    }
                    set.UnionWith(names);
                }
            }
        }

        var variants = new Dictionary<string, HashSet<string>>();
        foreach (string htmlFile in new[] { Path.Combine(root, "index.html") })
        {
            string html = File.ReadAllText(htmlFile);
            foreach (Match m in Regex.Matches(html, @"class=""([^""]+)"""))
            {
                string[] classes = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string c in classes)
                {
                    if (toGame.TryGetValue(c, out string gname))
                    {
                        if (!variants.TryGetValue(gname, out var fits))
                        {
                            fits = new HashSet<string>();
                            variants[gname] = fits;
                        }
                        fits.UnionWith(classes.Where(c => Required.ContainsKey(c)));
                    }
                }
            }
        }

        int paired = variants.Sum(kv => kv.Value.Count);
        int gaps = 0;

        foreach (var (gname, fits) in variants)
        {
            var uses = uxml.GetValueOrDefault(gname, new HashSet<string>());
            if (fits.Count > 1)
            {
                int bare = uses.Count(s => !s.Split(' ').Any(u => Utility.Contains(u)));
                if (bare > 0)
                {
                    Console.WriteLine($"  .{gname} ({string.Join("/", fits.OrderBy(f => f))}): {bare} элементов без утилиты fit-*");
                    gaps++;
                }
                continue;
            }

            string fit = fits.FirstOrDefault();
            if (string.IsNullOrEmpty(fit)) continue;

            var have = uss.GetValueOrDefault(gname, new Dictionary<string, string>());
            if (uses.Count > 0 && uses.All(s => s.Split(' ').Any(u => Utility.Contains(u))))
            {
                var clash = Required[fit].Keys.Intersect(have.Keys).ToList();
                if (clash.Any())
                {
                    Console.WriteLine($"  .{gname} (fit={fit}): утилиту перебивает правило класса");
                    gaps++;
                }
                continue;
            }

            foreach (var (prop, want) in Required[fit])
            {
                string got = have.GetValueOrDefault(prop);
                if (got == null)
                {
                    Console.WriteLine($"  .{gname} (fit={fit}): {prop} = не сказано");
                    gaps++;
                }
                else if (want != null && got != want)
                {
                    Console.WriteLine($"  .{gname} (fit={fit}): {prop} = {got}, нужно {want}");
                    gaps++;
                }
            }
        }

        Console.WriteLine($"контракт data-fit: {paired} пар класс↔вариант, {gaps} пропусков");
        return paired < Floor ? 1 : (gaps == 0 ? 0 : 1);
    }

    private static Dictionary<string, Dictionary<string, string>> ParseUssRules(string[] files)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"([^{]+)\{([^}]*)\}", RegexOptions.Singleline))
            {
                string selectors = m.Groups[1].Value;
                string body = m.Groups[2].Value;
                var props = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string decl in body.Split(';'))
                {
                    string[] kv = decl.Split(':', 2);
                    if (kv.Length == 2)
                    {
                        props[kv[0].Trim()] = kv[1].Trim();
                    }
                }

                foreach (string selector in selectors.Split(','))
                {
                    string clean = Regex.Replace(selector, @"\s+", " ").Trim();
                    string last = clean.Split(' ', '+', '>', '~').Last().Trim();
                    var classMatch = Regex.Match(last, @"\.([\w-]+)");
                    if (classMatch.Success)
                    {
                        string className = classMatch.Groups[1].Value;
                        if (!result.TryGetValue(className, out var existing))
                        {
                            existing = new Dictionary<string, string>(StringComparer.Ordinal);
                            result[className] = existing;
                        }
                        foreach (var (k, v) in props)
                        {
                            existing[k] = v;
                        }
                    }
                }
            }
        }
        return result;
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
