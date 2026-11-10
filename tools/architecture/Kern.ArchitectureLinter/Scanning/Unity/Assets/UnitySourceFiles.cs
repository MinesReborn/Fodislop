using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Scanning;

public static class UnitySourceFiles
{
    public static IEnumerable<string> EnumerateUssFiles(string root)
    {
        if (!Directory.Exists(root))
            yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.uss", SearchOption.AllDirectories))
            yield return file;
    }

    public static IEnumerable<string> EnumerateUxmlFiles(string root)
    {
        if (!Directory.Exists(root))
            yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.uxml", SearchOption.AllDirectories))
            yield return file;
    }

    public static IEnumerable<string> EnumerateShaderFiles(string root)
    {
        var extensions = new[] { ".shader", ".compute", ".hlsl" };
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }
}
