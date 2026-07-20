using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

public class CsProjFix : AssetPostprocessor
{
    // This method is called after Unity generates the .csproj file
    private static string OnGeneratedCSProject(string path, string content)
    {
        // Use Regex to find the <LangVersion> tag and replace it
        // Unity usually sets this to specific versions like '9.0' or 'default'
        var pattern = @"<LangVersion>.*?</LangVersion>";
        var replacement = "<LangVersion>preview</LangVersion>";

        // Log for verification (optional)
        // UnityEngine.Debug.Log($"Patching LangVersion to 'preview' in {path}");

        return Regex.Replace(content, pattern, replacement);
    }
}
