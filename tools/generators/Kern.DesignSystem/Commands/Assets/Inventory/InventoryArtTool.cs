#nullable enable

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace Kern.DesignSystem.Commands.Assets;

internal static class InventoryArtTool
{
    private const int HueSectors = 12;
    private const int MaxTextBytes = 4 * 1024 * 1024;
    private static readonly Regex GuidRegex = new(@"\b[0-9a-f]{32}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".prefab", ".unity", ".asset", ".mat", ".controller",
        ".uxml", ".json", ".xml", ".anim", ".playable", ".txt",
    };

    private static readonly Dictionary<string, (string Kind, object? Value)> Metrics = new(StringComparer.Ordinal)
    {
        ["Assets/Textures/Server/Cells"] = ("multiple", 32),
        ["Assets/Textures/Items"] = ("exact", (42, 42)),
        ["Assets/Textures/Server/Crystals"] = ("uniform", null),
        ["Assets/Textures/Server/Pack"] = ("multiple", 32),
        ["Assets/Resources/UI/HUD/Player/Skills/Icons"] = ("exact", (73, 73)),
    };

    public static int Run(string[] args)
    {
        string root = FindRoot();
        string assets = Path.Combine(root, "Assets");
        string document = Path.Combine(root, "docs/design/design-art-inventory.md");
        List<string> pngs = Directory.Exists(assets)
            ? Directory.EnumerateFiles(assets, "*.png", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("Library"))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

        if (pngs.Count == 0)
        {
            Console.Error.WriteLine("PNG не найдены");
            return 2;
        }

        (HashSet<string> guids, string code, int scanned) = BuildReferenceIndex(assets);
        Dictionary<string, List<Item>> families = new(StringComparer.Ordinal);
        List<Item> rows = [];
        foreach (string png in pngs)
        {
            Item item = Measure(png, root);
            string? guid = ReadGuid(png);
            string stem = Path.GetFileNameWithoutExtension(png).ToLowerInvariant();
            bool distinctive = stem.Length >= 4 && stem.Any(char.IsLetter);
            string reference = guid != null && guids.Contains(guid)
                ? "guid"
                : distinctive && code.Contains(stem, StringComparison.Ordinal)
                    ? "имя"
                    : "нет";
            item.Reference = reference;
            item.Referenced = reference != "нет";
            if (!families.TryGetValue(item.Family, out List<Item>? familyItems))
            {
                familyItems = [];
                families[item.Family] = familyItems;
            }

            familyItems.Add(item);
            rows.Add(item);
        }

        foreach ((string family, List<Item> items) in families)
        {
            (string kind, _) = MetricFor(family);
            if (kind != "uniform")
            {
                continue;
            }

            (int width, int height) mode = items
                .GroupBy(item => (item.Width, item.Height))
                .OrderByDescending(group => group.Count())
                .First()
                .Key;
            foreach (Item item in items.Where(item => (item.Width, item.Height) != mode))
            {
                item.Metric = $"violation (не {mode.width}x{mode.height})";
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(document)!);
        File.WriteAllText(document, BuildDocument(rows, families, scanned), new UTF8Encoding(false));

        int violations = rows.Count(row => row.Metric.StartsWith("violation", StringComparison.Ordinal));
        int unused = rows.Count(row => !row.Referenced);
        Console.WriteLine($"PNG: {rows.Count} в {families.Count} семействах");
        Console.WriteLine($"вне метрики: {violations}");
        Console.WriteLine($"без литеральных ссылок: {unused}");
        Console.WriteLine($"записано: {Path.GetRelativePath(root, document)}");
        return 0;
    }

    private static Item Measure(string path, string root)
    {
        ImageResult image;
        using (FileStream input = File.OpenRead(path))
        {
            image = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
        }

        int minX = image.Width;
        int minY = image.Height;
        int maxX = -1;
        int maxY = -1;
        List<(double r, double g, double b, double weight)> pixels = [];
        List<byte> edgeAlpha = [];

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int pixelIndex = (y * image.Width + x) * 4;
                byte red = image.Data[pixelIndex];
                byte green = image.Data[pixelIndex + 1];
                byte blue = image.Data[pixelIndex + 2];
                byte alpha = image.Data[pixelIndex + 3];
                if (alpha == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                double weight = alpha / 255.0;
                pixels.Add((red / 255.0, green / 255.0, blue / 255.0, weight));
            }
        }

        bool empty = maxX < 0;
        double coverage = empty ? 0 : (maxX - minX + 1) * (maxY - minY + 1) / (double)(image.Width * image.Height);
        double softness = 0;
        double hue = 0;
        double saturation = 0;
        double value = 0;
        if (!empty)
        {
            for (int x = minX; x <= maxX; x++)
            {
                edgeAlpha.Add(image.Data[(minY * image.Width + x) * 4 + 3]);
                edgeAlpha.Add(image.Data[(maxY * image.Width + x) * 4 + 3]);
                if (minY > 0)
                {
                    edgeAlpha.Add(image.Data[((minY - 1) * image.Width + x) * 4 + 3]);
                }
            }

            for (int y = minY; y <= maxY; y++)
            {
                edgeAlpha.Add(image.Data[(y * image.Width + minX) * 4 + 3]);
                edgeAlpha.Add(image.Data[(y * image.Width + maxX) * 4 + 3]);
            }

            List<byte> partial = edgeAlpha.Where(alpha => alpha is > 0 and < 255).ToList();
            if (partial.Count > 0)
            {
                softness = partial.Average(alpha => (1.0 - Math.Abs(2.0 * alpha / 255.0 - 1.0)) * 2.0);
            }

            double weightSum = pixels.Sum(pixel => pixel.weight);
            double cosine = 0;
            double sine = 0;
            foreach ((double r, double g, double b, double weight) in pixels)
            {
                double maximum = Math.Max(r, Math.Max(g, b));
                double minimum = Math.Min(r, Math.Min(g, b));
                double delta = maximum - minimum;
                value += maximum * weight;
                saturation += (maximum > 0 ? delta / maximum : 0) * weight;
                if (delta <= 0)
                {
                    continue;
                }

                double rawHue = maximum == r
                    ? ((g - b) / delta % 6.0)
                    : maximum == g
                        ? (b - r) / delta + 2.0
                        : (r - g) / delta + 4.0;
                double angle = rawHue * Math.PI / 3.0;
                cosine += Math.Cos(angle) * weight;
                sine += Math.Sin(angle) * weight;
            }

            hue = (Math.Atan2(sine, cosine) * 180.0 / Math.PI + 360.0) % 360.0;
            saturation /= weightSum;
            value /= weightSum;
        }

        string family = Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/');
        (string kind, object? metricValue) = MetricFor(family);
        return new Item
        {
            Name = Path.GetRelativePath(root, path).Replace('\\', '/'),
            Family = family,
            Width = image.Width,
            Height = image.Height,
            Metric = MetricState(kind, metricValue, image.Width, image.Height),
            Coverage = coverage,
            Softness = softness,
            Margins = empty ? (0, 0, 0, 0) : (minX, minY, image.Width - 1 - maxX, image.Height - 1 - maxY),
            Hue = empty ? null : hue,
            Saturation = saturation,
            Value = value,
            Empty = empty,
        };
    }

