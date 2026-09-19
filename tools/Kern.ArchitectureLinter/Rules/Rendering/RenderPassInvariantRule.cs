#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Render passes must not hardcode effect bypasses or unconditional overrides of .active.
/// Ported from check-architecture.js checkRenderPassInvariants().
/// </summary>
public sealed class RenderPassInvariantRule : IRule
{
    public string Id => "KERN-RENDER-PASS-INVARIANT";
    public string Description => "Render pass invariant validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private const string RenderPassPath = "Assets/Scripts/Rendering/PostProcessing/Pipeline/PostProcessRenderPass.cs";

    private static readonly Regex BypassBlock = new(@"if \(\s*(?:PostProcessRuntimeState\.)?BypassPostProcessEffects\s*\)", RegexOptions.Compiled);
    private static readonly Regex EffectDisable = new(@"^\s*(?:bloomActive|vignetteActive|cgActive|eigengrauActive|mbActive)\s*=\s*false\s*;", RegexOptions.Multiline);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var path = Path.Combine(context.ProjectRoot, RenderPassPath);

        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var content = File.ReadAllText(path);
        var lines = content.Split('\n');
        var inBypassBlock = false;
        var bypassBraceDepth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (BypassBlock.IsMatch(line))
            {
                inBypassBlock = true;
                bypassBraceDepth = 0;
            }

            if (inBypassBlock)
            {
                bypassBraceDepth += line.Count(c => c == '{');
                bypassBraceDepth -= line.Count(c => c == '}');
                if (bypassBraceDepth <= 0 && line.Contains("}"))
                    inBypassBlock = false;
                continue;
            }

            // Outside BypassPostProcessEffects block
            if (EffectDisable.IsMatch(line))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"строка {i + 1}: Безусловное отключение эффекта ({line.Trim()}) вне BypassPostProcessEffects. Эффекты должны управляться volume компонентами.",
                    Severity = Severity,
                    TypeName = $"{RenderPassPath}:{i + 1}"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
