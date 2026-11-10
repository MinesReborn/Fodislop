using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Scanning;

public static class ProjectSourcePaths
{
    public static string GetProjectRelativePath(string projectRoot, string absolutePath)
    {
        return Path.GetRelativePath(projectRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
