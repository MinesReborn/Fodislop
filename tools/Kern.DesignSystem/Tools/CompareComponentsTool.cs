using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class CompareComponentsTool
{
    public static int Run(string[] args)
    {
        bool check = args.Contains("--check");
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string mapPath = Path.Combine(root, "component-map.json");
        string gameStylesDir = Path.Combine(repo, "Assets", "Resources", "Styles");
        string outPath = Path.Combine(repo, "docs", "design-component-drift.md");

        var map = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(mapPath)) ?? new();
        var game = ParseRules(Directory.GetFiles(gameStylesDir, "*.uss"));
        var mirror = ParseRules(Directory.GetFiles(root, "css/*.css").Concat(Directory.GetFiles(root, "css/screens/*.css")));
        var gamePalette = BuildPalette(Directory.GetFiles(gameStylesDir, "*.uss"));
        var mirrorPalette = BuildPalette(Directory.GetFiles(root, "css/*.css"));

        var drift = new List<string>();
        var missing = new List<string>();
        int checkedCount = 0;

        foreach (var (section, entries) in map)
        {
            if (section == "_") continue;
            foreach (var (gname, mname) in entries)
            {
                string gKey = "." + gname;
                string mKey = "." + mname;
                if (!game.ContainsKey(gKey) && !mirror.ContainsKey(mKey))
                {
                    missing.Add($"{section} | .{gname} | .{mname}");
                    continue;
                }
                if (!game.ContainsKey(gKey) || !mirror.ContainsKey(mKey))
                {
                    drift.Add($"{section} | .{gname} | .{mname} | — | нет правила | нет правила | {mKey}");
                    continue;
                }

                var gBody = game[gKey];
                var mBody = mirror[mKey];
                foreach (var prop in gBody.Keys.Intersect(mBody.Keys))
                {
                    string gv = Normalize(prop, gBody[prop], gamePalette);
                    string mv = Normalize(prop, mBody[prop], mirrorPalette);
                    if (gv != mv)
                    {
                        drift.Add($"{section} | .{gname} | .{mname} | {prop} | {gv} | {mv} | {mKey}");
                    }
                    checkedCount++;
                }
            }
        }

        string rows = drift.Any() ? string.Join("\n", drift) : "| — | | | | | |";
        string gone = missing.Any() ? string.Join("\n", missing) : "| — | | |";

        var sb = new StringBuilder();
        sb.AppendLine("# Расхождения компонентов с макетом");
        sb.AppendLine($"\n{drift.Count} расхождений, {missing.Count} отсутствующих реакций на {checkedCount} сравнимых свойств.\n");
        sb.AppendLine("| секция | игра | макет | свойство | в игре | в макете |");
        sb.AppendLine("|---|---|---|---|---|---|");
        sb.AppendLine(rows);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"{Path.GetRelativePath(repo, outPath)}: {drift.Count} расхождений, {missing.Count} отсутствующих");
        return 0;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseRules(IEnumerable<string> files)
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
                if (!props.Any()) continue;

                foreach (string selector in selectors.Split(','))
                {
                    string clean = Regex.Replace(selector, @"\s+", " ").Trim();
                    string last = Regex.Replace(clean, @"(^|[\s>+~])[A-Za-z]\w*(?=[.:])", "$1").Trim();
                    if (!result.ContainsKey(last))
                    {
                        result[last] = new Dictionary<string, string>(StringComparer.Ordinal);
                    }
                    foreach (var (k, v) in props)
                    {
                        result[last][k] = v;
                    }
                }
            }
        }
        return result;
    }

    private static Dictionary<string, string> BuildPalette(IEnumerable<string> files)
    {
        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string text = Regex.Replace(File.ReadAllText(file), @"/\*[\s\S]*?\*/", " ");
            foreach (Match m in Regex.Matches(text, @"(--[\w-]+)\s*:\s*([^;{}]+)"))
            {
                palette[m.Groups[1].Value] = Regex.Replace(m.Groups[2].Value, @"\s+", " ").Trim();
            }
        }
        return palette;
    }

    private static string Normalize(string prop, string value, Dictionary<string, string> palette)
    {
        string resolved = Regex.Replace(value, @"var\(\s*(--[\w-]+)\s*\)", m =>
        {
            string name = m.Groups[1].Value;
            return palette.TryGetValue(name, out string v) ? v : m.Value;
        });
        return resolved.Replace("rgb(", "rgba(").Trim().ToLowerInvariant();
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
