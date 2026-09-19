using System.Text.Json;

namespace Kern.DesignSystem.Tools;

internal static class FitEasingTool
{
    private const double BezierX1 = 0.2;
    private const double BezierY1 = 0.75;
    private const double BezierX2 = 0.2;
    private const double BezierY2 = 1.0;
    private const int Samples = 101;

    public static int Run(string[] args)
    {
        double[] correct = new double[Samples];
        double[] naive = new double[Samples];

        for (int i = 0; i < Samples; i++)
        {
            double t = (double)i / (Samples - 1);
            correct[i] = BezierYAtTime(t, (BezierX1, BezierY1, BezierX2, BezierY2));
            naive[i] = BezierAxis(t, BezierY1, BezierY2);
        }

        Console.WriteLine($"кривая: cubic-bezier({BezierX1},{BezierY1},{BezierX2},{BezierY2})   точек: {Samples}\n");

        foreach (var (label, target) in new[] { ("ПРАВИЛЬНО (решаем x(u)=t)", correct), ("ОШИБОЧНО (сравниваем y(t) напрямую)", naive) })
        {
            Console.WriteLine(label);
            Console.WriteLine($"  {"кривая",-22} {"max",9} {"rms",9}");
            var ranked = Curves.Select(kv => (Score(target, kv.Value), kv.Key))
                .OrderBy(s => s.Item1.Rms)
                .Take(5);

            foreach (var (score, name) in ranked)
            {
                Console.WriteLine($"  {name,-22} {score.Max,9:F4} {score.Rms,9:F4}");
            }

            var best = Curves.Select(kv => (Score(target, kv.Value), kv.Key))
                .OrderBy(s => s.Item1.Rms)
                .First();
            Console.WriteLine($"  -> {best.Key}\n");
        }

        var correctBest = Curves.Select(kv => (Score(correct, kv.Value), kv.Key))
            .OrderBy(s => s.Item1.Rms)
            .First();
        var circScore = Score(correct, Curves["ease-out-circ"]);
        Console.WriteLine($"утверждение витрины: ease-out-circ   max={circScore.Max:F4} rms={circScore.Rms:F4}");
        Console.WriteLine($"фактический победитель: {correctBest.Item2}   max={correctBest.Item1.Max:F4} rms={correctBest.Item1.Rms:F4}");
        Console.WriteLine("ВЕРНО" + (correctBest.Key == "ease-out-circ" ? "" : "  УТВЕРЖДЕНИЕ НЕВЕРНО"));

        return 0;
    }

    private static double BezierAxis(double u, double a, double b)
    {
        double v = 1.0 - u;
        return 3 * v * v * u * a + 3 * v * u * u * b + u * u * u;
    }

    private static double BezierYAtTime(double t, (double x1, double y1, double x2, double y2) p)
    {
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (BezierAxis(mid, p.x1, p.x2) < t) lo = mid;
            else hi = mid;
        }
        return BezierAxis((lo + hi) / 2, p.y1, p.y2);
    }

    private static (double Max, double Rms) Score(double[] target, Func<double, double> f)
    {
        double maxDiff = 0.0;
        double sumSq = 0.0;
        for (int i = 0; i < target.Length; i++)
        {
            double t = (double)i / (target.Length - 1);
            double diff = Math.Abs(f(t) - target[i]);
            maxDiff = Math.Max(maxDiff, diff);
            sumSq += diff * diff;
        }
        return (maxDiff, Math.Sqrt(sumSq / target.Length));
    }

    private static readonly Dictionary<string, Func<double, double>> Curves = new(StringComparer.Ordinal)
    {
        ["linear"] = t => t,
        ["ease-in-sine"] = t => 1 - Math.Cos(t * Math.PI / 2),
        ["ease-out-sine"] = t => Math.Sin(t * Math.PI / 2),
        ["ease-in-out-sine"] = t => -(Math.Cos(Math.PI * t) - 1) / 2,
        ["ease-in"] = t => t * t,
        ["ease-out"] = t => 1 - Math.Pow(1 - t, 2),
        ["ease-in-out"] = t => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
        ["ease"] = t => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
        ["ease-in-cubic"] = t => Math.Pow(t, 3),
        ["ease-out-cubic"] = t => 1 - Math.Pow(1 - t, 3),
        ["ease-in-out-cubic"] = t => t < 0.5 ? 4 * Math.Pow(t, 3) : 1 - Math.Pow(-2 * t + 2, 3) / 2,
        ["ease-in-circ"] = t => 1 - Math.Sqrt(Math.Max(0.0, 1 - t * t)),
        ["ease-out-circ"] = t => Math.Sqrt(Math.Max(0.0, 1 - Math.Pow(t - 1, 2))),
        ["ease-in-out-circ"] = t => t < 0.5 ? (1 - Math.Sqrt(Math.Max(0.0, 1 - Math.Pow(2 * t, 2)))) / 2 : (Math.Sqrt(Math.Max(0.0, 1 - Math.Pow(-2 * t + 2, 2))) + 1) / 2,
        ["ease-in-back"] = t => 2.70158 * t * t * t - 1.70158 * t * t,
        ["ease-out-back"] = t => 1 + 2.70158 * Math.Pow(t - 1, 3) + 1.70158 * Math.Pow(t - 1, 2),
        ["ease-in-out-back"] = t => t < 0.5 ? Math.Pow(2 * t, 2) * ((2.70158 + 1) * 2 * t - 2.70158) / 2 : (Math.Pow(2 * t - 2, 2) * ((2.70158 + 1) * (t * 2 - 2) + 2.70158) + 2) / 2,
        ["ease-in-elastic"] = t => t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * 2 * Math.PI / 3),
        ["ease-out-elastic"] = t => t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * 2 * Math.PI / 3) + 1,
        ["ease-in-out-elastic"] = t => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * 2 * Math.PI / 4.5)) / 2 : (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * 2 * Math.PI / 4.5)) / 2 + 1,
        ["ease-in-bounce"] = t => 1 - OutBounce(1 - t),
        ["ease-out-bounce"] = OutBounce,
        ["ease-in-out-bounce"] = t => t < 0.5 ? (1 - OutBounce(1 - 2 * t)) / 2 : (1 + OutBounce(2 * t - 1)) / 2,
    };

    private static double OutBounce(double t)
    {
        double n = 7.5625, d = 2.75;
        if (t < 1 / d) return n * t * t;
        if (t < 2 / d) { t -= 1.5 / d; return n * t * t + 0.75; }
        if (t < 2.5 / d) { t -= 2.25 / d; return n * t * t + 0.9375; }
        t -= 2.625 / d;
        return n * t * t + 0.984375;
    }
}
