using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.USS;

public sealed class UssStylesheetRule : IRule
{
    private static readonly HashSet<string> UssLonghand = new(StringComparer.Ordinal)
    {
        "all", "-unity-background-image-tint-color", "-unity-background-scale-mode",
        "-unity-editor-text-rendering-mode", "-unity-font", "-unity-font-definition",
        "-unity-material", "-unity-overflow-clip-box", "-unity-paragraph-spacing",
        "-unity-slice-bottom", "-unity-slice-left", "-unity-slice-right",
        "-unity-slice-scale", "-unity-slice-top", "-unity-slice-type",
        "-unity-text-align", "-unity-text-auto-size", "-unity-text-generator",
        "-unity-text-outline-color", "-unity-text-outline-width",
        "-unity-text-overflow-position",
        "align-content", "align-items", "align-self", "aspect-ratio",
        "background-color", "background-image", "filter", "background-position-x",
        "background-position-y", "background-repeat", "background-size",
        "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
        "border-bottom-width", "border-left-color", "border-left-width",
        "border-right-color", "border-right-width", "border-top-color",
        "border-top-left-radius", "border-top-right-radius", "border-top-width",
        "bottom", "color", "cursor", "display", "flex-basis",
        "flex-direction", "flex-grow", "flex-shrink", "flex-wrap", "font-size",
        "height", "justify-content", "left", "letter-spacing", "margin-bottom",
        "margin-left", "margin-right", "margin-top", "max-height", "max-width",
        "min-height", "min-width", "opacity", "overflow", "padding-bottom",
        "padding-left", "padding-right", "padding-top", "position", "right",
        "rotate", "scale", "text-overflow", "text-shadow", "top", "transform-origin",
        "transition-delay", "transition-duration", "transition-property",
        "transition-timing-function", "translate", "visibility", "white-space",
        "word-spacing", "width",
        "-unity-font-style", "-unity-text-outline-color",
    };

    private static readonly HashSet<string> UssShorthand = new(StringComparer.Ordinal)
    {
        "background", "background-position", "border", "border-color",
        "border-radius", "border-width", "flex", "font", "margin", "padding",
        "transition", "-unity-slice", "-unity-text-outline",
    };

    private static readonly HashSet<string> UssEasings = new(StringComparer.Ordinal)
    {
        "ease", "ease-in", "ease-out", "ease-in-out", "linear",
        "ease-in-sine", "ease-out-sine", "ease-in-out-sine",
        "ease-in-cubic", "ease-out-cubic", "ease-in-out-cubic",
        "ease-in-circ", "ease-out-circ", "ease-in-out-circ",
        "ease-in-elastic", "ease-out-elastic", "ease-in-out-elastic",
        "ease-in-back", "ease-out-back", "ease-in-out-back",
        "ease-in-bounce", "ease-out-bounce", "ease-in-out-bounce",
    };

    private static readonly Regex BadFuncRegex = new(@"\b(calc|min|max|clamp|color-mix)\s*\(", RegexOptions.Compiled);
    private static readonly Regex RelativeUnits = new(@"(?<![\w-])[0-9.]+(em|rem|ch|ex|vw|vh|vmin|vmax)(?![\w-])", RegexOptions.Compiled);

    public string Id => "KERN-USS-STYLESHEET";
    public string Description => "USS stylesheet validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var stylesDir = Path.Combine(projectRoot, "Assets", "Resources", "Styles");

        if (!Directory.Exists(stylesDir))
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = $"USS styles directory not found: {stylesDir}", Severity = Severity, AssemblyName = stylesDir });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var ussFiles = SourceScanner.EnumerateUssFiles(stylesDir).ToList();
        if (ussFiles.Count == 0)
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = "No .uss files found.", Severity = Severity, AssemblyName = stylesDir });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in ussFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = File.ReadAllText(file);
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
            var body = Regex.Replace(content, @"/\*[\s\S]*?\*/", m => new string('\n', m.Value.Split('\n').Length - 1));

            // Brace balance
            var open = (body.Split('{').Length - 1);
            var close = (body.Split('}').Length - 1);
            if (open != close)
            {
                violations.Add(new RuleViolation { RuleId = Id, Message = $"{relative}: unbalanced braces ({open} open, {close} close)", Severity = Severity, AssemblyName = relative });
            }

            var lines = body.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var decl = Regex.Match(line, @"^\s*(-?[a-zA-Z][\w-]*)\s*:");
                if (decl.Success)
                {
                    var prop = decl.Groups[1].Value;
                    if (!prop.StartsWith("--") && !UssLonghand.Contains(prop) && !UssShorthand.Contains(prop))
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"{relative}:{i + 1} property '{prop}' is not a valid UI Toolkit property",
                            Severity = Severity,
                            AssemblyName = relative,
                            Line = i + 1
                        });
                    }
                }

                // Bad functions
                foreach (Match m in BadFuncRegex.Matches(line))
                {
                    var func = m.Groups[1].Value;
                    var why = func switch
                    {
                        "calc" => "USS has no arithmetic in values",
                        "min" => "USS has no arithmetic in values",
                        "max" => "USS has no arithmetic in values",
                        "clamp" => "USS has no arithmetic in values",
                        "color-mix" => "not supported in USS",
                        _ => "unsupported function"
                    };
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"{relative}:{i + 1} function {func}() — {why}",
                        Severity = Severity,
                        AssemblyName = relative,
                        Line = i + 1
                    });
                }

                // Transition timing
                var timing = Regex.Match(line, @"transition-timing-function\s*:\s*([^;]+);");
                if (timing.Success)
                {
                    foreach (var raw in timing.Groups[1].Value.Split(','))
                    {
                        var value = raw.Trim();
                        if (value.StartsWith("var(") || string.IsNullOrEmpty(value))
                            continue;
                        if (!UssEasings.Contains(value))
                        {
                            violations.Add(new RuleViolation
                            {
                                RuleId = Id,
                                Message = $"{relative}:{i + 1} easing '{value}' is not in the USS set",
                                Severity = Severity,
                                AssemblyName = relative,
                                Line = i + 1
                            });
                        }
                    }
                }

                // Relative units (em, rem, ch, etc.)
                foreach (Match m in RelativeUnits.Matches(line))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"{relative}:{i + 1} относительная единица '{m.Value}': USS понимает только px и %",
                        Severity = Severity,
                        AssemblyName = relative,
                        Line = i + 1
                    });
                }

                // Token declarations and usage
                foreach (Match m in Regex.Matches(line, @"(--[a-z0-9-]+)\s*:"))
                    declared.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(line, @"var\(\s*(--[a-z0-9-]+)"))
                {
                    var token = m.Groups[1].Value;
                    if (!used.ContainsKey(token))
                        used[token] = new HashSet<string>(StringComparer.Ordinal);
                    used[token].Add(relative);
                }
            }
        }

        // Undeclared tokens
        foreach (var token in used.Keys.Where(t => !declared.Contains(t)).OrderBy(t => t))
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Token {token} used in {string.Join(", ", used[token].OrderBy(f => f))} but not declared",
                Severity = Severity,
                AssemblyName = stylesDir
            });
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
