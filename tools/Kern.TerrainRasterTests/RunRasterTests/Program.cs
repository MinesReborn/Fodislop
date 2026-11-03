using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kern.TerrainRasterTests;

internal static class Program
{
    private static readonly string[] Mutations =
    [
        "depth-face-without-bounds",
        "depth-reversed",
        "depth-without-quantization",
        "volume-normal-reversed",
        "double-quantize-fragments",
        "ao-flat-contact",
        "ao-to-black",
        "rim-merges-any-masses",
        "background-ignores-occlusion",
        "wall-variant-ignores-bottom",
        "node-edge-jitter-sign",
        "round-rim-follows-cell-sides",
        "world-anchor-skips-autotiles",
    ];

    public static int Main()
    {
        string root = FindProjectRoot();
        // HLSL round uses nearest-even; std::round rounds ties away from zero.
        string shim = Read(root, "tools/Kern.LightingTests/Native/Transport/Shims/NativeTransportShim.cpp")
            .Replace("std::round(a.x)", "std::nearbyint(a.x)", StringComparison.Ordinal)
            .Replace("std::round(a.y)", "std::nearbyint(a.y)", StringComparison.Ordinal);
        string format = Read(root, "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl");
        string animationProfile = Read(root, "Assets/Shaders/Terrain/Animation/TerrainAnimationProfile.hlsl")
            .Replace("#include \"Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl\"", string.Empty, StringComparison.Ordinal);
        string loader =
            Read(root, "Assets/Shaders/Terrain/Cells/Decoding/TerrainCellDecoding.hlsl") + "\n" +
            Read(root, "Assets/Shaders/Terrain/Autotiling/TerrainCellTiling.hlsl") + "\n" +
            Read(root, "Assets/Shaders/Terrain/Cells/Variation/TerrainCellVariation.hlsl") + "\n" +
            Read(root, "Assets/Shaders/Terrain/Cells/Geometry/TerrainCellGeometry.hlsl") + "\n" +
            Read(root, "Assets/Shaders/Terrain/Cells/Vertices/TerrainCellVertexLoader.hlsl");
        string terrainContour = Read(root, "Assets/Shaders/Terrain/Geometry/TerrainContour.hlsl");
        string terrainGeometryContract = Read(root, "Assets/Shaders/Terrain/Geometry/TerrainGeometryContract.hlsl");
        string screenCoverage = SliceBetween(terrainContour, "// The visible terrain", "float TerrainCellOccupancy(");
        string terrainGeometry = Read(root, "Assets/Shaders/Terrain/Geometry/TerrainGeometry.hlsl");
        string terrainDepth = Read(root, "Assets/Shaders/Terrain/Geometry/TerrainDepth.hlsl");
        string depthScenario = Read(root, "tools/Kern.TerrainRasterTests/Native/Terrain/depth.cpp");
        string roundCoverage = SliceBetween(terrainContour, "float EvaluateRoundableBlockAlpha(", "// The rim switch is bound");
        string terrainShader = ReadWithIncludes(root, "Assets/Shaders/Terrain/Rendering/Shader/Terrain.shader");
        string passCommon = Read(root, "Assets/Shaders/Terrain/Rendering/Passes/Common/TerrainPassCommon.hlsl");
        string volumeInput = Regex.Match(terrainShader, @"struct TerrainVolumeVertexInput\s*\{[^}]+\}").Value;
        if (volumeInput.Contains("TEXCOORD", StringComparison.Ordinal))
        {
            return Fail("Screen input must remain POSITION-only for every mesh and recorded vertex declaration");
        }
        if (passCommon.Contains("TEXCOORD", StringComparison.Ordinal) ||
            !terrainShader.Contains("Varyings vert (TerrainVolumeVertexInput input)", StringComparison.Ordinal))
        {
            return Fail("Physical vertex inputs must remain POSITION-only; edge attributes belong to the screen pass");
        }

        string scenario = Read(root, "tools/Kern.TerrainRasterTests/Native/Terrain/scenario.cpp");
        string aoShim = Read(root, "tools/Kern.TerrainRasterTests/Native/AmbientOcclusion/ao-field.cpp");
        string ao = Read(root, "Assets/Shaders/Terrain/Occlusion/TerrainAmbientOcclusion.hlsl");
        string terrainSampling = Read(root, "Assets/Shaders/Terrain/Textures/TerrainSampling.hlsl");
        string tileAddressing = Read(root, "Assets/Shaders/Terrain/Autotiling/TerrainTileAddressing.hlsl");
        string tileUvResult = SliceBetween(terrainSampling, "struct TerrainTileUvResult", "float TerrainUvCross(");
        string tileSampling = SliceBetween(terrainSampling, "void TerrainSetResolvedTileIdentity(", "// Ширина экранного пикселя");
        string lighting = Read(root, "Assets/Shaders/Terrain/Lighting/TerrainLightingData.hlsl");
        string terrainGeometryUv = SliceBetween(
                terrainSampling,
                "float TerrainUvCross(",
                "void TerrainSetResolvedTileIdentity(")
            .Replace("[unroll]", string.Empty, StringComparison.Ordinal);

        if (!terrainGeometryUv.Contains("TerrainResolveGeometryTileUV(", StringComparison.Ordinal))
        {
            return Fail("Production terrain geometry UV remap was not found");
        }

        if (!terrainShader.Contains("float2 projectedPoint = TerrainVolumeProjectPoint(", StringComparison.Ordinal) ||
            !terrainShader.Contains("volumeEdge.xy, volumeEdge.zw", StringComparison.Ordinal) ||
            !terrainShader.Contains("heightFraction = TerrainCornerBase(input.positionOS.w).y;", StringComparison.Ordinal) ||
            !terrainShader.Contains("input.positionOS.z = isDepth ? 2.0", StringComparison.Ordinal) ||
            !terrainShader.Contains("TerrainVolumeMaterialPoint(edgePoint, normal,", StringComparison.Ordinal) ||
            terrainShader.Contains("TerrainDepthSource(", StringComparison.Ordinal))
        {
            return Fail("Terrain volume must project actual side/face vertices, not reproject fragment copies");
        }

        if (terrainShader.Contains("TerrainRimRaw(", StringComparison.Ordinal) ||
            terrainShader.Contains("TerrainRim(surface", StringComparison.Ordinal))
        {
            return Fail("Terrain.shader must not apply the retired rim to visible albedo");
        }

        if (Regex.Matches(terrainShader, "BuildTerrainSurfaceInputs\\(").Count != 3)
        {
            return Fail("Expected exactly one surface parse in each terrain pass");
        }

        if (Regex.Matches(terrainSampling, "PixelArtSampleUV\\(").Count != 1 ||
            !terrainShader.Contains("SampleTerrainSurfaceTexel(", StringComparison.Ordinal) ||
            !Read(root, "Assets/Shaders/Terrain/Lighting/TerrainLightingFieldCommon.hlsl").Contains("SampleTerrainSurfaceTexel(", StringComparison.Ordinal))
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
        if (terrainShader[materialFieldPassAt..].Contains("input.isDepth", StringComparison.Ordinal) ||
            terrainShader[materialFieldPassAt..].Contains("input.materialCellSample", StringComparison.Ordinal))
        {
            return Fail("Lighting field passes cannot reference screen-volume varyings");
        }
        if (Regex.IsMatch(terrainDepth, @"\b(?:float[234]?|half[234]?|int[234]?)\s+(?:point|line|triangle|sample)\b"))
        {
            return Fail("Volume shader uses a reserved HLSL keyword as an identifier");
        }

        string ambientOcclusionPass = terrainShader[ambientOcclusionPassAt..];
        int ambientOcclusionFunctionAt = ambientOcclusionPass.IndexOf("half4 AmbientOcclusionFieldFrag", StringComparison.Ordinal);
        if (ambientOcclusionFunctionAt < 0)
        {
            return Fail("Terrain AO fragment entry point is missing");
        }

        int ambientOcclusionFunctionEndAt = ambientOcclusionPass.IndexOf("ENDHLSL", ambientOcclusionFunctionAt, StringComparison.Ordinal);
        if (ambientOcclusionFunctionEndAt < ambientOcclusionFunctionAt)
        {
            return Fail("Terrain AO fragment entry point is missing its HLSL block terminator");
        }

        string ambientOcclusionFunction = ambientOcclusionPass[ambientOcclusionFunctionAt..ambientOcclusionFunctionEndAt];
        if (materialFieldPass.Contains("TerrainGeometryCoverageForField", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("TerrainGeometrySignedDistance", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("if (exteriorDistance >= _TerrainAmbientOcclusionDistance)", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("_TerrainAmbientOcclusionDistance, exteriorDistance)", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("ColorMask R", StringComparison.Ordinal) ||
            !ambientOcclusionFunction.Contains("SampleTerrainLightingFieldAlbedoTexel", StringComparison.Ordinal) ||
            !ambientOcclusionFunction.Contains("clip(alpha - _AlphaCutoff)", StringComparison.Ordinal) ||
            ambientOcclusionFunction.Contains("_PrismaticFlowMap", StringComparison.Ordinal))
        {
            return Fail("AO must combine geometry with the material atlas occupancy threshold");
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

        string rim = SliceBetween(terrainContour, "// The rim switch is bound", "// The visible terrain");
        string contour = terrainContour[..terrainContour.IndexOf("float2 QuantizeTerrainFaceUV(", StringComparison.Ordinal)];
        const string quantizeUv = """
            float2 QuantizeTerrainFaceUV(float2 uv)
            {
                float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
                return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
            }
            """;
        const string extra = """
            float distance(float2 a, float2 b) { return length(a - b); }
            float lerp(float a, float b, float t) { return a + (b-a)*t; }
            float2 lerp(float2 a, float2 b, float2 t) { return a + (b-a)*t; }
            float4 round(float4 a) { return {std::nearbyint(a.x),std::nearbyint(a.y),std::nearbyint(a.z),std::nearbyint(a.w)}; }
            float4 make_float4(float2 a, float b, float c) { return {a.x,a.y,b,c}; }
            float4 make_float4(float a, float2 b, float c) { return {a,b.x,b.y,c}; }
            float _TestFwidth = 0.0f;
            float fwidth(float) { return _TestFwidth; }
            float sign(float value) { return (value > 0) - (value < 0); }
            using bool4 = int4;
            float4 frac(float4 a) { return {frac(a.x), frac(a.y), frac(a.z), frac(a.w)}; }
            float2 clamp(float2 a, float lo, float hi) { return {std::clamp(a.x,lo,hi),std::clamp(a.y,lo,hi)}; }
            float2 clamp(float2 a, float2 lo, float2 hi) { return {std::clamp(a.x,lo.x,hi.x),std::clamp(a.y,lo.y,hi.y)}; }
            using uint4 = unsigned __attribute__((ext_vector_type(4)));
            uint asuint(int value) { return (uint)value; }
            uint asuint(float value) { uint result; std::memcpy(&result, &value, 4); return result; }
            int min(int a, int b) { return a < b ? a : b; }
            uint min(uint a, uint b) { return a < b ? a : b; }
            int asint(uint value) { return (int)value; }
            bool any(int4 a) { return a.x || a.y || a.z || a.w; }
            float asfloat(uint value) { float result; std::memcpy(&result, &value, 4); return result; }
            long bufferReads = 0;
            template <typename T> struct ShimBuffer {
                std::vector<T> data;
                void reset(int count) { data.assign(count, T{}); }
                T operator[](int index) const {
                    ++bufferReads;
                    if (index < 0 || index >= (int)data.size())
                        throw std::runtime_error("out-of-bounds buffer read " + std::to_string(index));
                    return data[index];
                }
            };
            """;
        const string terrainUniforms = """
            float _TerrainDepthPixels = 16.0;
            float _RoundableCornerRadius = 0.51;
            float _RimQuantizationEnabled = 0.0;
            float _TerrainAmbientOcclusionDistance = 0.25;
            float _RimDistanceScale = 2.0;
            float _RimFalloff = 0.5;
            float _TerrainRimEnabled = 1.0;
            float4 _ShimAtlasTexelSize[8] = {};
            float4 TerrainMaterialAtlasTexelSize(int slot) { return _ShimAtlasTexelSize[slot]; }
            """;

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"kern-terrain-raster-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            // Миры стенда: клетки, строки типов и вершины FillQuad, с которыми
            // сверяется настоящий шейдер.
            string worldDirectory = Path.Combine(temporaryDirectory, "worlds");
            ProcessResult export = Run(
                "dotnet",
                ["test", Path.Combine(root, "tools/Kern.TerrainTests"), "--filter", "FullyQualifiedName~ExportWorldsForHlslShim"],
                root,
                ("KERN_TERRAIN_SHIM_FIXTURES", worldDirectory));
            if (export.ExitCode != 0 || !Directory.Exists(worldDirectory))
            {
                Console.Error.Write(export.StandardOutput);
                return Fail("Terrain stand did not export the shim worlds");
            }

            string cppPath = Path.Combine(temporaryDirectory, "test.cpp");
            string executablePath = Path.Combine(temporaryDirectory, "test");
            foreach (string? mutation in new string?[] { null }.Concat(Mutations))
            {
                string candidateLoader = loader;
                string candidateDepth = terrainDepth;
                string candidateTileSampling = tileSampling;
                if (mutation == "world-anchor-skips-autotiles")
                {
                    candidateTileSampling = candidateTileSampling.Replace(
                        "if (worldTextureAnchor && !isTiling)",
                        "if (worldTextureAnchor)",
                        StringComparison.Ordinal);
                    if (candidateTileSampling == tileSampling) return Fail("world-anchor-skips-autotiles mutation is stale");
                }
                if (mutation == "depth-face-without-bounds")
                {
                    candidateDepth = candidateDepth.Replace("surface.anchored = 1.0;", "surface.anchored = 0.0;", StringComparison.Ordinal);
                }
                if (mutation == "depth-reversed")
                {
                    candidateDepth = candidateDepth.Replace("return geometryPosition + TerrainDepthOffset", "return geometryPosition - TerrainDepthOffset", StringComparison.Ordinal);
                }
                if (mutation == "depth-without-quantization")
                {
                    candidateDepth = candidateDepth.Replace(
                        "round(fromCameraCells * pixels / KERN_TERRAIN_PRESENTATION_EYE_HEIGHT)",
                        "(fromCameraCells * pixels / KERN_TERRAIN_PRESENTATION_EYE_HEIGHT)", StringComparison.Ordinal);
                }
                if (mutation == "volume-normal-reversed")
                {
                    candidateDepth = candidateDepth.Replace("float2(edge.y, -edge.x)", "float2(-edge.y, edge.x)", StringComparison.Ordinal);
                }
                if (mutation is "depth-face-without-bounds" or "depth-reversed" or "depth-without-quantization" or "volume-normal-reversed" && candidateDepth == terrainDepth)
                {
                    return Fail($"{mutation} mutation is stale");
                }
                string candidateGeometry = terrainGeometry;
                string candidateAo = ao;
                string candidateRim = rim;

                if (mutation == "double-quantize-fragments")
                {
                    string before = "float2 samplePosition,\n    float4 cornersX,\n    float4 cornersY,\n    float anchored)\n{";
                    string after = before + "\n    samplePosition = (floor(samplePosition * 32.0) + 0.5) / 32.0;";
                    candidateGeometry = candidateGeometry.Replace(before, after, StringComparison.Ordinal);
                    if (candidateGeometry == terrainGeometry) return Fail("double-quantize-fragments mutation is stale");
                }

                if (mutation is "rim-merges-any-masses" or "background-ignores-occlusion" or "wall-variant-ignores-bottom"
                    or "node-edge-jitter-sign")
                {
                    (string before, string after) = mutation switch
                    {
                        "rim-merges-any-masses" => (
                            "return !TerrainTypeIsBackground(other) && otherMass != 0u && TerrainTypeReliefGroup(own) == otherMass;",
                            "return !TerrainTypeIsBackground(other) && otherMass != 0u;"),
                        "background-ignores-occlusion" => (
                            "        if (occluded && (!retainBackground || !exposedPresentationGround))\n        {\n            return v;\n        }",
                            "        if (false)\n        {\n            return v;\n        }"),
                        "wall-variant-ignores-bottom" => (
                            "bool hasBottom = (cornerSideMask & 8u) != 0u;",
                            "bool hasBottom = false;"),
                        "node-edge-jitter-sign" => (
                            "if (ctl && ctr) return int2(0, -ry);",
                            "if (ctl && ctr) return int2(0, ry);"),
                        _ => throw new InvalidOperationException(mutation),
                    };
                    candidateLoader = candidateLoader.Replace(before, after, StringComparison.Ordinal);
                    if (candidateLoader == loader) return Fail($"{mutation} mutation is stale");
                }

                if (mutation == "round-rim-follows-cell-sides")
                {
                    candidateRim = candidateRim.Replace(
                        "    if (KernTerrainIsRoundable(surface.packedContour))",
                        "    if (false)",
                        StringComparison.Ordinal);
                    if (candidateRim == rim) return Fail("round-rim-follows-cell-sides mutation is stale");
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
                    .Replace("Texture2D<float>", "AoTexture", StringComparison.Ordinal)
                    .Replace("SamplerState", "int", StringComparison.Ordinal);
                string source = shim + extra + aoShim + terrainUniforms + Translate(
                    terrainGeometryContract + candidateGeometry + format + animationProfile + candidateLoader + lighting +
                    contour + quantizeUv + roundCoverage + candidateRim + terrainGeometryUv +
                    tileAddressing + tileUvResult + candidateTileSampling + candidateAo + screenCoverage + candidateDepth) + depthScenario + scenario;
                File.WriteAllText(cppPath, source);

                ProcessResult compile = Run("clang++", ["-std=c++20", "-O2", "-ffp-contract=off", cppPath, "-o", executablePath], temporaryDirectory);
                Console.Out.Write(compile.StandardOutput);
                Console.Error.Write(compile.StandardError);
                if (compile.ExitCode != 0) return compile.ExitCode;

                ProcessResult result = Run(executablePath, [worldDirectory], temporaryDirectory);
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
            .Replace("[loop]", string.Empty, StringComparison.Ordinal)
            .Replace("inout TerrainTileUvResult tile", "TerrainTileUvResult& tile", StringComparison.Ordinal)
            .Replace("out float4 edgeEndpoints", "float4& edgeEndpoints", StringComparison.Ordinal)
            .Replace("out float2 sourceSample", "float2& sourceSample", StringComparison.Ordinal)
            .Replace("out float2 nearestPosition", "float2& nearestPosition", StringComparison.Ordinal)
            .Replace("Texture2D<float4>", "Texture", StringComparison.Ordinal)
            .Replace("StructuredBuffer<", "ShimBuffer<", StringComparison.Ordinal)
            .Replace("(TerrainCellVertex)0", "TerrainCellVertex{}", StringComparison.Ordinal);
        source = Regex.Replace(source, @"\(int2\)round\(([^)]+)\)", "make_int2(round($1))", RegexOptions.CultureInvariant);
        // HLSL vector casts convert components; a C++ vector cast reinterprets bits.
        source = Regex.Replace(source, @"\(float2\)(clamp\([^;\n]+\))", "make_float2($1)", RegexOptions.CultureInvariant);
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
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Shaders/Terrain/Rendering/Shader/Terrain.shader"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private static string ReadWithIncludes(string root, string relativePath)
    {
        string text = Read(root, relativePath);
        return Regex.Replace(text, @"#include\s+""([^""]+)""", m =>
        {
            string inc = m.Groups[1].Value;
            string incPath = Path.Combine(root, inc);
            return File.Exists(incPath) ? File.ReadAllText(incPath) : m.Value;
        });
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        params (string Name, string Value)[] environment)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach ((string name, string value) in environment) startInfo.Environment[name] = value;
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
