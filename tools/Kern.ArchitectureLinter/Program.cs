using Kern.ArchitectureLinter.Core;

namespace Kern.ArchitectureLinter;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => arg is "--help" or "-h"))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var context = ParseArguments(args);
            if (context == null)
            {
                PrintUsage();
                return 2;
            }

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
        var includedRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? sarifPath = null;
        bool sarif = false;
        RuleSeverity failOn = RuleSeverity.Error;
        bool strict = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--project-root":
                case "-p":
                    if (!TryReadValue(args, ref i, arg, out projectRoot))
                    {
                        return null;
                    }

                    break;
                case "--exclude":
                case "-e":
                    if (!TryReadValue(args, ref i, arg, out string excludePattern))
                    {
                        return null;
                    }

                    excludePatterns.Add(excludePattern);
                    break;
                case "--rule":
                case "-r":
                    if (!TryReadValue(args, ref i, arg, out string ruleId))
                    {
                        return null;
                    }

                    includedRuleIds.Add(ruleId);
                    break;
                case "--sarif":
                    sarif = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        sarifPath = args[++i];
                    }

                    break;
                case "--fail-on":
                    if (!TryReadValue(args, ref i, arg, out string severity) ||
                        !Enum.TryParse(severity, true, out RuleSeverity parsedSeverity))
                    {
                        Console.Error.WriteLine($"Invalid severity for {arg}: '{severity}'.");
                        return null;
                    }

                    failOn = parsedSeverity;
                    break;
                case "--strict":
                    strict = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown option: {arg}");
                        return null;
                    }

                    assemblyPaths.Add(arg);
                    break;
            }
        }

        projectRoot = Path.GetFullPath(projectRoot);
        if (strict && failOn == RuleSeverity.Error)
        {
            failOn = RuleSeverity.Warning;
        }

        bool requiresAssemblies = ArchitectureLinter.SelectedRulesRequireAssemblies(includedRuleIds);
        if (requiresAssemblies && assemblyPaths.Count == 0)
        {
            assemblyPaths.AddRange(DiscoverAssemblies(projectRoot));
        }

        var unityPaths = requiresAssemblies && assemblyPaths.Count > 0 ? DiscoverUnityPaths() : [];

        return new LinterContext
        {
            ProjectRoot = projectRoot,
            AssemblyPaths = assemblyPaths,
            UnityAssemblyPaths = unityPaths,
            ExcludePatterns = excludePatterns,
            IncludedRuleIds = includedRuleIds,
            EnableSarif = sarif,
            SarifOutputPath = sarifPath ?? Path.Combine(projectRoot, "architecture-lint.sarif"),
            FailOnSeverity = failOn,
            Strict = strict
        };
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string value)
    {
        if (index + 1 < args.Count && !args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            value = args[++index];
            return true;
        }

        Console.Error.WriteLine($"Missing value for {option}.");
        value = string.Empty;
        return false;
    }

    private static List<string> DiscoverAssemblies(string projectRoot)
    {
        var assembliesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

            IEnumerable<string> candidates = Directory
                .EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories)
                .Where(dll =>
                {
                    string name = Path.GetFileNameWithoutExtension(dll);
                    return name.StartsWith("Kern", StringComparison.Ordinal) ||
                           name is "Assembly-CSharp" or "Assembly-CSharp-Editor";
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(path => path, StringComparer.Ordinal);

            foreach (string dll in candidates)
            {
                string name = Path.GetFileNameWithoutExtension(dll);
                assembliesByName.TryAdd(name, dll);
            }
        }

        bool hasModularRuntime = assembliesByName.Keys.Any(name =>
            name.StartsWith("Kern.", StringComparison.Ordinal));
        if (hasModularRuntime)
        {
            assembliesByName.Remove("Assembly-CSharp");
        }

        return assembliesByName
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToList();
    }

    private static List<string> DiscoverUnityPaths()
    {
        var paths = new List<string>();

        var unityApp = Directory.GetDirectories("/Applications/Unity/Hub/Editor", "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(unityApp))
        {
            var managed = Path.Combine(unityApp, "Unity.app", "Contents", "Resources", "Scripting", "Managed", "UnityEngine");
            if (Directory.Exists(managed))
                paths.Add(managed);

            var managedLegacy = Path.Combine(unityApp, "Unity.app", "Contents", "Managed");
            if (Directory.Exists(managedLegacy))
                paths.Add(managedLegacy);

            var mono = Path.Combine(unityApp, "Unity.app", "Contents", "MonoBleedingEdge", "lib", "mono", "4.7.1-api");
            if (Directory.Exists(mono))
                paths.Add(mono);
        }

        return paths;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Kern Architecture Linter");
        Console.WriteLine("Usage: dotnet run -- [options] [assembly_paths...]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --project-root <path>    Project root directory (default: current)");
        Console.WriteLine("  -e, --exclude <pattern>      Exclude assemblies matching pattern");
        Console.WriteLine("  -r, --rule <id>              Run only this rule (repeatable; default: all)");
        Console.WriteLine("  --sarif [path]               Output SARIF report (default: architecture-lint.sarif)");
        Console.WriteLine("  --fail-on <severity>         Exit code 1 on this severity (Error, Warning, Info)");
        Console.WriteLine("  --strict                     Treat warnings as errors");
        Console.WriteLine("  -h, --help                   Show this help");
    }
}
