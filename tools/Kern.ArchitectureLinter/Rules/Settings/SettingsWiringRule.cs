using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Settings;

public sealed class SettingsWiringRule : IRule
{
    private const string ConfigPath = "Assets/Scripts/Core/Configuration/Contracts/ClientConfig.cs";
    private const string SettingsDir = "Assets/Scripts/Core/Interfaces/Contracts/Settings";

    private static readonly HashSet<string> MetadataFields = new(StringComparer.Ordinal)
    {
        "SchemaVersion", "ProjectDefaultsHash"
    };

    private static readonly Dictionary<string, string[]> StartupApplyContracts = new(StringComparer.Ordinal)
    {
        ["TerrainRenderer"] = new[] { "ApplyClientConfig" },
        ["SurfaceRenderer"] = new[] { "ApplyClientConfig" },
        ["LightingEngine"] = new[] { "EnsureInitialized", "ApplyClientConfig" },
        ["PostProcessController"] = new[] { "EnsureVolumeSetup", "ApplyClientConfig" }
    };

    public string Id => "KERN-SETTINGS-WIRING";
    public string Description => "Settings wiring and dead field detection";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        var configSrc = ReadRequired(Path.Combine(projectRoot, ConfigPath));
        if (configSrc == null)
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = "Could not read ClientConfig.cs.", Severity = Severity, AssemblyName = ConfigPath });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var settingsFiles = Directory.Exists(Path.Combine(projectRoot, SettingsDir))
            ? Directory.EnumerateFiles(Path.Combine(projectRoot, SettingsDir), "*.cs").ToList()
            : new List<string>();
        var extraFiles = new[] { Path.Combine(projectRoot, "Assets/Scripts/Rendering/Settings/Contracts/GraphicsQualitySettings.cs") };
        foreach (var f in extraFiles)
            if (File.Exists(f)) settingsFiles.Add(f);

        var fields = new List<string>();
        foreach (var file in settingsFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(content, @"^\s*public\s+(?!const\b)(?!static\s+readonly\b)[A-Za-z0-9_<>\[\],.\s?]+?\s+([A-Za-z0-9_]+)\s*(?:=(?!=)|;)", RegexOptions.Multiline))
                fields.Add(m.Groups[1].Value);
        }

        // Collect reads from all production files
        var reads = fields.ToDictionary(f => f, f => new List<string>(), StringComparer.Ordinal);
        var wiringRoot = Path.Combine(projectRoot, "Assets");
        foreach (var file in SourceScanner.EnumerateCsFiles(wiringRoot, "Tests", "Plugins", "VContainer", "Editor"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
            if (relative.StartsWith("Assets/Scripts/Core/Interfaces/Contracts/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Assets/Scripts/Core/Configuration/", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(file);
            foreach (var field in fields)
            {
                if (Regex.IsMatch(content, $@"\.{Regex.Escape(field)}\b"))
                    reads[field].Add(relative);
            }
        }

        // Attribute-wired fields (AudioBus)
        var attributeWired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in settingsFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^\s*public\s+\S+\s+(?!operator\b)([A-Za-z0-9_]+)\s*(?:=(?!=)|;)");
                if (!m.Success) continue;
                for (var j = i - 1; j >= 0 && j >= i - 5; j--)
                {
                    if (lines[j].Trim().StartsWith("[") && lines[j].Trim().Contains("AudioBus"))
                    {
                        attributeWired.Add(m.Groups[1].Value);
                        break;
                    }
                }
            }
        }

        // Dead fields
        foreach (var field in fields)
        {
            if (MetadataFields.Contains(field) || attributeWired.Contains(field))
                continue;
            if (reads[field].Count == 0)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"ClientConfig.{field} is never referenced in production code — the setting does nothing.",
                    Severity = Severity,
                    AssemblyName = ConfigPath,
                    TypeName = field
                });
            }
        }

        // UI-only wiring: flow state owned by UI (scale, gateway gates).
        // Such fields are correctly read only from UI controllers.
        var uiAllowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "UIScale",
            "AutoLogin",
            "OnboardingDone",
        };
        foreach (var field in fields)
        {
            if (MetadataFields.Contains(field) || attributeWired.Contains(field) || uiAllowed.Contains(field))
                continue;
            var readers = reads[field];
            if (readers.Count == 0) continue;
            if (readers.All(r => r.Contains("/UI/") || Regex.IsMatch(r, @"/(Gateway|PauseMenu)\.cs$")))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"ClientConfig.{field} is read only from UI controllers ({string.Join(", ", readers.Take(5))}) — no game system applies it.",
                    Severity = Severity,
                    AssemblyName = ConfigPath,
                    TypeName = field
                });
            }
        }

        // Setting range coverage
        foreach (var file in settingsFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^\s*public\s+(?!const\b)(?!static\s+readonly\b)[A-Za-z0-9_<>\[\],.\s?]+?\s+(?!operator\b)([A-Za-z0-9_]+)\s*(?:=(?!=)|;)");
                if (!m.Success) continue;
                var fieldName = m.Groups[1].Value;

                string attributes = "";
                for (var j = i - 1; j >= 0 && j >= i - 10; j--)
                {
                    var line = lines[j].Trim();
                    if (line.StartsWith("//") || line.StartsWith("///") || line == "")
                        continue;
                    if (line.StartsWith("["))
                    {
                        attributes += line;
                        continue;
                    }
                    break;
                }
                if (!Regex.IsMatch(attributes, @"\[Setting(Range|Unbounded)|\[Range\("))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"{fieldName} has neither [SettingRange], [Range] nor [SettingUnbounded]. Declare its bounds next to the field.",
                        Severity = Severity,
                        AssemblyName = file,
                        TypeName = fieldName,
                        Line = i + 1
                    });
                }
                if (!Regex.IsMatch(attributes, @"\[SettingConsumer\("))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"{fieldName} has no [SettingConsumer] attribute. Every setting must declare its target subsystem.",
                        Severity = Severity,
                        AssemblyName = file,
                        TypeName = fieldName,
                        Line = i + 1
                    });
                }
            }
        }

        // Startup application contract
        var bootstrapFile = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/GameStartupPipeline.cs");
        if (File.Exists(bootstrapFile))
        {
            var bootstrapSrc = File.ReadAllText(bootstrapFile);
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(
                         bootstrapSrc,
                         @"\b([A-Za-z0-9_<>]+)\??\s+([a-z_][A-Za-z0-9_]*)\s*(?:=|;|\))"))
            {
                string typeName = match.Groups[1].Value;
                if (StartupApplyContracts.ContainsKey(typeName))
                {
                    variables[match.Groups[2].Value] = typeName;
                }
            }

            foreach (Match match in Regex.Matches(
                         bootstrapSrc,
                         @"\bvar\s+([a-z_][A-Za-z0-9_]*)\s*=\s*[^;]*?Resolve<([A-Za-z0-9_<>]+)>"))
            {
                string typeName = match.Groups[2].Value;
                if (StartupApplyContracts.ContainsKey(typeName))
                {
                    variables.TryAdd(match.Groups[1].Value, typeName);
                }
            }

            var invokedContracts = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string variableName, string typeName) in variables)
            {
                foreach (Match match in Regex.Matches(
                             bootstrapSrc,
                             $@"\b{Regex.Escape(variableName)}\.([A-Za-z0-9_]+)\s*\("))
                {
                    invokedContracts.Add($"{typeName}.{match.Groups[1].Value}");
                }
            }

            foreach (var (cls, applyMethods) in StartupApplyContracts)
            {
                var found = applyMethods.Any(method => invokedContracts.Contains($"{cls}.{method}"));
                if (!found)
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"{cls} is not applied at startup: GameStartupPipeline must invoke {cls}.{string.Join(" or ", applyMethods)}() on a typed receiver.",
                        Severity = Severity,
                        AssemblyName = bootstrapFile,
                        TypeName = cls
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static string? ReadRequired(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
