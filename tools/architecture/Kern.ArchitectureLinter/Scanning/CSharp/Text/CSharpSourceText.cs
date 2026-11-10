using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Scanning;

public static class CSharpSourceText
{
    private static readonly Regex s_commentRegex = new Regex(@"/\*[\s\S]*?\*/|//[^\n]*", RegexOptions.Compiled);

    public static string StripComments(string source)
    {
        return s_commentRegex.Replace(source, match =>
        {
            char[] replacement = match.Value
                .Select(character => character == '\n' ? '\n' : ' ')
                .ToArray();
            return new string(replacement);
        });
    }
}
