using Kern.DesignSystem.Paths;

using System.Text;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Commands.Styles;

// Токены игры — из макета. Перевод CSS → USS уже сделан один раз: visual/kern-brand/tokens/export/tokens.uss
// (build-tokens.mjs из visual/kern-ui/css/tokens.css, проверяется его --check). Здесь второй перевод не пишется:
// блок :root берётся как есть, к нему добавляются шрифты — в USS это FontAsset, а не имя гарнитуры.
internal static class EmitUssTokensTool
{
    private static readonly (string Name, string Asset, string Note)[] Faces =
    {
        ("--face-display", "Assets/Resources/Fonts/Exo2/Exo2_SDF.asset", "заголовки, название, ник"),
        ("--face-text", "Assets/Resources/Fonts/Onest/Onest_SDF.asset", "текст, подписи, кнопки"),
        ("--face-mono", "Assets/Resources/Fonts/JetBrainsMono/JetBrainsMono_SDF.asset", "числа, координаты, метки"),
    };

    public static int Run(string[] args)
    {
        bool check = args.Contains("--check");
        string root = DesignSystemPaths.GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "../.."));
        string source = Path.Combine(repo, "visual/kern-brand/tokens/export/tokens.uss");
        string target = Path.Combine(repo, "Assets/Resources/UI/Theme/Tokens/Styles/KernTokens.uss");

        Match block = Regex.Match(File.ReadAllText(source), @":root \{\n(?<body>.*?)\n\}", RegexOptions.Singleline);
        if (!block.Success)
        {
            Console.Error.WriteLine($"в {Path.GetRelativePath(repo, source)} нет блока :root");
            return 1;
        }

        var sb = new StringBuilder();
        sb.Append("/* ФАЙЛ МАШИННЫЙ. Правки будут затёрты.\n");
        sb.Append("   Источник истины: visual/kern-ui/css/tokens.css → visual/kern-brand/tokens/export/tokens.uss (build-tokens.mjs).\n");
        sb.Append("   Генератор: tools/generators/Kern.DesignSystem emit-uss-tokens; --check сверяет файл с макетом.\n");
        sb.Append("   Что не переносится переменной (каналы палитры, тени, узоры, срезы, слои) — список в шапке tokens.uss. */\n");
        sb.Append(":root {\n");
        sb.Append(block.Groups["body"].Value.TrimEnd());
        sb.Append("\n\n    /* font */\n");
        foreach (var (name, asset, note) in Faces)
        {
            if (!File.Exists(Path.Combine(repo, asset)))
            {
                Console.Error.WriteLine($"нет SDF-ассета для {name}: {asset}");
                return 1;
            }

            sb.Append($"    {name}: url(\"project://database/{asset}\"); /* {note} */\n");
        }

        sb.Append("}\n");
        string text = sb.ToString();

        if (check)
        {
            if (!File.Exists(target) || File.ReadAllText(target) != text)
            {
                Console.WriteLine($"ТОКЕНЫ ИГРЫ РАЗОШЛИСЬ С МАКЕТОМ: {Path.GetRelativePath(repo, target)}");
                return 1;
            }

            Console.WriteLine("игра совпадает с макетом");
            return 0;
        }

        File.WriteAllText(target, text);
        Console.WriteLine($"записано: {Path.GetRelativePath(repo, target)}");
        return 0;
    }
}
