using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Scanning;

public static class CSharpSourceFiles
{
    public static IEnumerable<string> EnumerateCsFiles(string root, params string[] excludeSegments)
    {
        if (!Directory.Exists(root))
            yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (excludeSegments.Any(s => relative.Split('/').Contains(s, StringComparer.OrdinalIgnoreCase)))
                continue;
            yield return file;
        }
    }

    public static IEnumerable<string> EnumerateAllCsFiles(params string[] roots)
    {
        foreach (var root in roots)
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
        }
    }
}
