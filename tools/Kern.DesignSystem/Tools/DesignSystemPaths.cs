namespace Kern.DesignSystem.Tools;

internal static class DesignSystemPaths
{
    public static string GetRoot()
    {
        string[] starts = [Directory.GetCurrentDirectory(), AppContext.BaseDirectory];
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                string localTokensPath = Path.Combine(directory.FullName, "css", "tokens.css");
                if (File.Exists(localTokensPath) && File.Exists(Path.Combine(directory.FullName, "index.html")))
                {
                    return directory.FullName;
                }

                string visualRoot = Path.Combine(directory.FullName, "visual", "kern-ui-lab");
                if (File.Exists(Path.Combine(visualRoot, "css", "tokens.css")) &&
                    File.Exists(Path.Combine(visualRoot, "index.html")))
                {
                    return visualRoot;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate visual/kern-ui-lab with its CSS tokens and index.html.");
    }
}
