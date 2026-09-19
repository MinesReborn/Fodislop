#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Localization;

/// <summary>
/// Every keyed-UXML loader across all production scripts must call RegisterLocalizable.
/// Ported from check-architecture.js checkLocalizationRegistry().
/// </summary>
public sealed class LocalizationRegistryRule : IRule
{
    public string Id => "KERN-LOCALIZATION-REGISTRY";
    public string Description => "Localization registry validation";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex ResourcesLoad = new(@"Resources\.Load<VisualTreeAsset>\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex ResourcePaths = new(@"ResourcePaths\.([A-Za-z]+)Uxml", RegexOptions.Compiled);
    private static readonly Regex RegisterLocalizable = new(@"RegisterLocalizable", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // Find keyed UXML files
        var keyedUxml = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uiDir = Path.Combine(projectRoot, "Assets", "Resources", "UI");
        if (Directory.Exists(uiDir))
        {
            foreach (var file in Directory.EnumerateFiles(uiDir, "*.uxml"))
            {
                var content = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(content, @"text=""([^""]*)"""))
                {
                    if (Regex.IsMatch(m.Groups[1].Value, @"^[a-z][a-z0-9_.-]*\.[a-z0-9_.-]+$"))
                    {
                        keyedUxml.Add(Path.GetFileNameWithoutExtension(file));
                        break;
                    }
                }
            }
        }

        if (keyedUxml.Count == 0)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        // Find loaders
        var loaderFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var scriptsDir = Path.Combine(projectRoot, "Assets", "Scripts");

        foreach (var file in SourceScanner.EnumerateAllCsFiles(scriptsDir))
        {
            if (file.Contains("/Tests/")) continue;
            var content = File.ReadAllText(file);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in ResourcesLoad.Matches(content))
            {
                var baseName = m.Groups[1].Value.Split('/').Last();
                if (keyedUxml.Contains(baseName)) found.Add(baseName);
            }
            foreach (Match m in ResourcePaths.Matches(content))
            {
                if (keyedUxml.Contains(m.Groups[1].Value)) found.Add(m.Groups[1].Value);
            }

            if (found.Count > 0)
            {
                var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
                foreach (var baseName in found)
                {
                    if (!loaderFiles.ContainsKey(baseName))
                        loaderFiles[baseName] = new HashSet<string>();
                    loaderFiles[baseName].Add(relative);
                }
            }
        }

        foreach (var (baseName, files) in loaderFiles)
        {
            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(projectRoot, file));
                if (!RegisterLocalizable.IsMatch(content))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"загружает ключевой UXML ({baseName}.uxml), но не вызывает RegisterLocalizable. Смена языка до этой вьюхи не дойдёт.",
                        Severity = Severity,
                        TypeName = file
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
