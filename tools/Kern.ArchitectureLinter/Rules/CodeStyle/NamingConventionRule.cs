#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.CodeStyle;

/// <summary>
/// Validates C# naming conventions. Private fields must start with underscore.
/// Interfaces must start with 'I'.
/// Scans source files directly (not DLLs) to catch changes immediately.
/// </summary>
public sealed class NamingConventionRule : IRule
{
    public string Id => "KERN-NAMING";
    public string Description => "Naming convention validation";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    // Pattern to match private fields: private [modifiers] Type Name = ;
    // Excludes properties (have {), expression-bodied properties (=>, not =),
    // methods (have parentheses), and nested types
    private static readonly Regex PrivateFieldPattern = new(
        @"^\s*private\s+(?:static\s+|readonly\s+|volatile\s+)*(?!class\b|struct\b|enum\b|interface\b)(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*(?:<[^>]*>)?\s*\??\s+([_A-Za-z][A-Za-z0-9_]*)\s*(?:=(?![>=])|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Pattern to match interface declarations
    private static readonly Regex InterfacePattern = new(
        @"\binterface\s+([A-Za-z][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    // Compiler-generated names to skip
    private static readonly HashSet<string> CompilerGenerated = new()
    {
        "BackingField"
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets", "Scripts");
        var editorRoot = Path.Combine(context.ProjectRoot, "Assets", "Editor");

        foreach (var file in SourceScanner.EnumerateAllCsFiles(scriptsRoot, editorRoot))
        {
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);
            if (IsExcluded(relative)) continue;

            var source = File.ReadAllText(file);
            var stripped = SourceScanner.StripComments(source);

            // Check interfaces in non-comment code
            foreach (Match m in InterfacePattern.Matches(stripped))
            {
                var name = m.Groups[1].Value;
                if (!name.StartsWith("I"))
                {
                    var line = source.Substring(0, m.Index).Count(c => c == '\n') + 1;
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Interface '{name}' does not start with 'I'.",
                        Severity = Severity,
                        TypeName = $"{relative}:{line}"
                    });
                }
            }

            // Check private fields in non-comment code
            foreach (Match m in PrivateFieldPattern.Matches(stripped))
            {
                var fieldName = m.Groups[1].Value;

                // Skip if already has underscore
                if (fieldName.StartsWith("_"))
                    continue;

                // Skip compiler-generated backing fields
                if (IsCompilerGeneratedBackingField(fieldName, source, m.Index))
                    continue;

                var line = source.Substring(0, m.Index).Count(c => c == '\n') + 1;
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Private field '{fieldName}' does not start with '_'.",
                    Severity = Severity,
                    TypeName = $"{relative}:{line}"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static bool IsExcluded(string relative)
    {
        return relative.StartsWith("Assets/Scripts/VContainer/") ||
               relative.StartsWith("Assets/Plugins/") ||
               relative.StartsWith("Packages/");
    }

    private static bool IsCompilerGeneratedBackingField(string fieldName, string source, int matchIndex)
    {
        // Check if this is a backing field for a property (has <...>BackingField pattern)
        if (fieldName.Contains("BackingField") || fieldName.StartsWith("<"))
            return true;

        return false;
    }
}
