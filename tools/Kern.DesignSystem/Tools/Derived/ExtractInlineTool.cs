using AngleSharp.Html.Parser;
using System.Text;
using System.Text.Json;

namespace Kern.DesignSystem.Tools.Derived;

internal static class ExtractInlineTool
{
    private static readonly HashSet<string> DataProps = new() { "width", "height", "left", "top", "transform" };

    public static int Run(string[] args)
    {
        string root = GetRoot();
        string indexPath = Path.Combine(root, "index.html");
        string html = File.ReadAllText(indexPath);

        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var results = new List<Dictionary<string, object>>();
        void Walk(Stack<AngleSharp.Dom.IElement> stack, AngleSharp.Dom.INode node)
        {
            if (node is AngleSharp.Dom.IElement el)
            {
                var newStack = new Stack<AngleSharp.Dom.IElement>(stack.Reverse());
                newStack.Push(el);
                if (el.GetAttribute("style") is string style)
                {
                    string owner = "";
                    foreach (var parent in newStack.Reverse())
                    {
                        if (!string.IsNullOrEmpty(parent.GetAttribute("id")))
                        {
                            owner = parent.GetAttribute("id")!;
                            break;
                        }
                    }

                    results.Add(new Dictionary<string, object>
                    {
                        ["tag"] = el.TagName,
                        ["line"] = "0",
                        ["owner"] = owner,
                        ["cls"] = el.GetAttribute("class") ?? "",
                        ["key"] = el.GetAttribute("data-i18n") ?? "",
                        ["id"] = el.GetAttribute("id") ?? "",
                        ["style"] = style,
                        ["parent_cls"] = newStack.Count > 1 ? newStack.ElementAt(1).GetAttribute("class") ?? "" : "",
                    });
                }

                foreach (var child in el.Children)
                {
                    Walk(newStack, child);
                }
            }
        }

        Walk(new Stack<AngleSharp.Dom.IElement>(), doc.DocumentElement);

        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
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
