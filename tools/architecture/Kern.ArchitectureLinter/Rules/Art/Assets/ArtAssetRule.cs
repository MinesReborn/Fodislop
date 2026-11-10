#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Art;

/// <summary>
/// Каждый путь к арту, записанный в коде, обязан существовать.
/// </summary>
///
/// Путь в коде — контракт без компилятора: опечатка в имени файла не ломает
/// сборку и не видна в диффе, а на экране вместо картинки оказывается пустое
/// место, которое списывают на что угодно, кроме буквы в строке.
public sealed class ArtAssetRule : IRule
{
    public string Id => "KERN-ART-ASSET";
    public string Description => "Art paths referenced from code must exist";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly Regex PathLiteral = new(@"""([^""\r\n]*\.png)""", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets/Scripts");
        if (!Directory.Exists(scriptsRoot))
        {
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        foreach (var file in CSharpSourceFiles.EnumerateCsFiles(scriptsRoot, "Tests", "VContainer"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = ProjectSourcePaths.GetProjectRelativePath(projectRoot, file);
            var lines = CSharpSourceText.StripComments(File.ReadAllText(file)).Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (Match match in PathLiteral.Matches(lines[index]))
                {
                    var literal = match.Groups[1].Value;
                    var assetPath = ResolveAssetPath(literal);
                    if (assetPath is null)
                    {
                        continue;
                    }

                    var full = Path.Combine(
                        projectRoot,
                        assetPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(full))
                    {
                        continue;
                    }

                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"в коде записан путь '{literal}', а файла нет: {assetPath}",
                        Severity = Severity,
                        TypeName = relative,
                        Line = index + 1
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    // Ветки без явного корня не проверяются: имя вида "aliveRadar.png" ищется
    // загрузчиком по своим правилам, и приговор по нему был бы догадкой.
    private static string? ResolveAssetPath(string literal)
    {
        if (literal.Contains('{') || literal.Contains('}'))
        {
            return null;
        }

        if (literal.StartsWith("Assets/", StringComparison.Ordinal))
        {
            return literal;
        }

        if (literal.StartsWith("Textures/", StringComparison.Ordinal) ||
            literal.StartsWith("Resources/", StringComparison.Ordinal))
        {
            return "Assets/" + literal;
        }

        return null;
    }
}
