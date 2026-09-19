using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kern.DesignSystem.Tools;

internal static class ExtractI18nTool
{
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "svg", "defs", "g", "path", "use", "circle", "rect", "title"
    };

    private static readonly Dictionary<string, string> ScreenNs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["authView"] = "gateway",
        ["onboardingView"] = "onboarding",
        ["menuArea"] = "mainmenu",
        ["descentView"] = "descent",
        ["ingameView"] = "hud",
        ["pauseView"] = "pause",
        ["reconnectView"] = "network",
    };

    private static readonly Dictionary<string, string> ModalNs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inventoryModal"] = "inventory",
        ["programmatorModal"] = "programmator",
        ["settingsModal"] = "settings",
        ["chatModal"] = "chat",
        ["serverBrowserModal"] = "server",
        ["profileModal"] = "mainmenu",
        ["chronicleModal"] = "mainmenu",
        ["clanModal"] = "mainmenu",
        ["traderModal"] = "mainmenu",
        ["repairModal"] = "mainmenu",
    };

    private static readonly Regex NotText = new(@"^([A-Z]|[xX]\d+|T\d+|CR|OK|ОР|v?\d+[\d.,:/%°\s+-]*|[^\w\s]+)$", RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string indexPath = Path.Combine(root, "index.html");
        if (!File.Exists(indexPath)) return 0;

        var parser = new HtmlParser();
        var doc = parser.ParseDocument(File.ReadAllText(indexPath));

        var gameDictPath = Path.Combine(repo, "Assets", "Resources", "Localization", "ru.json");
        var gameDict = File.Exists(gameDictPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(gameDictPath))
            : new Dictionary<string, string>();
        var byNorm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in gameDict)
        {
            string norm = Normalize(kv.Value);
            if (!byNorm.ContainsKey(norm)) byNorm[norm] = kv.Key;
        }

        var found = new List<NodeInfo>();
        void Walk(IEnumerable<INode> stack, INode node)
        {
            var newStack = stack.Append(node);
            if (node.NodeType == NodeType.Text)
            {
                string text = Regex.Replace(node.TextContent ?? "", @"\s+", " ").Trim();
                if (string.IsNullOrEmpty(text) || !Regex.IsMatch(text, "[A-Za-zА-Яа-яЁё]"))
                    return;
                if (newStack.Any(n => n is IElement el && SkipTags.Contains(el.TagName)))
                    return;
                bool notr = newStack.Any(n => n is IElement el && el.GetAttribute("translate") == "no");
                bool devDrawer = false;
                foreach (var n in newStack)
                {
                    if (n is IElement el)
                    {
                        string cls = el.GetAttribute("class") ?? "";
                        if (cls.Contains("dev-drawer"))
                        {
                            devDrawer = true;
                            break;
                        }
                    }
                }
                if (notr || devDrawer) return;

                bool keyed = newStack.Any(n => n is IElement el && el.GetAttribute("data-i18n") != null);
                string parentId = newStack.Reverse().OfType<IElement>().FirstOrDefault()?.Id ?? "";
                string parentClass = newStack.Reverse().OfType<IElement>().FirstOrDefault()?.GetAttribute("class") ?? "";
                found.Add(new NodeInfo
                {
                    Text = text,
                    Keyed = keyed,
                    ParentId = parentId,
                    ParentClass = parentClass,
                    Element = newStack.Reverse().OfType<IElement>().FirstOrDefault(),
                });
            }
            else if (node is IElement el)
            {
                foreach (var child in el.Children)
                {
                    Walk(newStack, child);
                }
            }
        }

        Walk(Array.Empty<INode>(), doc.DocumentElement);

        var reused = new List<NodeInfo>();
        var minted = new List<NodeInfo>();
        var skipped = new List<NodeInfo>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in found)
        {
            if (NotText.IsMatch(node.Text))
            {
                skipped.Add(node);
                continue;
            }

            string norm = Normalize(node.Text);
            if (byNorm.TryGetValue(norm, out string existingKey))
            {
                node.Key = existingKey;
                node.Origin = "игра";
                reused.Add(node);
                continue;
            }

            string ns = Namespace(node);
            string slot = Slot(node);
            string baseKey = $"{ns}.{slot}";
            if (!used.TryGetValue(baseKey, out int count))
            {
                count = 0;
            }
            count++;
            used[baseKey] = count;
            node.Key = count == 1 ? baseKey : $"{baseKey}_{count}";
            node.Origin = "новый";
            minted.Add(node);
        }

        Console.WriteLine($"текстовых узлов игры : {reused.Count + minted.Count}");
        Console.WriteLine($"  ключ есть в игре   : {reused.Count} (уник. {reused.Select(n => n.Key).Distinct().Count()})");
        Console.WriteLine($"  ключ выведен       : {minted.Count} (уник. {minted.Select(n => n.Key).Distinct().Count()})");
        Console.WriteLine($"  не текст (пропуск) : {skipped.Count}");

        var nsCounts = minted.GroupBy(n => n.Key.Split('.')[0]).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("\nвыведенные ключи по пространствам:");
        foreach (var kv in nsCounts.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {kv.Key,-14} {kv.Value}");
        }

        Console.WriteLine("\nобразцы выведенных ключей:");
        foreach (var n in minted.Take(24))
        {
            Console.WriteLine($"  {n.Key,-44} {n.Text.Substring(0, Math.Min(44, n.Text.Length))}");
        }

        if (args.Contains("--apply"))
        {
            ApplyKeys(doc, minted, skipped, root, reused.Count);
        }

        return 0;
    }

    private static string Namespace(NodeInfo node)
    {
        string id = node.ParentId;
        if (ModalNs.TryGetValue(id, out string modalNs)) return modalNs;
        if (ScreenNs.TryGetValue(id, out string screenNs)) return screenNs;
        if (node.ParentClass.Contains("modal-overlay") && !string.IsNullOrEmpty(id))
            return ModalNs.GetValueOrDefault(id, "modal");
        return "common";
    }

    private static string Slot(NodeInfo node)
    {
        string id = node.ParentId;
        if (!string.IsNullOrEmpty(id))
        {
            string slug = Slugify(Regex.Replace(id, @"([a-z])([A-Z])", "$1_$2"));
            if (!string.IsNullOrEmpty(slug)) return slug;
        }
        foreach (string cls in (node.ParentClass ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cls.StartsWith("fdn-") || cls.StartsWith("sg-") || cls is "active" or "done" or "current")
                continue;
            string slug = Slugify(cls);
            if (!string.IsNullOrEmpty(slug)) return slug;
        }
        return Slugify(node.Text.Substring(0, Math.Min(24, node.Text.Length)));
    }

    private static string Slugify(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            sb.Append(ch switch
            {
                'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'е' => "e", 'ё' => "e",
                'ж' => "zh", 'з' => "z", 'и' => "i", 'й' => "y", 'к' => "k", 'л' => "l", 'м' => "m",
                'н' => "n", 'о' => "o", 'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t", 'у' => "u",
                'ф' => "f", 'х' => "h", 'ц' => "c", 'ч' => "ch", 'ш' => "sh", 'щ' => "sch",
                'ъ' => "", 'ы' => "y", 'ь' => "", 'э' => "e", 'ю' => "yu", 'я' => "ya",
                _ => ch.ToString(),
            });
        }
        s = Regex.Replace(sb.ToString(), @"[^a-z0-9]+", "_").Trim('_');
        s = Regex.Replace(s, @"_+", "_");
        return s;
    }

    private static string Normalize(string s)
    {
        return Regex.Replace(s.ToLowerInvariant(), @"[^а-яёa-z0-9]", "");
    }

    private static void ApplyKeys(IDocument doc, List<NodeInfo> minted, List<NodeInfo> skipped, string root, int reusedCount)
    {
        var outDir = Path.Combine(root, "i18n");
        Directory.CreateDirectory(outDir);

        int dataI18nCount = 0, notrCount = 0;
        var seen = new HashSet<string>();
        var splitDebt = new List<NodeInfo>();

        foreach (var node in skipped)
        {
            IElement el = node.Element;
            if (el == null || seen.Contains(el.GetAttribute("data-i18n") ?? el.Id ?? "")) continue;
            seen.Add(el.GetAttribute("data-i18n") ?? el.Id ?? "");
            el.SetAttribute("translate", "no");
            notrCount++;
        }

        foreach (var node in minted.Concat(skipped))
        {
            if (node.Keyed) continue;
            IElement el = node.Element;
            if (el == null) continue;
            string id = el.GetAttribute("data-i18n") ?? el.Id ?? Guid.NewGuid().ToString();
            if (seen.Contains(id))
            {
                splitDebt.Add(node);
                continue;
            }
            seen.Add(id);
            el.SetAttribute("data-i18n", node.Key);
            dataI18nCount++;
        }

        string indexPath = Path.Combine(root, "index.html");
        File.WriteAllText(indexPath, doc.DocumentElement.OuterHtml);

        var mirror = minted.ToDictionary(n => n.Key, n => n.Text);
        File.WriteAllText(Path.Combine(outDir, "mirror.ru.json"),
            JsonSerializer.Serialize(mirror, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"\n-> index.html: data-i18n {dataI18nCount}, translate=\"no\" {notrCount}");
        Console.WriteLine($"-> i18n/mirror.ru.json: {mirror.Count} ключей, которых нет в словаре игры");
        Console.WriteLine($"   переиспользовано ключей игры: {reusedCount}");
        if (splitDebt.Count > 0)
        {
            Console.WriteLine($"\n   разрезанных предложений (второй узел без ключа): {splitDebt.Count}");
            foreach (var n in splitDebt)
            {
                Console.WriteLine($"     index.html:{n.Line,-5} {n.Text.Substring(0, Math.Min(52, n.Text.Length))}");
            }
        }
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

    private class NodeInfo
    {
        public string Text { get; set; } = "";
        public bool Keyed { get; set; }
        public string ParentId { get; set; } = "";
        public string ParentClass { get; set; } = "";
        public IElement? Element { get; set; }
        public string? Key { get; set; }
        public string Origin { get; set; } = "";
        public int Line { get; set; }
    }
}
