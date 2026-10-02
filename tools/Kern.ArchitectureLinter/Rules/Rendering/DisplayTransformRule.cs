using System.Globalization;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Core;
using Mono.Cecil;

namespace Kern.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Validates display transform shader and Render Graph invariants.
/// Checks matrix reciprocity and gamma curve application order.
/// </summary>
public sealed class DisplayTransformRule : IRule
{
    private const RegexOptions Invariant = RegexOptions.CultureInvariant;

    public string Id => "KERN-DISPLAY-TRANSFORM";
    public string Description => "Display transform shader and Render Graph invariants";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        string shaderRoot = Path.Combine(
            context.ProjectRoot,
            "Assets",
            "Resources",
            "Shaders",
            "PostProcessing");

        CheckFile(violations, Path.Combine(shaderRoot, "ColorGrading.hlsl"), CheckColorGrading);
        CheckFile(violations, Path.Combine(shaderRoot, "PostProcess.compute"), CheckPostProcessShader);
        CheckFile(violations, Path.Combine(shaderRoot, "WorldBloom.compute"), CheckWorldBloomShader);
        CheckFile(violations, Path.Combine(shaderRoot, "WorldBloomAdd.shader"), CheckWorldBloomAddShader);
        CheckFile(violations, Path.Combine(shaderRoot, "Scopes.compute"), CheckScopesShader);
        CheckFile(
            violations,
            Path.Combine(context.ProjectRoot, "Assets", "Scripts", "Rendering", "PostProcessing", "Scopes", "ScopesRenderPass.cs"),
            CheckHDRDisplayAccess);
        CheckFile(
            violations,
            Path.Combine(
                context.ProjectRoot,
                "Assets",
                "Scripts",
                "Rendering",
                "PostProcessing",
                "Pipeline",
                "PostProcessRenderPass.cs"),
            CheckRenderPassSource);

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void CheckFile(
        ICollection<RuleViolation> violations,
        string path,
        Action<ICollection<RuleViolation>, string, string> check)
    {
        if (!File.Exists(path))
        {
            AddViolation(violations, path, $"Required display-pipeline file '{Path.GetFileName(path)}' was not found.");
            return;
        }

        check(violations, path, File.ReadAllText(path));
    }

    private void CheckColorGrading(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        if (!source.TrimEnd().EndsWith(
                "#endif // KERN_COLOR_GRADING_INCLUDED",
                StringComparison.Ordinal))
        {
            AddViolation(
                violations,
                path,
                "Include guard must close with #endif // KERN_COLOR_GRADING_INCLUDED.");
        }

        Require(
            violations,
            path,
            source,
            @"saturate\(\(color\s*/\s*scale\s*-",
            "LUT lookup must normalize HDR color before the table domain clamp.");
        Require(
            violations,
            path,
            source,
            @"lutColor\s*\*=\s*scale",
            "LUT output must restore the HDR scale removed before lookup.");
    }

    private void CheckPostProcessShader(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        CheckHDRIncludes(violations, path, source);
        if (Regex.IsMatch(source, @"\b(?:float|half|real)\s+Luminance\s*\(", Invariant))
        {
            AddViolation(violations, path, "Use Color.hlsl Luminance; a local definition conflicts on Metal.");
        }
        if (Regex.IsMatch(source, @"void\s+(?:CompositeFinal|BloomPrefilter|BloomUpsampleComposite)\b", Invariant))
        {
            AddViolation(violations, path, "Display shader must not contain the retired screen-space bloom kernels.");
        }
        Require(violations, path, source, @"void\s+DisplayFinal", "Display effects must be separated from the scene pass.");
        Require(
            violations,
            path,
            source,
            @"source\.rgb\s*/\s*(?:_DisplayPaperWhiteNits|paperWhite)",
            "Display effects must normalize absolute HDR nits.");
        Require(violations, path, source, @"ToDisplayOutput\(color\)", "Display effects must restore URP output units.");
        if (source.Contains("KernDisplayTransform(", StringComparison.Ordinal) ||
            source.Contains("ConvertOutputGamut(", StringComparison.Ordinal) ||
            source.Contains("headroom * excess", StringComparison.Ordinal))
        {
            AddViolation(violations, path, "Custom effects must not perform tone mapping or own display gamut conversion.");
        }
    }

    private void CheckWorldBloomShader(ICollection<RuleViolation> violations, string path, string source)
    {
        foreach (string kernel in new[] { "Prefilter", "Downsample", "Upsample" })
        {
            Require(violations, path, source, @"void\s+" + kernel + @"\b",
                "World-grid bloom requires production kernel " + kernel + ".");
        }
    }

    private void CheckWorldBloomAddShader(ICollection<RuleViolation> violations, string path, string source)
    {
        Require(violations, path, source, @"float4\s+AddBloom\b", "World-grid bloom requires scene-linear additive composition.");
        Require(violations, path, source, @"Blend\s+One\s+\[_WorldBloomSceneBlend\]", "Bloom must preserve its production scene blend contract.");
    }

