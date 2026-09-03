using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var context = ParseArguments(args);
        if (context == null)
        {
            PrintUsage();
            return 2;
        }

        try
        {
            var linter = new ArchitectureLinter(context);
            return await linter.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Fatal error: {ex}");
            return 2;
        }
    }

    private static LinterContext? ParseArguments(string[] args)
    {
        var projectRoot = Environment.CurrentDirectory;
        var assemblyPaths = new List<string>();
        var excludePatterns = new List<string>();
        string? sarifPath = null;
        bool sarif = false;
        int failOn = (int)RuleSeverity.Error;
        bool strict = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--project-root":
                case "-p":
                    if (++i < args.Length) projectRoot = args[i];
                    break;
                case "--exclude":
                case "-e":
                    if (++i < args.Length) excludePatterns.Add(args[i]);
                    break;
                case "--sarif":
                    sarif = true;
                    if (++i < args.Length) sarifPath = args[i];
                    break;
                case "--fail-on":
                    if (++i < args.Length && Enum.TryParse<RuleSeverity>(args[i], true, out var sev)) failOn = (int)sev;
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return null;
                default:
                    assemblyPaths.Add(arg);
                    break;
            }
        }

        if (assemblyPaths.Count == 0)
        {
            assemblyPaths.AddRange(DiscoverAssemblies(projectRoot));
        }

        if (assemblyPaths.Count == 0)
        {
            Console.Error.WriteLine("No assemblies found. Specify assembly paths as arguments.");
            return null;
        }

        var unityPaths = DiscoverUnityPaths(projectRoot);

        return new LinterContext
        {
            ProjectRoot = projectRoot,
            AssemblyPaths = assemblyPaths,
            UnityAssemblyPaths = unityPaths,
            ExcludePatterns = excludePatterns,
            EnableSarif = sarif,
            SarifOutputPath = sarifPath ?? Path.Combine(projectRoot, "architecture-lint.sarif"),
            FailOnSeverity = failOn,
            Strict = strict
        };
    }

    private static List<string> DiscoverAssemblies(string projectRoot)
    {
        var assemblies = new List<string>();
        var searchPaths = new[]
        {
            Path.Combine(projectRoot, "Temp", "bin"),
            Path.Combine(projectRoot, "Library", "ScriptAssemblies"),
            Path.Combine(projectRoot, "build"),
        };

        foreach (var dir in searchPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (name.StartsWith("Fodinae") || name is "Assembly-CSharp" or "Assembly-CSharp-Editor")
                    assemblies.Add(dll);
            }
        }

        return assemblies;
    }

    private static List<string> DiscoverUnityPaths(string projectRoot)
    {
        var paths = new List<string>();

        var unityApp = Directory.GetDirectories("/Applications/Unity/Hub/Editor", "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(unityApp))
        {
            var managed = Path.Combine(unityApp, "Unity.app", "Contents", "Managed");
            if (Directory.Exists(managed))
                paths.Add(managed);

            var mono = Path.Combine(unityApp, "Unity.app", "Contents", "MonoBleedingEdge", "lib", "mono", "4.7.1-api");
            if (Directory.Exists(mono))
                paths.Add(mono);
        }

        return paths;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Fodinae Architecture Linter");
        Console.WriteLine("Usage: dotnet run -- [options] [assembly_paths...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --project-root <path>    Project root directory (default: current)");
        Console.WriteLine("  -e, --exclude <pattern>      Exclude assemblies matching pattern");
        Console.WriteLine("  --sarif [path]               Output SARIF report (default: architecture-lint.sarif)");
        Console.WriteLine("  --fail-on <severity>         Exit code 1 on this severity (Error, Warning, Info)");
        Console.WriteLine("  --strict                     Treat warnings as errors");
        Console.WriteLine("  -h, --help                   Show this help");
    }
}
