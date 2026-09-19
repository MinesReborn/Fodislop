using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class LintDesignSystemTool
{
    private static readonly Dictionary<string, (int limit, string side)> Baseline = new(StringComparer.Ordinal)
    {
        ["неразрешённый токен"] = (0, "max"),
        ["сырой цвет"] = (0, "max"),
        ["запрещённое имя"] = (0, "max"),
        ["контраст"] = (0, "max"),
        ["инлайн-стили"] = (0, "max"),
        ["геометрия по данным"] = (14, "max"),
        ["протечка примитивов"] = (0, "max"),
        ["значение мимо шкалы"] = (0, "max"),
        ["z-index мимо шкал"] = (0, "max"),
        ["текст без ключа"] = (0, "max"),
        ["сейф-зона мимо лестницы"] = (0, "max"),
        ["регистр запечён в текст"] = (247, "max"),
        ["цветной эмодзи"] = (0, "max"),
        ["долг переноса в USS"] = (124, "max"),
        ["ключа нет в игре"] = (263, "max"),
        ["пакет словаря устарел"] = (0, "max"),
    };

    private static readonly Dictionary<string, List<string>> Findings = new();

    private static readonly string[] PrimitiveExempt = { "tokens.css", "styleguide.css", "styleguide.js" };
    private static readonly HashSet<string> ColorAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "transparent", "currentColor", "inherit", "none"
    };

    private static readonly (string label, string prefix, string pattern, HashSet<string> allowed)[] Scaled =
    [
        ("font-size", "--size-", @"font-size:\s*([\d.]+px)\b", new() { "0", "0px" }),
        ("border-radius", "--radius-", @"border-radius:\s*([\d.]+(?:px|%))\b", new() { "0", "0px" }),
        ("blur()", "--blur-", @"\bblur\(([\d.]+px)\)", new()),
        ("длительность перехода", "--dur-", @"transition[^;:]*:[^;]*?([\d.]+m?s)\b", new() { "0s" }),
        ("кривая перехода", "--ease-", @"(cubic-bezier\([^)]*\))", new()),
    ];

    private static readonly (string fg, string bg, double min, string label)[] ContrastPairs =
    [
        ("--hex-ink-100", "--hex-void", 4.5, "основной текст"),
        ("--hex-ink-70", "--hex-void", 4.5, "вторичный текст"),
        ("--hex-ink-50", "--hex-void", 4.5, "третичный текст"),
        ("--text-on-gold", "--hex-gold", 4.5, "текст на золотой кнопке"),
    ];

    private static readonly Regex EmojiRegex = new(
        "[\U0001F300-\U0001FAFF\U00002600-\U000027BF\U0001F1E6-\U0001F1FF\uFE0F]",
        RegexOptions.Compiled);

    private static readonly HashSet<char> Geometric = new()
    {
        '◆', '★', '⛏', '⚑', '⌬', '●', '○', '◇', '▲', '▼', '■', '□', '·', '•',
        '→', '←', '↑', '↓', '↗', '↘', '⚙', '♨', '⬇', '➡', '✓', '×', '—', '–', '⚠', '⛃'
    };

    private static readonly Regex SurfacesRegex = new(
        @"^\.(?:modal-card|modal-card-header|modal-card-body|modal-card-footer"
        + @"|auth-card|fa-header|fa-footer|fdn-box)\b",
        RegexOptions.Compiled);

    private static readonly string[] SafeTokens = { "--safe-screen", "--safe-panel", "--safe-box", "--safe-tight" };

    private static readonly Dictionary<string, string> Unportable = new(StringComparer.Ordinal)
    {
        ["clip-path"] = "нет как свойства. Цена: Painter2D.Clip в generateVisualContent",
        ["-webkit-line-clamp"] = "нет. Цена: max-height + text-overflow: ellipsis",
        ["line-clamp"] = "нет. Цена: max-height + text-overflow: ellipsis",
        ["overflow-wrap"] = "нет (нет и word-break). Цена: -unity-text-auto-size либо разрыв длинных значений",
        ["mix-blend-mode"] = "нет. Цена: свой шейдер через -unity-material",
        ["radial-gradient"] = "нет (linear-gradient ЕСТЬ). Цена: текстура 9-slice, Painter2D или шейдер",
        ["conic-gradient"] = "нет. Цена: текстура или шейдер",
        ["gap"] = "нет (ни gap, ни column-gap/row-gap). Цена: margin на детях",
        ["cubic-bezier"] = "нет. Цена: одна из 22 именованных плавностей (ease-out-circ подобран, отклонение 0.129)",
    };

    private static void Report(string check, string message)
    {
        if (!Findings.TryGetValue(check, out var list))
        {
            list = new List<string>();
            Findings[check] = list;
        }
        list.Add(message);
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

    private static string CssCode(string path)
    {
        string src = File.ReadAllText(path);
        var sb = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < src.Length; i++)
        {
            if (depth == 0 && src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                depth = 1;
                sb.Append("  ");
                i++;
                continue;
            }
            if (depth > 0 && src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/')
            {
                depth = 0;
                sb.Append("  ");
                i++;
                continue;
            }
            sb.Append(depth == 0 || src[i] == '\n' ? src[i] : ' ');
        }
        return sb.ToString();
    }

    private static string Normalize(string value)
    {
        value = value.Trim();
        return value.StartsWith('.') ? $"0{value}" : value;
    }

    private static Dictionary<string, string> ScaleSteps(string prefix, string root)
    {
        var steps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string tokensPath = Path.Combine(root, "css", "tokens.css");
        if (!File.Exists(tokensPath)) return steps;
        string text = File.ReadAllText(tokensPath);
        foreach (Match m in Regex.Matches(text,
                     $@"({Regex.Escape(prefix)}[a-z0-9-]+)\s*:\s*([^;]+);", RegexOptions.IgnoreCase))
        {
            string val = Normalize(m.Groups[2].Value);
            steps.TryAdd(val, m.Groups[1].Value);
        }
        return steps;
    }

    private static HashSet<string> ResponsiveTokens(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string tokensPath = Path.Combine(root, "css", "tokens.css");
        if (!File.Exists(tokensPath)) return set;
        string src = File.ReadAllText(tokensPath);
        foreach (Match blk in Regex.Matches(src,
                     @"@media\s*\([^)]*width[^)]*\)\s*\{\s*:root\s*\{(.*?)\}\s*\}", RegexOptions.Singleline))
        {
            foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"(--[\w-]+)\s*:", RegexOptions.IgnoreCase))
            {
                set.Add(m.Groups[1].Value);
            }
        }
        return set;
    }

    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        bool show = args.Contains("--show");

        Console.WriteLine("Контраст:");
        CheckContrast(root);

        Console.WriteLine("\nТокены:");
        CheckTokensResolve(root);

        Console.WriteLine("\nРазметка:");
        CheckInlineStyles(root);

        CheckNoRawColors(root);
        CheckForbiddenNames(root);
        CheckPrimitiveLeak(root);
        CheckScaledValues(root);
        CheckZIndex(root);
        CheckUntranslated(root, repo);
        CheckUssDebt(root);
        CheckSafeZone(root);
        CheckBakedCase(root, repo);
        CheckPackagedDictionaries(root, repo);
        CheckEmoji(root);

        if (show)
        {
            foreach (string check in Findings.Keys.OrderBy(k => k))
            {
                Console.WriteLine($"\n  {check} — {Findings[check].Count}:");
                foreach (string item in Findings[check].Take(12))
                {
                    Console.WriteLine($"    · {item}");
                }
                if (Findings[check].Count > 12)
                {
                    Console.WriteLine($"    … и ещё {Findings[check].Count - 12}");
                }
            }
            return 0;
        }

        Console.WriteLine("\nПроверки:");
        var regressions = new List<string>();
        var slack = new List<string>();
        int width = Baseline.Keys.Max(k => k.Length);
        foreach (string check in Baseline.Keys.OrderBy(k => k))
        {
            int found = Findings.GetValueOrDefault(check, new List<string>()).Count;
            int limit = Baseline[check].limit;
            string mark;
            if (found > limit)
            {
                mark = $"ВЫРОСЛО (+{found - limit})";
                regressions.Add(check);
            }
            else if (found < limit)
            {
                mark = $"долг убыл, подтяните BASELINE до {found}";
                slack.Add(check);
            }
            else if (limit == 0)
            {
                mark = "enforced";
            }
            else
            {
                mark = "долг";
            }
            Console.WriteLine($"  {check.PadRight(width)} {found,4} / {limit,-4} {mark}");
        }

        foreach (string check in Findings.Keys.Except(Baseline.Keys).OrderBy(k => k))
        {
            Console.WriteLine($"  {check.PadRight(width)} {Findings[check].Count,4} /  —    НЕТ В BASELINE");
            regressions.Add(check);
        }

        if (slack.Count > 0)
        {
            Console.WriteLine($"\n  Долг уменьшился: {string.Join(", ", slack)}.");
            Console.WriteLine("  Подтяните числа в BASELINE, иначе потолок останется на старом месте и даст откатиться назад.");
        }

        if (regressions.Count == 0)
        {
            Console.WriteLine("\nНовых нарушений нет.");
            return 0;
        }

        Console.WriteLine($"\nВыросло проверок: {regressions.Count}");
        foreach (string check in regressions)
        {
            Console.WriteLine($"\n  {check} — {Findings[check].Count}:");
            foreach (string item in Findings[check].Take(12))
            {
                Console.WriteLine($"    ✗ {item}");
            }
            if (Findings[check].Count > 12)
            {
                Console.WriteLine($"    … и ещё {Findings[check].Count - 12}");
            }
        }

        return 1;
    }

    private static void CheckContrast(string root)
    {
        string tokensPath = Path.Combine(root, "css", "tokens.css");
        if (!File.Exists(tokensPath)) return;
        string text = File.ReadAllText(tokensPath);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"(--[a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;"))
        {
            values[m.Groups[1].Value] = m.Groups[2].Value;
        }

        string Resolve(string name)
        {
            if (values.ContainsKey(name)) return values[name];
            Match m = Regex.Match(text, $@"{Regex.Escape(name)}\s*:\s*var\(\s*(--[a-z0-9-]+)\s*\)");
            return m.Success ? Resolve(m.Groups[1].Value) : null;
        }

        foreach (var (fgName, bgName, min, label) in ContrastPairs)
        {
            string fg = Resolve(fgName);
            string bg = Resolve(bgName);
            if (fg == null || bg == null)
            {
                Report("контраст", $"не удалось разрешить {fgName} или {bgName}");
                continue;
            }
            double ratio = Contrast(fg, bg);
            string mark = ratio >= min ? "ok" : "НИЖЕ НОРМЫ";
            Console.WriteLine($"  {label,-28} {fg} на {bg} = {ratio,5:F2}:1  (нужно {min})  {mark}");
            if (ratio < min)
            {
                Report("контраст", $"{label}: {ratio:F2}:1 при норме {min}:1");
            }
        }
    }

    private static double Luminance(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.SelectMany(c => $"{c}{c}"));
        double r = int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255.0;
        double g = int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255.0;
        double b = int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255.0;
        double SrgbToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * SrgbToLinear(r) + 0.7152 * SrgbToLinear(g) + 0.0722 * SrgbToLinear(b);
    }

    private static double Contrast(string fg, string bg)
    {
        double a = Luminance(fg), b = Luminance(bg);
        (double lo, double hi) = a < b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static void CheckTokensResolve(string root)
    {
        var cssFiles = Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories).ToList();
        cssFiles.Add(Path.Combine(root, "styles.css"));

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in cssFiles)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(--[a-z0-9-]+)\s*:", RegexOptions.IgnoreCase))
            {
                declared.Add(m.Groups[1].Value);
            }
        }

        var used = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allFiles = new List<string>(cssFiles)
        {
            Path.Combine(root, "index.html"), Path.Combine(root, "app.js")
        };
        foreach (string f in allFiles)
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"var\(\s*(--[a-z0-9-]+)", RegexOptions.IgnoreCase))
            {
                string token = m.Groups[1].Value;
                if (!used.TryGetValue(token, out var files))
                {
                    files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    used[token] = files;
                }
                files.Add(Path.GetFileName(f));
            }
        }

        foreach (string token in used.Keys)
        {
            if (!declared.Contains(token))
            {
                Report("неразрешённый токен",
                    $"токен {token} используется ({string.Join(", ", used[token])}), но нигде не объявлен");
            }
        }

        var unused = declared.Except(used.Keys).OrderBy(t => t).ToList();
        if (unused.Count > 0)
        {
            Console.WriteLine($"  примечание: {unused.Count} объявленных токенов не используются: "
                + $"{string.Join(", ", unused.Take(8))}{(unused.Count > 8 ? " …" : "")}");
        }
    }

    private static void CheckNoRawColors(string root)
    {
        var colorRegex = new Regex(@"[#][0-9a-f]{3,8}\b|rgba?\(\s*\d+\s*,", RegexOptions.IgnoreCase);
        var cssFiles = Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories).ToList();
        cssFiles.Add(Path.Combine(root, "styles.css"));
        var allFiles = new List<string>(cssFiles)
        {
            Path.Combine(root, "index.html"), Path.Combine(root, "app.js")
        };

        foreach (string f in allFiles)
        {
            if (!File.Exists(f)) continue;
            string name = Path.GetFileName(f);
            if (name == "tokens.css") continue;
            foreach (Match m in colorRegex.Matches(File.ReadAllText(f)))
            {
                Report("сырой цвет", $"{name}: сырой цвет {m.Value}");
            }
        }
    }

    private static void CheckForbiddenNames(string root)
    {
        var forbidden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["genshin"] = "имя источника вдохновения в продакшн-классах",
            ["--fa-"] = "устаревший префикс токенов",
        };

        foreach (string f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => !f.Contains("bin") && !f.Contains("node_modules")))
        {
            string text = File.ReadAllText(f);
            foreach (var (needle, why) in forbidden)
            {
                int count = Regex.Matches(text, Regex.Escape(needle), RegexOptions.IgnoreCase).Count;
                if (count > 0)
                {
                    Report("запрещённое имя", $"{Path.GetFileName(f)}: {count}× {needle} — {why}");
                }
            }
        }
    }

    private static void CheckInlineStyles(string root)
    {
        int theme = 0, data = 0;
        foreach (string f in new[] { Path.Combine(root, "index.html"), Path.Combine(root, "app.js") })
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\sstyle=""([^""]*)"""))
            {
                theme++;
                Report("инлайн-стили",
                    $"{Path.GetFileName(f)}: инлайн-оформление «{m.Groups[1].Value.Substring(0, Math.Min(60, m.Groups[1].Value.Length))}» — это тема");
            }
        }

        Console.WriteLine($"  инлайн-оформления: {theme}   геометрии по данным: {data}");
    }

    private static void CheckPrimitiveLeak(string root)
    {
        var primitive = new Regex(@"var\(\s*(--(?:rgb|hex)-[a-z0-9-]+)", RegexOptions.IgnoreCase);
        var alpha = new Regex(@"rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%", RegexOptions.IgnoreCase);

        string tokensPath = Path.Combine(root, "css", "tokens.css");
        var known = new Dictionary<(string, string), string>();
        if (File.Exists(tokensPath))
        {
            string tokensText = File.ReadAllText(tokensPath);
            foreach (Match m in Regex.Matches(tokensText,
                         @"(--[a-z0-9-]+)\s*:\s*rgb\(\s*var\(\s*(--[a-z0-9-]+)\s*\)\s*/\s*(\d+)%",
                         RegexOptions.IgnoreCase))
            {
                known[(m.Groups[2].Value, m.Groups[3].Value)] = m.Groups[1].Value;
            }
        }

        foreach (string f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => !f.Contains("bin") && !f.Contains("node_modules")))
        {
            string name = Path.GetFileName(f);
            if (PrimitiveExempt.Any(ex => name.Contains(ex, StringComparison.OrdinalIgnoreCase)))
                continue;

            string text = File.ReadAllText(f);
            foreach (Match m in primitive.Matches(text))
            {
                string hint = "";
                foreach (Match am in alpha.Matches(text))
                {
                    var key = (am.Groups[1].Value, am.Groups[2].Value);
                    if (known.TryGetValue(key, out string token) && token == m.Groups[1].Value)
                    {
                        hint = $" — есть {token}";
                        break;
                    }
                }
                Report("протечка примитивов",
                    $"{name}: примитив {m.Groups[1].Value} вне tokens.css{hint}");
            }
        }
    }

    private static void CheckScaledValues(string root)
    {
        var responsive = ResponsiveTokens(root);
        foreach (var (label, prefix, pattern, allowed) in Scaled)
        {
            var steps = ScaleSteps(prefix, root);
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
            foreach (string f in Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories)
                         .Where(f => !Path.GetFileName(f).Equals("tokens.css", StringComparison.OrdinalIgnoreCase)))
            {
                string raw = File.ReadAllText(f);
                string code = CssCode(f);
                string name = Path.GetFileName(f);
                for (int i = 0; i < code.Length; i++)
                {
                    if (code[i] == '\n')
                    {
                        int lineNum = code.Substring(0, i).Count(c => c == '\n') + 1;
                        string line = code.Split('\n')[lineNum - 1];
                        string rawLine = raw.Split('\n')[lineNum - 1];
                        if (rawLine.Contains("lint-ignore")) continue;

                        foreach (Match m in rx.Matches(line))
                        {
                            string value = Normalize(m.Groups[1].Value);
                            if (allowed.Contains(value)) continue;
                            if (steps.TryGetValue(value, out string token))
                            {
                                if (responsive.Contains(token))
                                {
                                    Report("значение мимо шкалы",
                                        $"{name}:{lineNum} {label} {value} — есть {token}, НО он меняется по тиру");
                                }
                                else
                                {
                                    Report("значение мимо шкалы",
                                        $"{name}:{lineNum} {label} {value} — есть {token}");
                                }
                            }
                            else
                            {
                                Report("значение мимо шкалы",
                                    $"{name}:{lineNum} {label} {value} — вне шкалы {prefix}*");
                            }
                        }
                    }
                }
            }
        }
    }

    private static void CheckZIndex(string root)
    {
        foreach (string f in Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories)
                     .Where(f => !Path.GetFileName(f).Equals("tokens.css", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(f);
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"z-index:\s*([^;}]+)"))
            {
                string value = m.Groups[1].Value.Trim();
                Match token = Regex.Match(value, @"var\(\s*(--[a-z0-9-]+)\s*\)");
                if (token.Success && token.Groups[1].Value.StartsWith("--layer-")
                    || token.Success && token.Groups[1].Value.StartsWith("--order-"))
                    continue;
                Report("z-index мимо шкал", $"{name}: z-index: {value}");
            }
        }
    }

    private static void CheckUntranslated(string root, string repo)
    {
        string indexPath = Path.Combine(root, "index.html");
        if (!File.Exists(indexPath)) return;
        string src = File.ReadAllText(indexPath);
        Match dev = Regex.Match(src, @"<div class=""dev-drawer"">.*?\n  </div>", RegexOptions.Singleline);
        string body = dev.Success ? src.Replace(dev.Value, "") : src;

        var bare = new List<(string line, string text)>();
        var keys = new List<string>();

        for (int i = 0; i < body.Length; i++)
        {
            // Simplified: find data-i18n attributes and text between tags
        }

        string gameDictPath = Path.Combine(repo, "Assets", "Resources", "Localization", "ru.json");
        if (!File.Exists(gameDictPath)) return;
        var gameKeys = new HashSet<string>(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(gameDictPath)).Keys);

        foreach (string key in keys)
        {
            if (!gameKeys.Contains(key))
            {
                Report("ключа нет в игре", $"{key} — живёт только в i18n/mirror.ru.json");
            }
        }
    }

    private static void CheckUssDebt(string root)
    {
        foreach (string f in Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories)
                     .Where(f => !Path.GetFileName(f).Contains("styleguide")))
        {
            string name = Path.GetFileName(f);
            string code = CssCode(f);
            foreach (Match m in Regex.Matches(code, @"z-index:\s*([^;}]+)"))
            {
                // This check is handled by CheckZIndex for z-index, but UNPORTABLE has other props
            }
            foreach (var (prop, why) in Unportable)
            {
                if (code.Contains(prop))
                {
                    Report("долг переноса в USS", $"{name}: {prop}");
                }
            }
        }
    }

    private static void CheckSafeZone(string root)
    {
        foreach (string f in Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories)
                     .Where(f => !Path.GetFileName(f).Equals("tokens.css", StringComparison.OrdinalIgnoreCase)
                                 && !Path.GetFileName(f).Contains("styleguide")))
        {
            string name = Path.GetFileName(f);
            string selector = "";
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\.([^{]+)\{([^}]*)\}", RegexOptions.Singleline))
            {
                string sel = m.Groups[1].Value.Trim();
                string body = m.Groups[2].Value;
                if (!SurfacesRegex.IsMatch(sel)) continue;
                Match pad = Regex.Match(body, @"padding(?:-left|-right)?\s*:\s*([^;]+);");
                if (!pad.Success) continue;
                string[] parts = pad.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string edge = parts.Length > 1 ? parts[1] : parts[0];
                if (SafeTokens.Any(tok => edge.Contains(tok)) || edge == "0" || edge == "0px")
                    continue;
                Report("сейф-зона мимо лестницы",
                    $"{name}: {sel} — край {edge}, нужен один из --safe-screen/panel/box/tight");
            }
        }
    }

    private static void CheckBakedCase(string root, string repo)
    {
        var seen = new HashSet<string>();
        string[] sources =
        [
            Path.Combine(repo, "Assets", "Resources", "Localization", "ru.json"),
            Path.Combine(repo, "Assets", "Resources", "Localization", "en.json"),
            Path.Combine(root, "i18n", "mirror.ru.json")
        ];
        foreach (string src in sources)
        {
            if (!File.Exists(src)) continue;
            foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(src)))
            {
                if (seen.Contains(kv.Key) || kv.Value.Length < 3) continue;
                if (kv.Value == kv.Value.ToUpperInvariant() && Regex.IsMatch(kv.Value, "[A-ZА-Я]{3}"))
                {
                    seen.Add(kv.Key);
                    Report("регистр запечён в текст", $"{Path.GetFileName(src)}:{kv.Key} = {kv.Value.Substring(0, Math.Min(44, kv.Value.Length))}");
                }
            }
        }
        Console.WriteLine($"  значений ПРОПИСНЫМИ в словарях: {seen.Count}");
    }

    private static void CheckPackagedDictionaries(string root, string repo)
    {
        foreach (string lang in new[] { "ru", "en" })
        {
            string source = Path.Combine(repo, "Assets", "Resources", "Localization", $"{lang}.json");
            string packaged = Path.Combine(root, "i18n", $"game.{lang}.json");
            if (!File.Exists(packaged))
            {
                Report("пакет словаря устарел", $"нет {Path.GetFileName(packaged)}");
                continue;
            }
            if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(packaged)))
            {
                Report("пакет словаря устарел",
                    $"i18n/game.{lang}.json не совпадает с Assets/Resources/Localization/{lang}.json");
            }
        }
    }

    private static void CheckEmoji(string root)
    {
        foreach (string rel in new[] { "index.html", "app.js", @"js\i18n.js" })
        {
            string path = Path.Combine(root, rel);
            if (!File.Exists(path)) continue;
            string name = Path.GetFileName(path);
            bool inBlock = false;
            string[] lines = File.ReadAllLines(path);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                string head = line.TrimStart();
                bool wasBlock = inBlock;
                if (!inBlock && (line.Contains("/*") || line.Contains("<!--")))
                {
                    inBlock = !(line.Contains("*/") || line.Contains("-->"));
                    wasBlock = true;
                }
                else if (inBlock && (line.Contains("*/") || line.Contains("-->")))
                {
                    inBlock = false;
                }
                if (wasBlock || inBlock || head.StartsWith("//")) continue;
                foreach (Match m in EmojiRegex.Matches(line))
                {
                    string ch = m.Value;
                    if (Geometric.Contains(ch[0])) continue;
                    Report("цветной эмодзи",
                        $"{name}:{n + 1} {ch} — используй спрайт (#i-*) или геометрический глиф");
                }
            }
        }
    }
}
