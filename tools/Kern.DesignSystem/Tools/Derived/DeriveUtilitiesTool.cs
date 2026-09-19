using System.Text;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools.Derived;

internal static class DeriveUtilitiesTool
{
    public static int Run(string[] args)
    {
        string root = GetRoot();
        var utilFiles = new[] { Path.Combine(root, "css", "base.css"), Path.Combine(root, "css", "components.css") };

        var tokMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(root, "css", "tokens.css")), @"^\s*(--[\w-]+)\s*:\s*([^;]+);", RegexOptions.Multiline))
        {
            tokMap[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        string Resolve(string value, int depth = 0)
        {
            if (depth > 6) return value;
            return Regex.Replace(value, @"var\((--[\w-]+)\)", m =>
            {
                string name = m.Groups[1].Value;
                return tokMap.ContainsKey(name) ? Resolve(tokMap[name], depth + 1) : m.Value;
            }).Trim();
        }

        string Canon(string decl)
        {
            string[] kv = decl.Split(':', 2);
            return $"{kv[0].Trim()}:{Regex.Replace(Resolve(kv[1].Trim()), @"\s+", " ")}";
        }

        var utilities = new Dictionary<string, HashSet<string>>();
        foreach (string file in utilFiles)
        {
            if (!File.Exists(file)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^(\.fdn-[\w-]+)\s*\{([^}]*)\}", RegexOptions.Multiline))
            {
                var ds = new HashSet<string>();
                foreach (string d in m.Groups[2].Value.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    ds.Add(Canon(d));
                }
                if (ds.Any()) utilities[m.Groups[1].Value] = ds;
            }
        }

        string html = File.ReadAllText(Path.Combine(root, "index.html"));
        var sets = Regex.Matches(html, @"\sstyle=""([^""]*)""").Select(m => m.Groups[1].Value).ToList();

        int full = 0, partial = 0, bare = 0;
        var residue = new Dictionary<string, int>();
        foreach (string s in sets)
        {
            var target = new HashSet<string>(s.Split(';').Where(d => !string.IsNullOrWhiteSpace(d)).Select(Canon));
            var chosen = new List<string>();
            var rest = new HashSet<string>(target);
            foreach (var (name, ds) in utilities.OrderByDescending(kv => kv.Value.Count))
            {
                if (ds.Count > 0 && ds.All(rest.Contains))
                {
                    chosen.Add(name);
                    rest.ExceptWith(ds);
                }
            }

            if (!rest.Any()) full++;
            else if (chosen.Any()) partial++;
            else bare++;

            foreach (string d in rest)
            {
                residue[d] = residue.GetValueOrDefault(d) + 1;
            }
        }

        Console.WriteLine($"инлайн-наборов: {sets.Count}");
        Console.WriteLine($"  выражаются существующими утилитами целиком : {full}");
        Console.WriteLine($"  выражаются частично                        : {partial}");
        Console.WriteLine($"  не выражаются вовсе                        : {bare}");
        Console.WriteLine($"\nостаток: {residue.Values.Sum()} объявлений, {residue.Count} различных");

        Console.WriteLine("\nКАНДИДАТЫ В УТИЛИТЫ (встретились ≥2 раз):");
        foreach (var (d, n) in residue.OrderByDescending(kv => kv.Value))
        {
            if (n >= 2) Console.WriteLine($"  ×{n,-3} {d}");
        }

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