    private void CheckScopesShader(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        CheckHDRIncludes(violations, path, source);
        Require(violations, path, source, @"RotateOutputSpaceToRec709\(color\)",
            "Rec.709 diagnostic axes must convert from the active HDR output gamut.");
        Require(
            violations,
            path,
            source,
            @"_ScopeSignalScale",
            "Scopes must map the HDR signal against the configured display peak.");
        Require(
            violations,
            path,
            source,
            @"WaveformBuffer[^\n]*_ScopeParams\.z",
            "Waveform must use its own density normalization.");
        Require(
            violations,
            path,
            source,
            @"VectorscopeBuffer[^\n]*_ScopeParams\.w",
            "Vectorscope must use its own density normalization.");
    }

    private void CheckHDRIncludes(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        int common = source.IndexOf("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl\"", StringComparison.Ordinal);
        int color = source.IndexOf("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl\"", StringComparison.Ordinal);
        int HDR = source.IndexOf("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/HDROutput.hlsl\"", StringComparison.Ordinal);
        if (common < 0 || color <= common || HDR <= color)
        {
            AddViolation(violations, path, "HDR helpers require Common.hlsl and Color.hlsl (including ACES) before HDROutput.hlsl.");
        }
    }

    private void CheckRenderPassSource(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        CheckHDRDisplayAccess(violations, path, source);
        Require(violations, path, source, @"renderPassEvent\s*=\s*RenderPassEvent\.AfterRenderingPostProcessing\s*;",
            "Display effects must execute after URP tone mapping.");
        Require(violations, path, source, @"renderPassEvent2D\s*=\s*RenderPassEvent2D\.AfterRenderingPostProcessing\s*;",
            "2D display effects must execute after URP tone mapping.");
        Require(violations, path, source, @"output.paperWhite.value",
            "Effect calibration must use the same VolumeStack paper white as URP.");
        Require(
            violations,
            path,
            source,
            @"TextureDesc\s+activeColorDesc\s*=\s*activeColor\.GetDescriptor\(renderGraph\)",
            "Render Graph temporary targets must inherit TextureDesc from activeColor.");
        Require(
            violations,
            path,
            source,
            @"TextureHandle\s+intermediateTexture\s*=\s*renderGraph\.CreateTexture\(desc\)",
            "Render Graph intermediate color must use the active-color-derived descriptor.");
    }

    private void CheckHDRDisplayAccess(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        foreach (string line in source.Split('\n'))
        {
            if (line.Contains("cameraData.hdrDisplayColorGamut", StringComparison.Ordinal) &&
                !Regex.IsMatch(line,
                    @"(?:hdrOutput|HDROutput|passData\.(?:HdrOutput|HDROutput))\s*\?\s*cameraData\.hdrDisplayColorGamut\s*:\s*ColorGamut\.sRGB",
                    Invariant))
            {
                AddViolation(violations, path, "Read HDR display gamut only when HDR output is active; SDR must use sRGB without querying HDR display information.");
            }
        }
    }

    private void CheckWhitePreservingMatrix(
        ICollection<RuleViolation> violations,
        string path,
        string source,
        string matrixName)
    {
        Match block = Regex.Match(
            source,
            Regex.Escape(matrixName) + @"\s*=\s*float3x3\(([^)]*)\)",
            Invariant);
        if (!block.Success || !TryReadFloatList(block.Groups[1].Value, out float[] values) || values.Length != 9)
        {
            AddViolation(violations, path, $"Matrix {matrixName} must contain exactly 9 numbers.");
            return;
        }

        for (int row = 0; row < 3; row++)
        {
            float sum = values[row * 3] + values[(row * 3) + 1] + values[(row * 3) + 2];
            if (Math.Abs(sum - 1f) > 0.002f)
            {
                AddViolation(
                    violations,
                    path,
                    $"Matrix {matrixName} row {row + 1} sums to {sum:F6} instead of 1.0.");
            }
        }
    }

    private static bool TryReadFloatList(string source, out float[] values)
    {
        MatchCollection matches = Regex.Matches(
            source,
            @"-?\d+(?:\.\d+)?(?:e[+-]?\d+)?",
            RegexOptions.IgnoreCase | Invariant);
        values = new float[matches.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            if (!float.TryParse(
                    matches[index].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]))
            {
                values = [];
                return false;
            }
        }

        return true;
    }

    private void Require(
        ICollection<RuleViolation> violations,
        string path,
        string source,
        string pattern,
        string message,
        RegexOptions options = RegexOptions.None)
    {
        if (Regex.IsMatch(source, pattern, options | Invariant))
        {
            return;
        }

        AddViolation(violations, path, message);
    }

    private void AddViolation(
        ICollection<RuleViolation> violations,
        string path,
        string message)
    {
        violations.Add(new RuleViolation
        {
            RuleId = Id,
            Message = message,
            Severity = Severity,
            AssemblyName = path,
        });
    }
}
