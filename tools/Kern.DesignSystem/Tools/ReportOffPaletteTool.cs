using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class ReportOffPaletteTool
{
    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string palettePath = Path.Combine(repo, "Assets", "Resources", "Styles", "token-palette.json");
        string stylesDir = Path.Combine(repo, "Assets", "Resources", "Styles");
        string outPath = Path.Combine(repo, "docs", "design-debt-uss.md");

        var palette = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(palettePath)) ?? new();
        var colors = palette.GetValueOrDefault("colors");
        if (colors.ValueKind == JsonValueKind.Undefined)
        {
            colors = JsonDocument.Parse("{}").RootElement;
        }
        var known = new HashSet<string>();
        foreach (var kv in colors.EnumerateObject())
        {
            var arr = kv.Value;
            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 3)
            {
                int r = arr[0].GetInt32();
                int g = arr[1].GetInt32();
                int b = arr[2].GetInt32();
                double a = arr.GetArrayLength() >= 4 ? arr[3].GetDouble() : 1.0;
                known.Add($"{r},{g},{b},{Math.Round(a, 3)}");
            }
        }

        var shared = new[] { "Theme.uss", "SciFi.uss", "Animations.uss", "Panel.uss", "Button.uss", "Input.uss", "Auth.uss" };
        var off = new Dictionary<string, int>(StringComparer.Ordinal);
        var where = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string name in shared)
        {
            string path = Path.Combine(stylesDir, name);
            if (!File.Exists(path)) continue;
            string text = Regex.Replace(File.ReadAllText(path), @"/\*[\s\S]*?\*/", " ");
            foreach (Match m in Regex.Matches(text, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)"))
            {
                string key = $"{m.Groups[1].Value},{m.Groups[2].Value},{m.Groups[3].Value},{Math.Round(m.Groups[4].Success ? double.Parse(m.Groups[4].Value) : 1.0, 3)}";
                if (known.Contains(key))
                {
                    off[m.Value] = off.GetValueOrDefault(m.Value) + 1;
                    if (!where.TryGetValue(m.Value, out var set))
                    {
                        set = new HashSet<string>();
                        where[m.Value] = set;
                    }
                    set.Add(name);
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Цвета общего слоя вне палитры");
        sb.AppendLine($"\n{off.Count} различных значений, {off.Values.Sum()} записей.\n");

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"{Path.GetRelativePath(repo, outPath)}: {off.Count} различных");
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
