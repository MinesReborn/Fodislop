using System.Text;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools.Derived;

internal static class DeriveTranslucencyTool
{
    private static readonly Regex Combo = new(@"rgb\(\s*var\(\s*(--rgb-[a-z0-9-]+)\s*\)\s*/\s*(\d+)%\s*\)", RegexOptions.IgnoreCase);
    private static readonly Regex Opaque = new(@"rgb\(\s*var\(\s*(--rgb-[a-z0-9-]+)\s*\)\s*\)", RegexOptions.IgnoreCase);

    public static int Run(string[] args)
    {
        bool apply = args.Contains("--apply");
        bool tokens = args.Contains("--tokens");
        string root = GetRoot();

        var targets = new[]
        {
            Path.Combine(root, "styles.css"),
            Path.Combine(root, "index.html"),
            Path.Combine(root, "css", "components.css"),
            Path.Combine(root, "css", "base.css"),
            Path.Combine(root, "css", "shell.css"),
        };

        var combos = new Dictionary<(string primitive, int alpha), int>();
        var opaques = new HashSet<string>();
        foreach (string f in targets)
        {
            if (!File.Exists(f)) continue;
            string text = File.ReadAllText(f);
            foreach (Match m in Combo.Matches(text))
            {
                var key = (m.Groups[1].Value, int.Parse(m.Groups[2].Value));
                combos[key] = combos.GetValueOrDefault(key) + 1;
            }
            foreach (Match m in Opaque.Matches(text))
            {
                opaques.Add(m.Groups[1].Value);
            }
        }

        var declarations = new Dictionary<string, string>();
        foreach (var ((primitive, alpha), _) in combos)
        {
            string name = TokenName(primitive, alpha >= 95 ? "solid" : "step");
            declarations[name] = alpha >= 95 ? $"var({primitive})" : $"var({primitive}) / {alpha}%";
        }

        foreach (string prim in opaques)
        {
            string name = TokenName(prim, "solid");
            declarations[name] = $"var({prim})";
        }

        if (tokens)
        {
            foreach (string name in declarations.Keys.Order())
            {
                Console.WriteLine($"  {name}: {declarations[name]};");
            }
            return 0;
        }

        Console.WriteLine($"Обращений к примитивам: {combos.Count + opaques.Count}");
        Console.WriteLine($"  с альфой: {combos.Count} в {combos.Values.Sum()} сочетаниях");
        Console.WriteLine($"  непрозрачных: {opaques.Count}");
        Console.WriteLine($"\nСемантических токенов: {declarations.Count}");

        if (apply)
        {
            foreach (string f in targets)
            {
                if (!File.Exists(f)) continue;
                string text = File.ReadAllText(f);
                foreach (var ((primitive, alpha), _) in combos)
                {
                    string name = TokenName(primitive, alpha >= 95 ? "solid" : "step");
                    string oldValue = alpha >= 95 ? $"rgb(var({primitive}))" : $"rgb(var({primitive}) / {alpha}%)";
                    text = text.Replace(oldValue, $"var({name})");
                }
                File.WriteAllText(f, text);
            }
        }

        return 0;
    }

    private static string TokenName(string primitive, string step)
    {
        string hue = primitive.Replace("--rgb-", "");
        return $"--{hue}-{step}";
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
