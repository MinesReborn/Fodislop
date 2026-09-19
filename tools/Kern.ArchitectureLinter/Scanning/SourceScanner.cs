using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Scanning;

public static class SourceScanner
{
    private static readonly Regex CommentRegex = new Regex(@"/\*[\s\S]*?\*/|//[^\n]*", RegexOptions.Compiled);

    public static string StripComments(string source)
    {
        return CommentRegex.Replace(source, match =>
        {
            char[] replacement = match.Value
                .Select(character => character == '\n' ? '\n' : ' ')
                .ToArray();
            return new string(replacement);
        });
    }

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

    public static string GetProjectRelativePath(string projectRoot, string absolutePath)
    {
        return Path.GetRelativePath(projectRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

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

    public static bool MatchesAny(string relativePath, IEnumerable<string> patterns)
    {
        return patterns.Any(p => p.Contains('*') ? MatchesGlob(relativePath, p) : relativePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesGlob(string path, string pattern)
    {
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$");
        return regex.IsMatch(path);
    }
}