    private static (HashSet<string> Guids, string Code, int Scanned) BuildReferenceIndex(string assets)
    {
        HashSet<string> guids = new(StringComparer.OrdinalIgnoreCase);
        List<string> code = [];
        int scanned = 0;
        foreach (string path in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
        {
            if (!TextExtensions.Contains(Path.GetExtension(path)) || new FileInfo(path).Length > MaxTextBytes)
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }

            scanned++;
            foreach (Match match in GuidRegex.Matches(text))
            {
                guids.Add(match.Value);
            }

            if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                code.Add(text.ToLowerInvariant());
            }
        }

        return (guids, string.Join('\n', code), scanned);
    }

    private static string? ReadGuid(string path)
    {
        string meta = path + ".meta";
        if (!File.Exists(meta))
        {
            return null;
        }

        Match match = GuidRegex.Match(File.ReadAllText(meta));
        return match.Success ? match.Value : null;
    }

    private static (string Kind, object? Value) MetricFor(string family)
    {
        string? best = Metrics.Keys
            .Where(prefix => family == prefix || family.StartsWith(prefix + "/", StringComparison.Ordinal))
            .OrderByDescending(prefix => prefix.Length)
            .FirstOrDefault();
        return best == null ? ("none", null) : Metrics[best];
    }

    private static string MetricState(string kind, object? value, int width, int height) => kind switch
    {
        "exact" when value is ValueTuple<int, int> exact => (width, height) == exact ? "ok" : $"violation ({exact.Item1}x{exact.Item2})",
        "multiple" when value is int multiple => width % multiple == 0 && height % multiple == 0 ? "ok" : $"violation (не кратно {multiple})",
        _ => "—",
    };

    private static string BuildDocument(List<Item> rows, Dictionary<string, List<Item>> families, int scanned)
    {
        StringBuilder lines = new();
        lines.AppendLine("# Инвентарь игрового арта");
        lines.AppendLine();
        lines.AppendLine("Файл машинный: правки затираются. Пересобрать:");
        lines.AppendLine();
        lines.AppendLine("```");
        lines.AppendLine("dotnet run --project tools/generators/Kern.DesignSystem -- inventory-art");
        lines.AppendLine("```");
        lines.AppendLine();
        lines.AppendLine($"Всего PNG: **{rows.Count}** в {families.Count} семействах. Текстовых ассетов прочитано: {scanned}.");
        lines.AppendLine();
        lines.AppendLine("## Сводка по семействам");
        lines.AppendLine();
        lines.AppendLine("| семейство | шт | размеры | метрика | занятость | мягкость | оттенок, секторов | насыщ., разброс | без ссылок |");
        lines.AppendLine("| --- | ---: | --- | ---: | --- | --- | ---: | ---: | ---: |");
        foreach (string family in families.Keys.Order(StringComparer.Ordinal))
        {
            List<Item> items = families[family];
            List<IGrouping<(int Width, int Height), Item>> sizes = items.GroupBy(item => (item.Width, item.Height)).OrderByDescending(group => group.Count()).Take(4).ToList();
            string sizeText = string.Join(", ", sizes.Take(3).Select(group => $"{group.Key.Width}x{group.Key.Height}"));
            if (sizes.Count > 3)
            {
                sizeText += $" … (+{sizes.Count - 3})";
            }

            int violations = items.Count(item => item.Metric.StartsWith("violation", StringComparison.Ordinal));
            double minCoverage = items.Min(item => item.Coverage);
            double maxCoverage = items.Max(item => item.Coverage);
            double minSoftness = items.Min(item => item.Softness);
            double maxSoftness = items.Max(item => item.Softness);
            int hueCount = items.Where(item => item.Hue.HasValue).Select(item => (int)(item.Hue!.Value / (360.0 / HueSectors)) % HueSectors).Distinct().Count();
            List<double> saturation = items.Where(item => !item.Empty).Select(item => item.Saturation).ToList();
            double saturationSpread = saturation.Count == 0 ? 0 : saturation.Max() - saturation.Min();
            int unused = items.Count(item => !item.Referenced);
            lines.AppendLine($"| `{family}` | {items.Count} | {sizeText} | {(violations == 0 ? "ok" : $"**{violations}**")} | {minCoverage:F2}–{maxCoverage:F2} | {minSoftness:F2}–{maxSoftness:F2} | {hueCount}/{HueSectors} | {saturationSpread:F2} | {unused}/{items.Count} |");
        }

        lines.AppendLine();
        lines.AppendLine("Колонки «метрика» и «без ссылок» отвечают числом: жирное число — столько ассетов просит объяснения, `ok` — объяснять нечего.");
        lines.AppendLine();
        lines.AppendLine("«Без ссылок» — не приговор. Programmator и Cells грузятся вычисленным путём, поэтому литеральной ссылки у них нет по устройству. Выход за метрику — тоже не всегда дефект: числа нужны арт-библии, а не приговору.");
        lines.AppendLine();
        lines.AppendLine("## Разбивка по оттенку");
        lines.AppendLine();
        lines.AppendLine("Сектор — 30°. Считаются только непрозрачные пиксели.");
        lines.AppendLine();
        lines.AppendLine("| семейство | " + string.Join(" | ", Enumerable.Range(0, HueSectors).Select(index => $"{index * 30}–{index * 30 + 30}°")) + " |");
        lines.AppendLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", HueSectors)));
        foreach (string family in families.Keys.Order(StringComparer.Ordinal))
        {
            int[] buckets = new int[HueSectors];
            foreach (Item item in families[family].Where(item => item.Hue.HasValue))
            {
                buckets[(int)(item.Hue!.Value / (360.0 / HueSectors)) % HueSectors]++;
            }

            lines.AppendLine($"| `{family}` | {string.Join(" | ", buckets)} |");
        }

        lines.AppendLine();
        lines.AppendLine("## Все файлы");
        lines.AppendLine();
        lines.AppendLine("Занятость — площадь непрозрачного bbox к площади холста. Мягкость — насколько рамка bbox полупрозрачна. Поля — отступы bbox от краёв. Ссылка: `guid`, `имя` или `нет`.");
        lines.AppendLine();
        lines.AppendLine("| файл | размер | метрика | занятость | мягкость | поля (л,в,п,н) | оттенок | насыщ. | светлота | ссылка |");
        lines.AppendLine("| --- | --- | --- | ---: | ---: | --- | ---: | ---: | ---: | --- |");
        foreach (Item item in rows.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            string hue = item.Hue.HasValue ? $"{item.Hue.Value:F0}°" : "—";
            lines.AppendLine($"| `{item.Name}` | {item.Width}x{item.Height} | {item.Metric} | {item.Coverage:F2} | {item.Softness:F2} | {item.Margins.Left}, {item.Margins.Top}, {item.Margins.Right}, {item.Margins.Bottom} | {hue} | {item.Saturation:F2} | {item.Value:F2} | {item.Reference} |");
        }

        return lines.ToString();
    }

    private static string FindRoot()
    {
        string directory = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(directory, "AGENTS.md")) && Directory.GetParent(directory) != null)
        {
            directory = Directory.GetParent(directory)!.FullName;
        }

        return directory;
    }

    private sealed class Item
    {
        public string Name { get; init; } = string.Empty;
        public string Family { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public string Metric { get; set; } = "—";
        public double Coverage { get; init; }
        public double Softness { get; init; }
        public (int Left, int Top, int Right, int Bottom) Margins { get; init; }
        public double? Hue { get; init; }
        public double Saturation { get; init; }
        public double Value { get; init; }
        public bool Empty { get; init; }
        public string Reference { get; set; } = "нет";
        public bool Referenced { get; set; }
    }
}
