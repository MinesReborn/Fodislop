using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools.Derived;

internal static class DeriveAlphaScaleTool
{
    private const int KMin = 3;
    private const int KMax = 15;

    public static int Run(string[] args)
    {
        string root = GetRoot();
        var counts = Measure(root);

        Console.WriteLine($"Измерено: {counts.Values.Sum()} использований, {counts.Count} различных значений альфы.");
        Console.WriteLine("Расстояние — логит; вес — частота в макете.\n");

        Console.WriteLine("Кривая ошибки:");
        Console.WriteLine($"  {"k",2}  {"ошибка",8}  {"выигрыш",8}  макс. сдвиг");
        double previous = 0;
        for (int k = KMin; k <= Math.Min(KMax, counts.Count); k++)
        {
            var (err, groups) = Partition(counts, k);
            var reps = groups.Select(g => g.DefaultIfEmpty(0).MaxBy(v => counts.GetValueOrDefault(v, 0))).ToList();
            double worst = groups.SelectMany((g, i) => g.Select(v => Math.Abs(Logit(v) - Logit(reps[i])))).Max();
            string gain = previous == 0 ? "—" : $"{previous - err,8:F3}";
            Console.WriteLine($"  {k,2}  {err,8:F3}  {gain}  {worst:F2} лог");
            previous = err;
        }

        return 0;
    }

    private static Dictionary<int, int> Measure(string root)
    {
        var counts = new Dictionary<int, int>();
        var alphaRegex = new Regex(@"rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%", RegexOptions.IgnoreCase);
        foreach (string f in Directory.GetFiles(root, "css/**/*.css", SearchOption.AllDirectories).Concat(new[] { Path.Combine(root, "styles.css"), Path.Combine(root, "index.html") }))
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in alphaRegex.Matches(File.ReadAllText(f)))
            {
                int alpha = int.Parse(m.Groups[2].Value);
                counts[alpha] = counts.GetValueOrDefault(alpha) + 1;
            }
        }
        return counts;
    }

    private static (double err, List<List<int>> groups) Partition(Dictionary<int, int> counts, int k)
    {
        var values = counts.Keys.Order().ToList();
        int n = values.Count;
        double inf = double.PositiveInfinity;

        double Cost(int i, int j)
        {
            double best = inf;
            int bestRep = values[i];
            for (int rep = i; rep < j; rep++)
            {
                double total = 0;
                for (int v = i; v < j; v++)
                {
                    total += counts.GetValueOrDefault(values[v], 0) * Math.Pow(Logit(values[v]) - Logit(values[rep]), 2);
                }
                if (total < best)
                {
                    best = total;
                    bestRep = values[rep];
                }
            }
            return best;
        }

        var dp = new double[k + 1, n + 1];
        var cut = new int[k + 1, n + 1];
        for (int i = 0; i <= k; i++)
            for (int j = 0; j <= n; j++)
                dp[i, j] = inf;
        dp[0, 0] = 0;

        for (int c = 1; c <= k; c++)
        {
            for (int j = c; j <= n; j++)
            {
                for (int i = c - 1; i < j; i++)
                {
                    if (dp[c - 1, i] == inf) continue;
                    double total = dp[c - 1, i] + Cost(i, j);
                    if (total < dp[c, j])
                    {
                        dp[c, j] = total;
                        cut[c, j] = i;
                    }
                }
            }
        }

        var groups = new List<List<int>>();
        int jj = n;
        for (int c = k; c > 0; c--)
        {
            int ii = cut[c, jj];
            groups.Add(values.GetRange(ii, jj - ii));
            jj = ii;
        }
        groups.Reverse();
        return (dp[k, n], groups);
    }

    private static double Logit(int alpha)
    {
        double a = Math.Clamp(alpha, 1, 99) / 100.0;
        return Math.Log(a / (1 - a));
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
