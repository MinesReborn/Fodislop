using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;
using Mono.Cecil;
using System.Text.RegularExpressions;

namespace Kern.ArchitectureLinter.Rules.Settings;

/// <summary>
/// Flags settings sections that declare no instance fields but are still
/// registered in ClientConfig, migration, repository and probe. An empty
/// section serializes as "{}" into every user config and drags five
/// wiring points for zero behavior (precedent: WorldLightingSettings).
/// </summary>
public sealed class EmptySettingsSectionRule : IRule
{
    private const string SettingsDir = "Assets/Scripts/Core/Interfaces/Contracts/Settings";

    private static readonly string[] ExtraFiles =
    [
        "Assets/Scripts/Rendering/Settings/Contracts/GraphicsQualitySettings.cs",
    ];

    // Instance field, not const and not static readonly (same shape as wiring rule).
    private static readonly Regex FieldRegex = new(
        @"^\s*public\s+(?!const\b)(?!static\s+readonly\b)[A-Za-z0-9_<>\[\],.\s?]+?\s+([A-Za-z0-9_]+)\s*(?:=(?!=)|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ClassRegex = new(
        @"^\s*public\s+(?:sealed\s+)?class\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public string Id => "KERN-SETTINGS-EMPTY";
    public string Description => "Empty settings sections with live wiring";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var files = new List<string>();
        string settingsRoot = Path.Combine(context.ProjectRoot, SettingsDir);
        if (Directory.Exists(settingsRoot))
            files.AddRange(Directory.EnumerateFiles(settingsRoot, "*.cs"));
        foreach (string extra in ExtraFiles)
        {
            string full = Path.Combine(context.ProjectRoot, extra);
            if (File.Exists(full))
                files.Add(full);
        }

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = File.ReadAllText(file);
            string noComments = SourceScanner.StripComments(content);
            foreach (Match cls in ClassRegex.Matches(noComments))
            {
                string className = cls.Groups[1].Value;
                int fieldCount = FieldRegex.Matches(noComments).Count;
                if (fieldCount == 0)
                {
                    string relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);
                    int line = noComments.Substring(0, cls.Index).Split('\n').Length;
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Settings section '{className}' declares no instance fields but stays wired (ClientConfig/migration/probe). Remove the section or justify it.",
                        Severity = Severity,
                        AssemblyName = relative,
                        TypeName = relative,
                        MemberName = className,
                        Line = line,
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
