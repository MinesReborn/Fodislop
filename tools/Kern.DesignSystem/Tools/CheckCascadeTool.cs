using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class CheckCascadeTool
{
    public static int Run(string[] args)
    {
        bool save = args.Contains("--save");
        string root = GetRoot();
        string snapPath = Path.Combine(root, "tools", ".cascade-snapshot.json");
        string entryPath = Path.Combine(root, "styles.css");

        string css = ExpandImports(entryPath);
        var decls = ParseDeclarations(css);

        string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", decls))));

        if (save)
        {
            var snapshot = new { count = decls.Count, sha, decls };
            File.WriteAllText(snapPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"отпечаток снят: {decls.Count} объявлений, sha {sha[..12]}");
            return 0;
        }

        if (!File.Exists(snapPath))
        {
            Console.WriteLine("отпечатка нет — сначала: --save");
            return 2;
        }

        var old = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(snapPath)) ?? new();
        int oldCount = old.GetValueOrDefault("count").GetInt32();
        string oldSha = old.GetValueOrDefault("sha").GetString() ?? "";

        if (oldSha == sha)
        {
            Console.WriteLine($"каскад не изменился: {decls.Count} объявлений, sha {sha[..12]}");
            return 0;
        }

        Console.WriteLine($"КАСКАД ИЗМЕНИЛСЯ: было {oldCount}, стало {decls.Count}");
        return 1;
    }

    private static string ExpandImports(string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Expand(path, seen);

        static string Expand(string p, HashSet<string> seen)
        {
            if (!seen.Add(Path.GetFullPath(p))) return "";
            string text = File.ReadAllText(p);
            var sb = new StringBuilder();
            int pos = 0;
            foreach (Match m in Regex.Matches(text, @"@import\s+url\(['""]([^'""]+)['""]\)\s*;"))
            {
                sb.Append(text[pos..m.Index]);
                sb.Append(Expand(Path.Combine(Path.GetDirectoryName(p)!, m.Groups[1].Value), seen));
                pos = m.Index + m.Length;
            }
            sb.Append(text[pos..]);
            return sb.ToString();
        }
    }

    private static List<string> ParseDeclarations(string css)
    {
        css = Regex.Replace(css, @"/\*.*?\*/", "");
        var result = new List<string>();
        var stack = new Stack<string>();
        var buf = new StringBuilder();

        foreach (char ch in css)
        {
            if (ch == '{')
            {
                stack.Push(Regex.Replace(buf.ToString(), @"\s+", " ").Trim());
                buf.Clear();
            }
            else if (ch == '}')
            {
                foreach (string decl in buf.ToString().Split(';'))
                {
                    string d = Regex.Replace(decl, @"\s+", " ").Trim();
                    if (!string.IsNullOrEmpty(d))
                    {
                        result.Add(string.Join(" :: ", stack.Reverse()) + " :: " + d);
                    }
                }
                buf.Clear();
                if (stack.Count > 0) stack.Pop();
            }
            else
            {
                buf.Append(ch);
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
