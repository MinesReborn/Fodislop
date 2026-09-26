using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kern.TerrainRasterTests;

internal static class Program
{
    private static readonly string[] Mutations =
    [
        "double-quantize-fragments",
        "ao-flat-contact",
        "ao-to-black",
    ];

    public static int Main()
    {
        string root = FindProjectRoot();
        string shim = Read(root, "tools/Kern.LightingTests/NativeTransportShim.cpp");
        string loader = Read(root, "Assets/Shaders/Terrain/TerrainCellData.hlsl");
        string terrainContour = Read(root, "Assets/Shaders/Terrain/TerrainContour.hlsl");
        string terrainGeometryContract = Read(root, "Assets/Shaders/Terrain/TerrainGeometryContract.hlsl");
        string terrainGeometry = Read(root, "Assets/Shaders/Terrain/TerrainGeometry.hlsl");
        string terrainShader = Read(root, "Assets/Shaders/Terrain/Terrain.shader");
        string scenario = Read(root, "tools/Kern.TerrainRasterTests/scenario.cpp");
        string aoShim = Read(root, "tools/Kern.TerrainRasterTests/ao-field.cpp");
        string ao = Read(root, "Assets/Shaders/Terrain/TerrainAmbientOcclusion.hlsl");
        string terrainSampling = Read(root, "Assets/Shaders/Terrain/TerrainSampling.hlsl");
        string lighting = Read(root, "Assets/Shaders/Terrain/TerrainLightingData.hlsl");
        string terrainGeometryUv = SliceBetween(
                terrainSampling,
                "float TerrainUvCross(",
                "void TerrainSetResolvedTileIdentity(")
            .Replace("[unroll]", string.Empty, StringComparison.Ordinal);

        if (!terrainGeometryUv.Contains("TerrainResolveGeometryTileUV(", StringComparison.Ordinal))
        {
            return Fail("Production terrain geometry UV remap was not found");
        }

        if (terrainShader.Contains("TerrainReliefRimRaw(", StringComparison.Ordinal) ||
            terrainShader.Contains("TerrainReliefRim(surface", StringComparison.Ordinal))
        {
            return Fail("Terrain.shader must not apply the retired relief rim to visible albedo");
        }

        if (Regex.Matches(terrainShader, "BuildTerrainSurfaceInputs\\(").Count != 3)
        {
            return Fail("Expected exactly one surface parse in each terrain pass");
        }

        if (Regex.Matches(terrainShader + Read(root, "Assets/Shaders/Terrain/TerrainLightingFieldCommon.hlsl"), "PixelArtSampleUV\\(").Count != 2)
        {
            return Fail("Visible and field albedo paths must share the pixel-grid UV correction");
        }

        int materialFieldPassAt = terrainShader.IndexOf("Name \"LightingMaterialField\"", StringComparison.Ordinal);
        int ambientOcclusionPassAt = terrainShader.IndexOf("Name \"LightingAmbientOcclusionField\"", StringComparison.Ordinal);
        if (materialFieldPassAt < 0 || ambientOcclusionPassAt < 0 || materialFieldPassAt > ambientOcclusionPassAt)
        {
            return Fail("Terrain material and AO fields must have distinct ordered shader passes");
        }

        string materialFieldPass = terrainShader[materialFieldPassAt..ambientOcclusionPassAt];
        string ambientOcclusionPass = terrainShader[ambientOcclusionPassAt..];
        if (materialFieldPass.Contains("TerrainGeometryCoverageForField", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("TerrainGeometrySignedDistance", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("if (exteriorDistance >= _TerrainAmbientOcclusionDistance)", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("_TerrainAmbientOcclusionDistance, exteriorDistance)", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("ColorMask A", StringComparison.Ordinal) ||
            ambientOcclusionPass.Contains("TerrainAnimationSampling.hlsl", StringComparison.Ordinal) ||
            ambientOcclusionPass.Contains("_PrismaticFlowMap", StringComparison.Ordinal))
        {
            return Fail("AO must have an alpha-only geometry pass without transport or flow-map dependencies");
        }

        int debugAt = terrainShader.IndexOf("if (KernTerrainDebugActive())", StringComparison.Ordinal);
        int debugClipAt = terrainShader.IndexOf("clip(cellCoverage - 0.5)", debugAt, StringComparison.Ordinal);
        if (debugAt < 0 || debugClipAt < debugAt)
        {
            return Fail("The terrain debug silhouette must be clipped before its AO/rim output");
        }

        int rawAoDebugAt = terrainShader.IndexOf("_WorldLightDebugView == 9)", StringComparison.Ordinal);
        int rawAoClipAt = terrainShader.IndexOf("clip(cellCoverage - 0.5)", rawAoDebugAt, StringComparison.Ordinal);
        int rawAoSampleAt = terrainShader.IndexOf("KernSampleTerrainAmbientOcclusion(", rawAoDebugAt, StringComparison.Ordinal);
        if (rawAoDebugAt < 0 || rawAoClipAt < rawAoDebugAt || rawAoSampleAt < rawAoClipAt)
        {
            return Fail("The raw AO debug output must use the displaced foreground silhouette");
        }

        string rim = SliceBetween(terrainContour, "// Выключатель каймы.", "// The visible terrain");
        string contour = terrainContour[..terrainContour.IndexOf("float2 QuantizeTerrainFaceUV(", StringComparison.Ordinal)];
        const string quantizeUv = """
            float2 QuantizeTerrainFaceUV(float2 uv)
            {
                float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
                return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
            }
            """;
        const string extra = """
            float2 round(float2 a) { return {std::round(a.x), std::round(a.y)}; }
            float distance(float2 a, float2 b) { return length(a - b); }
            float lerp(float a, float b, float t) { return a + (b-a)*t; }
            float2 lerp(float2 a, float2 b, float2 t) { return a + (b-a)*t; }
            float4 round(float4 a) { return {std::round(a.x),std::round(a.y),std::round(a.z),std::round(a.w)}; }
            float4 make_float4(float a, float2 b, float c) { return {a,b.x,b.y,c}; }
            float _TestFwidth = 0.0f;
            float fwidth(float) { return _TestFwidth; }
            float smoothstep(float a, float b, float x)
            {
              float t = std::clamp((x-a)/(b-a), 0.0f, 1.0f);
              return t*t*(3.0f-2.0f*t);
            }
            """;
        const string terrainUniforms = """
            float _OrganicBendStrength = 1.0;
            float _OrganicBendPivot = 0.35;
            float _RoundableCornerRadius = 0.51;
            float _ReliefRimQuantizationEnabled = 0.0;
            float _TerrainAmbientOcclusionDistance = 0.25;
            float _ReliefRimDistanceScale = 2.0;
            float _ReliefRimFalloff = 0.5;
            """;

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"kern-terrain-raster-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string cppPath = Path.Combine(temporaryDirectory, "test.cpp");
            string executablePath = Path.Combine(temporaryDirectory, "test");
            foreach (string? mutation in new string?[] { null }.Concat(Mutations))
            {
                string candidateLoader = loader;
                string candidateGeometry = terrainGeometry;
                string candidateAo = ao;

                if (mutation == "double-quantize-fragments")
                {
                    string before = """
                            return TerrainPolygonContains(
                                samplePosition,
                                cornersX,
                                cornersY,
                                packedEdges,
                                true) ? 1.0 : 0.0;
                        """;
                    string after = """
                            float2 fragmentGridSample = (floor(samplePosition * 32.0) + 0.5) / 32.0;
                            return TerrainPolygonContains(
                                fragmentGridSample,
                                cornersX,
                                cornersY,
                                packedEdges,
                                true) ? 1.0 : 0.0;
                        """;
                    candidateGeometry = candidateGeometry.Replace(before, after, StringComparison.Ordinal);
                    if (candidateGeometry == terrainGeometry) return Fail("double-quantize-fragments mutation is stale");
                }

                if (mutation == "ao-flat-contact")
                {
                    candidateAo = candidateAo.Replace("contact * _TerrainAmbientOcclusionStrength", "_TerrainAmbientOcclusionStrength", StringComparison.Ordinal);
                    if (candidateAo == ao) return Fail("ao-flat-contact mutation is stale");
                }

                if (mutation == "ao-to-black")
                {
                    candidateAo = candidateAo.Replace(
                        "1.0 - (occlusion * (1.0 - _TerrainAmbientOcclusionFloor))",
                        "1.0 - occlusion",
                        StringComparison.Ordinal);
                    if (candidateAo == ao) return Fail("ao-to-black mutation is stale");
                }

                candidateAo = candidateAo
                    .Replace("Texture2D<float4>", "AoTexture", StringComparison.Ordinal)
                    .Replace("SamplerState", "int", StringComparison.Ordinal);
                string source = shim + extra + aoShim + terrainUniforms + Translate(
                    terrainGeometryContract + candidateGeometry + candidateLoader + lighting +
                    contour + quantizeUv + rim + terrainGeometryUv + candidateAo) + scenario;
                File.WriteAllText(cppPath, source);

                ProcessResult compile = Run("clang++", ["-std=c++20", "-O2", "-ffp-contract=off", cppPath, "-o", executablePath], temporaryDirectory);
                Console.Out.Write(compile.StandardOutput);
                Console.Error.Write(compile.StandardError);
                if (compile.ExitCode != 0) return compile.ExitCode;

                ProcessResult result = Run(executablePath, [], temporaryDirectory);
                if (mutation is null)
                {
                    Console.Out.Write(result.StandardOutput);
                    if (result.ExitCode != 0) return Fail(result.StandardError);
                }
                else if (result.ExitCode == 0)
                {
                    return Fail($"Regression check accepted mutation: {mutation}");
                }
                else
                {
                    Console.WriteLine($"Mutation {mutation} rejected: {result.StandardError.Trim()}");
                }
            }

            return 0;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string Translate(string source)
    {
        source = Regex.Replace(source, "^#.*$", string.Empty, RegexOptions.Multiline)
            .Replace("[unroll]", string.Empty, StringComparison.Ordinal)
            .Replace("[branch]", string.Empty, StringComparison.Ordinal)
            .Replace("inout TerrainTileUvResult tile", "TerrainTileUvResult& tile", StringComparison.Ordinal)
            .Replace("out float2 nearestPosition", "float2& nearestPosition", StringComparison.Ordinal)
            .Replace("Texture2D<float4>", "Texture", StringComparison.Ordinal)
            .Replace("(TerrainCellVertex)0", "TerrainCellVertex{}", StringComparison.Ordinal);
        source = Regex.Replace(source, @"\(int2\)round\(([^)]+)\)", "make_int2(round($1))", RegexOptions.CultureInvariant);
        return Regex.Replace(source, @"\b(float[234]|int[23]|uint[23])\(", "make_$1(", RegexOptions.CultureInvariant);
    }

    private static string SliceBetween(string source, string start, string end)
    {
        int startIndex = source.IndexOf(start, StringComparison.Ordinal);
        int endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0) throw new InvalidDataException($"Could not locate expected source section: {start}");
        return source[startIndex..endIndex];
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Shaders/Terrain/Terrain.shader"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static ProcessResult Run(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
