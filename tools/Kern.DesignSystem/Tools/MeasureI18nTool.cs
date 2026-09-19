using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class MeasureI18nTool
{
    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string dictsDir = Path.Combine(repo, "Assets", "Resources", "Localization");

        if (!Directory.Exists(dictsDir))
        {
            Console.WriteLine($"нет словарей: {dictsDir}");
            return 2;
        }

        var langs = Directory.GetFiles(dictsDir, "*.json").Select(Path.GetFileNameWithoutExtension).Order().ToList();
        var keys = langs.ToDictionary(l => l, l => new HashSet<string>(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dictsDir, $"{l}.json")))!.Keys));

        string baseLang = langs.Contains("en") ? "en" : langs.First();
        Console.WriteLine($"СЛОВАРИ");
        Console.WriteLine($"  языков: {langs.Count} ({string.Join(", ", langs)})");
        foreach (string lang in langs)
        {
            int missing = keys[baseLang].Count - keys[lang].Count;
            int extra = keys[lang].Count - keys[baseLang].Count;
            Console.WriteLine($"  {lang,-4} ключей {keys[lang].Count,-5} нет от {baseLang}: {missing,-4} лишних: {extra}");
        }

        var growth = GrowthCurve(baseLang, "ru");
        Console.WriteLine("\nРОСТ ru/en ПО ДЛИНЕ ОРИГИНАЛА (символы)");
        Console.WriteLine($"  {"корзина",-8} {"n",5} {"med",6} {"p90",6} {"p99",6} {"max",6}");
        foreach (var (lo, hi) in Buckets)
        {
            var vals = growth.Per[lo];
            if (!vals.Any()) continue;
            string label = hi < 1000000 ? $"{lo}-{hi}" : $"{lo}+";
            Console.WriteLine($"  {label,-8} {vals.Count,5} {Median(vals),6:F2} {Percentile(vals, .90),6:F2} {Percentile(vals, .99),6:F2} {vals.Max(),6:F2}");
        }
        var all = growth.Per.Values.SelectMany(v => v).ToList();
        Console.WriteLine($"  {"ВСЕ",-8} {all.Count,5} {Median(all),6:F2} {Percentile(all, .90),6:F2} {Percentile(all, .99),6:F2} {all.Max(),6:F2}");

        return 0;
    }

    private static readonly List<(int lo, int hi)> Buckets = new() { (1, 5), (6, 10), (11, 20), (21, 40), (41, 1000000) };

    private static (Dictionary<int, List<double>> Per, List<(double F, string K, string S, string D)> Worst) GrowthCurve(string baseLang, string targetLang)
    {
        var baseDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(GetRoot(), "..", "..", "Assets", "Resources", "Localization", $"{baseLang}.json")))!;
        var targetDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(GetRoot(), "..", "..", "Assets", "Resources", "Localization", $"{targetLang}.json")))!;
        var per = Buckets.ToDictionary(b => b.lo, _ => new List<double>());
        var worst = new List<(double, string, string, string)>();

        foreach (var (k, src) in baseDict)
        {
            if (!targetDict.TryGetValue(k, out string dst)) continue;
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) continue;
            double f = (double)dst.Length / src.Length;
            per.First(b => b.Key <= src.Length && src.Length <= b.Key).Value.Add(f);
            worst.Add((f, k, src, dst));
        }

        worst.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        return (per, worst);
    }

    private static double Median(List<double> v)
    {
        v.Sort();
        return v[v.Count / 2];
    }

    private static double Percentile(List<double> v, double p)
    {
        v.Sort();
        return v[Math.Min((int)(p * (v.Count - 1)), v.Count - 1)];
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
