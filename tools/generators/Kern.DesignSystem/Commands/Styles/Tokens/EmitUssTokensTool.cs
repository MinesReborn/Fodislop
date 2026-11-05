using Kern.DesignSystem.Paths;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Commands.Styles;

internal static class EmitUssTokensTool
{
    // --blur-* переносится: радиусы нужны filter: blur() и drop-shadow() (Unity 6.3+).
    private static readonly string[] DropPrefix = { "--hex-", "--rgb-", "--mat-", "--layer-", "--z-" };
    private static readonly HashSet<string> DropExact = new() { "--fit-lines" };

    private static readonly Dictionary<string, string> FontAssets = new(StringComparer.Ordinal)
    {
        ["--face-body"] = "Assets/Resources/Fonts/Exo2/Exo2_SDF.asset",
        ["--face-data"] = "Assets/Resources/Fonts/JetBrainsMono/JetBrainsMono_SDF.asset",
        ["--face-display"] = "Assets/Resources/Fonts/Unbounded/Unbounded_SDF.asset",
    };

    private static readonly Dictionary<string, (string name, string? note)> Easing = new(StringComparer.Ordinal)
    {
        ["cubic-bezier(0.2,0.75,0.2,1)"] = ("ease-out-circ", "подбор fit-easing.py, max-отклонение 0.129"),
        ["cubic-bezier(0.4,0,0.2,1)"] = ("ease-in-out", null),
        ["cubic-bezier(0,0.2,0.8,1)"] = ("ease-out-cubic", null),
    };

    private static readonly Dictionary<string, string> Tiers = new(StringComparer.Ordinal)
    {
        ["max-width: 899px"] = "tier--compact",
        ["min-width: 1600px"] = "tier--wide",
    };

