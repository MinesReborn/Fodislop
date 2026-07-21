using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

public class CsProjFix : AssetPostprocessor
{
    // This method is called after Unity generates the .csproj file
    protected static string OnGeneratedCSProject(string path, string content)
    {
        // Use Regex to find the <LangVersion> tag and replace it
        // Unity usually sets this to specific versions like '9.0' or 'default'
        const string pattern = @"<LangVersion>.*?</LangVersion>";
        const string replacement = "<LangVersion>preview</LangVersion>";

        // Log for verification (optional)
        // UnityEngine.Debug.Log($"Patching LangVersion to 'preview' in {path}");

        return Regex.Replace(content, pattern, replacement);
    }
}
