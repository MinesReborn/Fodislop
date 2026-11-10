#nullable enable

using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rules;

/// <summary>
/// Rejects new production files above the line limit. A finite debt list exists
/// for legacy files that are already over the limit — they must not grow.
/// Ported from check-architecture.js checkOversizedProductionFiles().
/// </summary>
public sealed class OversizedFileRule : IRule
{
    public string Id => "KERN-OVERSIZED-FILE";
    public string Description => "Oversized production file detection";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private const int LineLimit = 500;

    // Список исключений закрыт: он сократился до нуля и обратно не заводится.
    // Файл сверх лимита — нарушение без права на поблажку; разбирайте
    // ответственности, а не вносите путь сюда.

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets/Scripts");

        foreach (var file in CSharpSourceFiles.EnumerateCsFiles(scriptsRoot, "Tests", "Editor", "VContainer"))
        {
            var relative = ProjectSourcePaths.GetProjectRelativePath(context.ProjectRoot, file);

            var lineCount = File.ReadLines(file).Count();
            if (lineCount > LineLimit)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"{lineCount} строк превышает лимит {LineLimit} для production файлов. Разделите ответственности вместо создания god-object.",
                    Severity = Severity,
                    TypeName = relative
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
