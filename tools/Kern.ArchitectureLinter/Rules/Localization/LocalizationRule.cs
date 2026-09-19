using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Localization;

/// <summary>
/// Validates localization parity: key existence across languages,
/// placeholder sanity, and dead key detection.
/// </summary>
public sealed class LocalizationRule : IRule
{
    private const string LocalizationDir = "Assets/Resources/Localization";
    private const string UIDir = "Assets/Resources/UI";
    private static readonly Regex LiteralKeyRegex = new(
        @"\\?\""(?<key>[a-z][A-Za-z0-9_.-]*\.[A-Za-z0-9_.-]+)\\?\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Id => "KERN-LOCALIZATION";
    public string Description => "Localization parity and wiring checks";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var locDir = Path.Combine(projectRoot, LocalizationDir);

        if (!Directory.Exists(locDir))
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = $"Localization directory not found: {LocalizationDir}", Severity = Severity, AssemblyName = LocalizationDir });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var langFiles = Directory.EnumerateFiles(locDir, "*.json").OrderBy(f => f).ToList();
        if (langFiles.Count == 0)
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = "No localization JSON files found.", Severity = Severity, AssemblyName = LocalizationDir });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var dictionaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in langFiles)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            try
            {
                var json = File.ReadAllText(file);
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in Regex.Matches(json, @"^\s*""(?<key>[^""]+)""\s*:", RegexOptions.Multiline))
                {
                    string key = match.Groups["key"].Value;
                    if (!seenKeys.Add(key))
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"{Path.GetFileName(file)} declares key '{key}' more than once.",
                            Severity = Severity,
                            AssemblyName = Path.Combine(LocalizationDir, Path.GetFileName(file)),
                            TypeName = key,
                        });
                    }
                }

                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
                dictionaries[lang] = dict;
            }
            catch (Exception ex)
            {
                violations.Add(new RuleViolation { RuleId = Id, Message = $"{Path.GetFileName(file)}: invalid JSON ({ex.Message})", Severity = Severity, AssemblyName = file });
            }
        }

        if (dictionaries.Count == 0)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var allKeys = new HashSet<string>();
        var langs = dictionaries.Keys.ToList();
        foreach (var dict in dictionaries.Values)
            foreach (var key in dict.Keys)
                allKeys.Add(key);

        // Key parity
        foreach (var key in allKeys.OrderBy(k => k))
        {
            var missing = langs.Where(l => !dictionaries[l].ContainsKey(key)).ToList();
            if (missing.Count > 0)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Key '{key}' missing in {string.Join(", ", missing)}",
                    Severity = Severity,
                    AssemblyName = LocalizationDir,
                    TypeName = key
                });
            }
        }

        // Cyrillic in non-ru translations
        foreach (var lang in langs.Where(l => l != "ru"))
        {
            if (!dictionaries.TryGetValue(lang, out var dict)) continue;
            foreach (var (key, value) in dict)
            {
                if (Regex.IsMatch(value, @"[А-Яа-яЁё]"))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Key '{key}' in {lang} contains Cyrillic — translation missing.",
                        Severity = Severity,
                        AssemblyName = Path.Combine(LocalizationDir, lang + ".json"),
                        TypeName = key
                    });
                }
            }
        }

        // Placeholder sanity
        foreach (var (lang, dict) in dictionaries)
        {
            foreach (var (key, value) in dict)
            {
                var indices = Regex.Matches(value, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(i => i).ToList();
                if (indices.Count > 0 && indices.Zip(Enumerable.Range(0, indices.Count), (a, b) => a == b).Any(x => !x))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Key '{key}' in {lang} has non-contiguous placeholders {{{string.Join(",", indices)}}}",
                        Severity = Severity,
                        AssemblyName = Path.Combine(LocalizationDir, lang + ".json"),
                        TypeName = key
                    });
                }
            }
        }

        foreach (string key in allKeys)
        {
            string[]? expectedPlaceholders = null;
            string? expectedLanguage = null;
            foreach (string lang in langs)
            {
                if (!dictionaries[lang].TryGetValue(key, out string? value))
                {
                    continue;
                }

                string[] placeholders = Regex.Matches(value, @"\{(\d+)\}")
                    .Select(match => match.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(index => index, StringComparer.Ordinal)
                    .ToArray();
                if (expectedPlaceholders == null)
                {
                    expectedPlaceholders = placeholders;
                    expectedLanguage = lang;
                    continue;
                }

                if (!expectedPlaceholders.SequenceEqual(placeholders, StringComparer.Ordinal))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Key '{key}' has different placeholder sets in " +
                                  $"{expectedLanguage} and {lang}.",
                        Severity = Severity,
                        AssemblyName = LocalizationDir,
                        TypeName = key,
                    });
                }
            }
        }

        // Usage wiring
        var usedKeys = new HashSet<string>();
        var scriptsRoot = Path.Combine(projectRoot, "Assets/Scripts");
        var editorRoot = Path.Combine(projectRoot, "Assets/Editor");
        foreach (string file in SourceScanner
                     .EnumerateAllCsFiles(scriptsRoot, editorRoot)
                     .Where(file => IsProductionSource(projectRoot, file)))
        {
            var content = File.ReadAllText(file);
            foreach (Match match in LiteralKeyRegex.Matches(content))
            {
                string key = match.Groups["key"].Value;
                if (allKeys.Contains(key))
                {
                    usedKeys.Add(key);
                }
            }
        }

        var uiDir = Path.Combine(projectRoot, UIDir);
        if (Directory.Exists(uiDir))
        {
            foreach (var file in Directory.EnumerateFiles(uiDir, "*.uxml"))
            {
                var content = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(content, @"(?:text|tooltip)=""([^""]*)"""))
                {
                    if (Regex.IsMatch(m.Groups[1].Value, @"^[a-z][a-z0-9_.-]*\.[a-z0-9_.-]+$"))
                        usedKeys.Add(m.Groups[1].Value);
                }
            }
        }

        foreach (var key in usedKeys.OrderBy(k => k))
        {
            var missing = langs.Where(l => !dictionaries[l].ContainsKey(key)).ToList();
            if (missing.Count > 0)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Used key '{key}' missing in {string.Join(", ", missing)}",
                    Severity = Severity,
                    AssemblyName = LocalizationDir,
                    TypeName = key
                });
            }
        }

        foreach (var key in allKeys.Except(usedKeys).OrderBy(k => k))
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Dead key '{key}' declared but never used in production code.",
                Severity = Severity,
                AssemblyName = LocalizationDir,
                TypeName = key
            });
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static bool IsProductionSource(string projectRoot, string path)
    {
        string relativePath = SourceScanner.GetProjectRelativePath(projectRoot, path);
        string[] segments = relativePath.Split('/');
        return !segments.Any(segment =>
            segment is "Tests" or "Plugins" or "VContainer");
    }
}
