using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Scanning;

public static class SourcePathPatterns
{
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
