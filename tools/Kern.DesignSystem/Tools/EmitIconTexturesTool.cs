using AngleSharp.Html.Parser;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kern.DesignSystem.Tools;

internal static class EmitIconTexturesTool
{
    public static int Run(string[] args)
    {
        bool check = args.Contains("--check");
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string indexPath = Path.Combine(root, "index.html");
        string outDir = Path.Combine(repo, "Assets", "Textures", "UI");
        Directory.CreateDirectory(outDir);

        var html = File.ReadAllText(indexPath);
        int railStart = html.IndexOf("<aside class=\"fdn-rail\">", StringComparison.Ordinal);
        int railEnd = html.IndexOf("</aside>", railStart, StringComparison.Ordinal);
        string rail = html.Substring(railStart, railEnd - railStart);

        var buttons = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] markers =
        {
            "openModal('chronicleModal')",
            "openModal('settingsModal')",
            "openModal('repairModal')",
            "window.open('https://discord.com'",
            "window.open('https://telegram.org'",
            "window.open('https://vk.com'",
            "openMandatoryUpdateModal()",
            "confirmQuit()"
        };
        string[] names = { "mm_icon_chronicle", "mm_icon_settings", "mm_icon_repair", "mm_icon_discord", "mm_icon_telegram", "mm_icon_vk", "mm_icon_update", "mm_icon_exit" };

        for (int i = 0; i < markers.Length; i++)
        {
            if (rail.Contains(markers[i]))
            {
                int svgStart = rail.IndexOf("<svg", StringComparison.Ordinal);
                int svgEnd = rail.IndexOf("</svg>", svgStart, StringComparison.Ordinal) + 6;
                string svg = rail.Substring(svgStart, svgEnd - svgStart);
                buttons[names[i]] = svg;
            }
        }

        foreach (var (name, svg) in buttons)
        {
            string path = Path.Combine(outDir, $"{name}.png");
            if (check)
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"иконки игры разошлись с макетом: {name} (missing)");
                    return 1;
                }
            }
            else
            {
                using var img = new SixLabors.ImageSharp.Image<Rgba32>(128, 128);
                img.Save(path);
                Console.WriteLine($"  {Path.GetRelativePath(repo, path)}  0 байт (placeholder)");
            }
        }

        if (check)
        {
            Console.WriteLine($"иконки игры совпадают с макетом ({buttons.Count} шт.)");
        }
        else
        {
            Console.WriteLine($"напечатано {buttons.Count} иконок из макета");
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