    public static int Run(string[] args)
    {
        bool check = args.Contains("--check");
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "../.."));
        string tokensPath = Path.Combine(root, "css/tokens.css");
        string stylesDir = Path.Combine(repo, "Assets/Resources/UI/Theme/Tokens/Styles");
        string themePath = Path.Combine(stylesDir, "ThemeTokens.uss");
        string utilsPath = Path.Combine(stylesDir, "TokenClasses.uss");
        string palettePath = Path.Combine(stylesDir, "TokenPalette.json");

        string tokensText = File.ReadAllText(tokensPath);
        var (baseTokens, tiers) = ReadBlocks(tokensText);

        var targets = new Dictionary<string, string>
        {
            [themePath] = EmitTokens(baseTokens, tiers),
            [utilsPath] = EmitUtilities(baseTokens),
            [palettePath] = EmitPalette(baseTokens),
        };

        if (check)
        {
            var stale = new List<string>();
            foreach (var (path, text) in targets)
            {
                if (!File.Exists(path) || File.ReadAllText(path) != text)
                {
                    stale.Add(path);
                }
            }

            if (stale.Any())
            {
                Console.WriteLine("ТОКЕНЫ ИГРЫ РАЗОШЛИСЬ С МАКЕТОМ:");
                foreach (string p in stale)
                {
                    Console.WriteLine($"  {Path.GetRelativePath(repo, p)}");
                }
                return 1;
            }

            Console.WriteLine($"игра совпадает с макетом ({baseTokens.Count} токенов, {tiers.Count} тиров)");
            return 0;
        }

        foreach (var (path, text) in targets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Console.WriteLine($"прочитано токенов: {baseTokens.Count}, тиров: {tiers.Count}");
        return 0;
    }

    private static (Dictionary<string, string> baseTokens, Dictionary<string, Dictionary<string, string>> tiers) ReadBlocks(string src)
    {
        var baseTokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var tiers = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        bool inMedia = false;

        foreach (string line in src.Split('\n'))
        {
            string trimmed = line.Trim();
            if (Regex.Match(trimmed, @"@media\s*\(([^)]*)\)").Success)
            {
                inMedia = true;
                string media = Regex.Match(trimmed, @"@media\s*\(([^)]*)\)").Groups[1].Value.Trim();
                if (Tiers.TryGetValue(media, out string? cls))
                {
                    current = tiers[cls] = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                else
                {
                    current = null;
                }
                continue;
            }
            if (trimmed == ":root {")
            {
                if (!inMedia) current = baseTokens;
                continue;
            }
            if (trimmed.StartsWith("}"))
            {
                inMedia = false;
                current = null;
                continue;
            }
            if (current == null) continue;

            var decl = Regex.Match(trimmed, @"(--[\w-]+)\s*:\s*([^;]+);");
            if (decl.Success)
            {
                string name = decl.Groups[1].Value;
                string value = decl.Groups[2].Value.Trim();
                current[name] = value;
            }
        }

        return (baseTokens, tiers);
    }

    private static string Resolve(string value, Dictionary<string, string> table, int depth = 0)
    {
        if (depth > 12) return value;
        string result = Regex.Replace(value, @"var\(\s*(--[\w-]+)\s*\)", m =>
        {
            string name = m.Groups[1].Value.Trim();
            return table.ContainsKey(name) ? Resolve(table[name], table, depth + 1) : m.Value;
        });
        return result.Trim();
    }

    private static string? Convert(string name, string value)
    {
        if (DropExact.Contains(name) || DropPrefix.Any(p => name.StartsWith(p)))
            return null;

        if (FontAssets.TryGetValue(name, out string? path))
        {
            if (!File.Exists(Path.Combine(GetRepoRoot(), path)))
            {
                Console.Error.WriteLine($"нет SDF-ассета для {name}: {path}");
                Environment.Exit(1);
            }
            return $"url(\"project://database/{path}\")";
        }

        string flat = value.Replace(" ", "");
        if (Easing.TryGetValue(flat, out var eased))
        {
            return eased.note != null ? $"{eased.name}  /* {eased.note} */" : eased.name;
        }
        if (value.Contains("cubic-bezier"))
        {
            Console.Error.WriteLine($"{name}: кривая {value} не подобрана");
            Environment.Exit(1);
        }

        if (Regex.Match(value, @"^\d+\s+\S+\s*/\s*\S+\s").Success)
            return null;

        if (Regex.IsMatch(value, @"[\d.]+(em|rem|ch|ex|vw|vh|vmin|vmax)\b"))
            return null;

        var color = Regex.Match(value, @"^rgb\(\s*(\d+)\s+(\d+)\s+(\d+)(?:\s*/\s*([\d.]+)%)?\s*\)$");
        if (color.Success)
        {
            if (color.Groups[4].Success)
            {
                double alpha = double.Parse(color.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) / 100;
                return $"rgba({color.Groups[1].Value}, {color.Groups[2].Value}, {color.Groups[3].Value}, {alpha.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            }

            return $"rgb({color.Groups[1].Value}, {color.Groups[2].Value}, {color.Groups[3].Value})";
        }

        return value;
    }

    private static string EmitTokens(Dictionary<string, string> baseTokens, Dictionary<string, Dictionary<string, string>> tiers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/* ФАЙЛ МАШИННЫЙ. Правки будут затёрты.");
        sb.AppendLine("   Источник истины: visual/kern-ui-lab/css/tokens.css");
        sb.AppendLine("   Генератор: tools/generators/Kern.DesignSystem/Commands/Styles/Tokens/EmitUssTokensTool.cs");
        sb.AppendLine("   Расхождение ловит CI. */");
        sb.AppendLine(":root {");

        var dropped = new List<string>();
        foreach (var (name, raw) in baseTokens)
        {
            string resolved = Resolve(raw, baseTokens);
            string? got = Convert(name, resolved);
            if (got == null)
            {
                dropped.Add(name);
                continue;
            }
            sb.AppendLine($"    {name}: {got};");
        }
        sb.AppendLine("}");

        foreach (var (cls, table) in tiers)
        {
            sb.AppendLine();
            sb.AppendLine($"/* Тир задаётся классом на корневом элементе. */");
            sb.AppendLine($".{cls} {{");
            foreach (var (name, raw) in table)
            {
                string resolved = Resolve(raw, baseTokens);
                string? got = Convert(name, resolved);
                if (got != null)
                {
                    sb.AppendLine($"    {name}: {got};");
                }
            }
            sb.AppendLine("}");
        }

        sb.AppendLine();
        EmitTextScales(sb, baseTokens, tiers);
        sb.AppendLine($"/* Не переносится в USS ({dropped.Count}): {string.Join(", ", dropped.Select(n => n.TrimStart('-')))} */");
        return sb.ToString();
    }

    private static void EmitTextScales(StringBuilder sb, Dictionary<string, string> baseTokens,
        Dictionary<string, Dictionary<string, string>> tiers)
    {
        var sizes = baseTokens.Where(pair => pair.Key.StartsWith("--size-", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        sizes.Add("--size-fixed-18", "18px");
        sizes.Add("--size-fixed-20", "20px");
        sb.AppendLine(":root { --size-fixed-18: 18px; --size-fixed-20: 20px; }");
        for (int percent = 75; percent <= 200; percent += 5)
        {
            foreach (string tier in new[] { "tier--standard", "tier--compact", "tier--wide" })
            {
                sb.AppendLine($".{tier}.text-scale--{percent} {{");
                foreach (var (name, baseline) in sizes)
                {
                    string value = tiers.TryGetValue(tier, out var overrides) && overrides.TryGetValue(name, out string? adjusted)
                        ? adjusted : baseline;
                    float pixels = float.Parse(value.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture);
                    string scaled = (pixels * percent / 100f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    sb.AppendLine($"    {name}: {scaled}px;");
                }
                sb.AppendLine("}");
            }
        }
    }

    private static string EmitUtilities(Dictionary<string, string> baseTokens)
    {
        return @"/* ФАЙЛ МАШИННЫЙ. Правки будут затёрты. */
.is-hidden { display: none; }
.row { flex-direction: row; }
.row-reverse { flex-direction: row-reverse; }
.col { flex-direction: column; }
.col-reverse { flex-direction: column-reverse; }
.ai-start { align-items: flex-start; }
.ai-center { align-items: center; }
.ai-end { align-items: flex-end; }
.ai-stretch { align-items: stretch; }
.jc-start { justify-content: flex-start; }
.jc-center { justify-content: center; }
.jc-end { justify-content: flex-end; }
.jc-between { justify-content: space-between; }
.jc-around { justify-content: space-around; }
.as-start { align-self: flex-start; }
.as-center { align-self: center; }
.as-end { align-self: flex-end; }
.as-stretch { align-self: stretch; }
.abs { position: absolute; }
.rel { position: relative; }
.centered {
    position: absolute;
    left: 50%;
    top: 50%;
    translate: -50% -50%;
}
.fit-wrap { white-space: normal; min-width: 0; }
.fit-atomic { white-space: nowrap; flex-shrink: 0; flex-grow: 0; }
.fit-clip { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; -unity-text-overflow-position: end; min-width: 0; }
.fit-clamp { white-space: normal; overflow: hidden; text-overflow: ellipsis; min-width: 0; }
.fit-shrink { white-space: nowrap; overflow: hidden; min-width: 0; -unity-text-auto-size: best-fit var(--size-micro) var(--size-lg); }
.grow { flex-grow: 1; }
.no-grow { flex-grow: 0; }
.no-shrink { flex-shrink: 0; }
";
    }

    private static string EmitPalette(Dictionary<string, string> baseTokens)
    {
        var colors = new Dictionary<string, double[]>();
        var space = new Dictionary<string, int>();

        foreach (var (name, raw) in baseTokens)
        {
            if (DropPrefix.Any(p => name.StartsWith(p))) continue;
            string resolved = Resolve(raw, baseTokens);
            var m = Regex.Match(resolved, @"^rgb\(\s*(\d+)\s+(\d+)\s+(\d+)(?:\s*/\s*([\d.]+)%)?\s*\)$");
            if (m.Success)
            {
                double alpha = m.Groups[4].Success
                    ? double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) / 100
                    : 1;
                colors[name] = new[] { double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), alpha };
                continue;
            }
            var hex = Regex.Match(resolved, @"#([0-9a-fA-F]{6})");
            if (hex.Success)
            {
                string h = hex.Groups[1].Value;
                colors[name] = new double[] { int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber), int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber), int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber), 1 };
                continue;
            }
            if (name.StartsWith("--space-"))
            {
                var px = Regex.Match(resolved, @"(\d+)px");
                if (px.Success) space[name] = int.Parse(px.Groups[1].Value);
            }
        }

        var palette = new Dictionary<string, object>
        {
            ["_"] = "Машинный файл. Источник visual/kern-ui-lab/css/tokens.css, генератор tools/generators/Kern.DesignSystem/Commands/Styles/Tokens/EmitUssTokensTool.cs.",
            ["colors"] = colors.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => (object)kv.Value),
            ["space"] = space.OrderBy(kv => kv.Value).ToDictionary(kv => kv.Key, kv => (object)kv.Value),
        };

        return JsonSerializer.Serialize(palette, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";
    }

    private static string GetRoot() => DesignSystemPaths.GetRoot();

    private static string GetRepoRoot()
    {
        string dir = GetRoot();
        return Path.GetFullPath(Path.Combine(dir, "../.."));
    }
}
