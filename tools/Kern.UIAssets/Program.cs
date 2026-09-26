using System.Diagnostics;

namespace Kern.UIAssets;

internal static class Program
{
    private const string MenuGeneratorProject = "tools/Kern.MenuAssetsGenerator/Kern.MenuAssetsGenerator.csproj";
    private const string SidebarGeneratorProject = "tools/Kern.SidebarIconsGenerator/Kern.SidebarIconsGenerator.csproj";

    public static int Main(string[] args)
    {
        if (args.Length > 1)
        {
            return PrintUsage();
        }

        string mode = args.Length == 0 ? "all" : args[0];
        if (mode is not ("all" or "menu" or "sidebar-icons"))
        {
            return PrintUsage();
        }

        try
        {
            string projectRoot = FindProjectRoot();
            if ((mode is "all" or "menu") &&
                !RunGenerator(projectRoot, MenuGeneratorProject))
            {
                return 1;
            }

            if ((mode is "all" or "sidebar-icons") &&
                !RunGenerator(projectRoot, SidebarGeneratorProject))
            {
                return 1;
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: Kern.UIAssets [all|menu|sidebar-icons]");
        return 2;
    }

    private static string FindProjectRoot()
    {
        string[] starts =
        [
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        ];

        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                string assetDirectory = Path.Combine(directory.FullName, "Assets", "Textures", "UI");
                if (Directory.Exists(assetDirectory) &&
                    File.Exists(Path.Combine(directory.FullName, MenuGeneratorProject)) &&
                    File.Exists(Path.Combine(directory.FullName, SidebarGeneratorProject)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root containing Assets/Textures/UI and the UI asset generators.");
    }

    private static bool RunGenerator(string projectRoot, string projectPath)
    {
        string fullProjectPath = Path.Combine(projectRoot, projectPath);
        if (!File.Exists(fullProjectPath))
        {
            throw new FileNotFoundException("UI asset generator project was not found.", fullProjectPath);
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(fullProjectPath);

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Could not start the .NET UI asset generator.");
        }

        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
