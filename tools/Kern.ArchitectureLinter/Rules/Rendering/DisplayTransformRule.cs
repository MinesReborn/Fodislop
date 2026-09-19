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

        Dictionary<string, float[]> matrices = ReadMatrices(source);
        if (!matrices.TryGetValue("toLms", out float[]? toLms) ||
            !matrices.TryGetValue("fromLms", out float[]? fromLms))
        {
            AddViolation(violations, path, "White balance requires both toLms and fromLms matrices.");
        }
        else
        {
            float worstDeviation = 0f;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    float value = 0f;
                    for (int index = 0; index < 3; index++)
                    {
                        value += fromLms[(row * 3) + index] * toLms[(index * 3) + column];
                    }

                    float expected = row == column ? 1f : 0f;
                    worstDeviation = Math.Max(worstDeviation, Math.Abs(value - expected));
                }
            }

            if (worstDeviation > 0.002f)
            {
                AddViolation(
                    violations,
                    path,
                    $"toLms and fromLms are not inverses (worst deviation {worstDeviation:F6}).");
            }
        }

        Require(
            violations,
            path,
            source,
            @"unitPower\s*=\s*1\.0\s*-\s*step\([^;]*abs\(power\s*-\s*1\.0\)\)",
            "Neutral ASC CDL power must preserve negative log values.");
        Require(
            violations,
            path,
            source,
            @"return\s+lerp\(powered,\s*graded,\s*unitPower\)",
            "ASC CDL must select the sign-preserving neutral-power path.");
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
        Require(violations, path, source, @"void\s+CompositeFinal", "Scene-linear artistic pass is required.");
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
        int hdr = source.IndexOf("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/HDROutput.hlsl\"", StringComparison.Ordinal);
        if (common < 0 || color <= common || hdr <= color)
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
        Require(violations, path, source, @"displayPass\s*\?\s*RenderPassEvent.AfterRenderingPostProcessing",
            "Display effects must execute after URP tone mapping.");
        Require(violations, path, source, @"output.paperWhite.value",
            "Effect calibration must use the same VolumeStack paper white as URP.");
        Require(violations, path, source, @"_outputSignature\s*!=\s*signature",
            "Display changes must invalidate temporal history.");
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
        Require(
            violations,
            path,
            source,
            @"temporalActive\s*=\s*PostProcessRuntimeState\.DebugView\s*==\s*PostProcessDebugView\.None",
            "Debug views must disable temporal history.");
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
                    @"(?:hdrOutput|passData\.HDROutput)\s*\?\s*cameraData\.hdrDisplayColorGamut\s*:\s*ColorGamut\.sRGB",
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

    private static Dictionary<string, float[]> ReadMatrices(string source)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     source,
                     @"const\s+float3x3\s+(\w+)\s*=\s*float3x3\(([^)]*)\)",
                     Invariant))
        {
            if (TryReadFloatList(match.Groups[2].Value, out float[] values) && values.Length == 9)
            {
                result[match.Groups[1].Value] = values;
            }
        }

        return result;
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
