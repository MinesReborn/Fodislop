using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Validates shader color library includes.
/// Shaders using color functions must include ShaderLibrary/Color.hlsl.
/// </summary>
public sealed class ShaderColorLibraryRule : IRule
{
    private static readonly string[] ColorFunctions =
    {
        "SRGBToLinear", "LinearToSRGB", "FastSRGBToLinear", "FastLinearToSRGB",
        "Luminance", "RGBToHSV", "HSVToRGB",
    };

    public string Id => "KERN-SHADER-COLOR";
    public string Description => "Shader color library include checks";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var assetsRoot = Path.Combine(projectRoot, "Assets");

        if (!Directory.Exists(assetsRoot))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        foreach (var file in SourceScanner.EnumerateShaderFiles(assetsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = File.ReadAllText(file);
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
            var includesColor = source.Contains("ShaderLibrary/Color.hlsl");

            foreach (var fn in ColorFunctions)
            {
                var used = Regex.IsMatch(source, $@"\b{Regex.Escape(fn)}\s*\(");
                if (!used || includesColor)
                    continue;

                var definedLocally = Regex.IsMatch(source, $@"(float|half|real)[1-4]?\s+{Regex.Escape(fn)}\s*\(");
                if (definedLocally)
                    continue;

                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Shader calls {fn} but does not include Color.hlsl and does not define it locally.",
                    Severity = Severity,
                    AssemblyName = relative
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
