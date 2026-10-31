using System.Security.Cryptography;

namespace Kern.TerrainCrystalTests;

internal static class Program
{
    private const int ColorSampleCount = 12_000;
    private const int PixelPairCount = 16_384;
    private const string FlowMapSha256 = "1800d15643ec50e669ef483b053db98a651b18072e46872c2e47fb65f37a4756";

    private static readonly string s_root = FindProjectRoot();

    public static int Main()
    {
        try
        {
            VerifyFlowMapFixture();
            VerifyProductionSourceContract();
            VerifyColorEquation();
            VerifyPixelCenterQuantization();
            Console.WriteLine($"Crystal CPU regression checks passed: {ColorSampleCount:N0} color samples, " +
                $"{PixelPairCount:N0} subpixel pairs, flow-map SHA-256 verified.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine("Crystal CPU regression check failed: " + exception.Message);
            return 1;
        }
    }

    private static void VerifyFlowMapFixture()
    {
        string path = ProjectPath("Assets/Resources/World/Terrain/Animation/Flow/PrismaticFlowMap.bytes");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != 160 * 128 * 4)
        {
            throw new InvalidDataException($"Flow map has {bytes.Length} bytes; expected {160 * 128 * 4}.");
        }

        string actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!StringComparer.Ordinal.Equals(actualHash, FlowMapSha256))
        {
            throw new InvalidDataException($"Flow map hash changed: {actualHash}.");
        }
    }

    private static void VerifyProductionSourceContract()
    {
        string crystalPath = ProjectPath("Assets/Shaders/Terrain/Animation/Prismatic/TerrainPrismaticCrystal.hlsl");
        string crystal = File.ReadAllText(crystalPath);
        RequireContains(crystal, "float2(serverCell.x + localPosition.x, serverCell.y - localPosition.y)", crystalPath);
        RequireContains(crystal, "QuantizeTerrainPixelCenter(surfacePosition)", crystalPath);
        string geometryPath = ProjectPath("Assets/Shaders/Terrain/Geometry/TerrainGeometryContract.hlsl");
        RequireContains(File.ReadAllText(geometryPath),
            "floor(position * KERN_TERRAIN_FACE_GRID_SIZE) + 0.5", geometryPath);
        RequireContains(crystal, "float3(0.3, 0.59, 0.11)", crystalPath);
        RequireContains(crystal, "inverseLuma * inverseLuma * inverseLuma", crystalPath);
        RequireContains(crystal, "sin(-(hue * 6.283185 + phase))", crystalPath);
        RequireContains(crystal, "* chroma * body", crystalPath);
    }

    private static void VerifyColorEquation()
    {
        var random = new Random(0xC7A57A1);
        int wrongYMutationDetections = 0;
        int wrongLuminanceMutationDetections = 0;
        Vector3[] tints =
        [
            new(0.93, 0.15, 0.23),
            new(0.12, 0.82, 0.31),
            new(0.14, 0.36, 0.96),
            new(0.91, 0.72, 0.17),
            new(0.72, 0.19, 0.88),
        ];

        for (int index = 0; index < ColorSampleCount; index++)
        {
            var baseColor = new Vector3(Next(random), Next(random), Next(random));
            var flow = new Vector3(Next(random), Next(random), Next(random));
            double phase = (Next(random) * 2.0 - 1.0) * Math.PI;
            Vector3 tint = tints[index % tints.Length];

            Vector3 actual = ProductionEquation(baseColor, flow, tint, phase, wrongY: false, wrongLuminance: false);
            Vector3 expected = ReferenceEquation(baseColor, flow, tint, phase);
            AssertNear(expected, actual, 1e-12, $"color sample {index}");

            Vector3 wrongY = ProductionEquation(baseColor, flow, tint, phase, wrongY: true, wrongLuminance: false);
            Vector3 wrongLuminance = ProductionEquation(baseColor, flow, tint, phase, wrongY: false, wrongLuminance: true);
            if (Distance(expected, wrongY) > 1e-8) wrongYMutationDetections++;
            if (Distance(expected, wrongLuminance) > 1e-8) wrongLuminanceMutationDetections++;
        }

        if (wrongYMutationDetections == 0 || wrongLuminanceMutationDetections == 0)
        {
            throw new InvalidOperationException(
                $"Mutation sensitivity failed: wrong-Y={wrongYMutationDetections}, " +
                $"wrong-luminance={wrongLuminanceMutationDetections}.");
        }
    }

    private static void VerifyPixelCenterQuantization()
    {
        var random = new Random(0x51A7E);
        const double grid = 32.0;
        for (int index = 0; index < PixelPairCount; index++)
        {
            double cellX = random.Next(-10_000, 10_000);
            double cellY = random.Next(-10_000, 10_000);
            double subX1 = random.NextDouble() * 0.999;
            double subY1 = random.NextDouble() * 0.999;
            double subX2 = random.NextDouble() * 0.999;
            double subY2 = random.NextDouble() * 0.999;
            var first = PixelCenter(cellX + subX1 / grid, cellY + subY1 / grid);
            var second = PixelCenter(cellX + subX2 / grid, cellY + subY2 / grid);
            if (first != second)
            {
                throw new InvalidOperationException($"Subpixel pair {index} did not share a phase coordinate.");
            }

            var adjacent = PixelCenter(cellX + 1.0 / grid, cellY);
            if (Math.Abs((adjacent.X - first.X) * grid - 1.0) > 1e-9 ||
                Math.Abs((adjacent.Y - first.Y) * grid) > 1e-9)
            {
                throw new InvalidOperationException($"Adjacent-pixel phase step failed at sample {index}.");
            }
        }
    }

    // Production-equation mirror. Mutation switches model the two known regression classes.
    private static Vector3 ProductionEquation(Vector3 baseColor, Vector3 flow, Vector3 tint, double phase,
        bool wrongY, bool wrongLuminance)
    {
        double maximum = Math.Max(flow.X, Math.Max(flow.Y, flow.Z));
        double minimum = Math.Min(flow.X, Math.Min(flow.Y, flow.Z));
        double chroma = maximum - minimum;
        double hue = 0;
        if (chroma > 0)
        {
            if (maximum == flow.X) hue = (flow.Y - flow.Z) / chroma;
            else if (maximum == flow.Y) hue = 2 + (flow.Z - flow.X) / chroma;
            else hue = 4 + (flow.X - flow.Y) / chroma;
            hue = Fraction(hue / 6);
        }

        if (wrongY) hue = Fraction(1 - hue);
        double wave = (Math.Sin(-(hue * 2 * Math.PI + phase)) + 1) * 0.5;
        double body = wave * wave * wave;
        double luma = wrongLuminance
            ? 1 - (baseColor.X * 0.3 + baseColor.Y * 0.11 + baseColor.Z * 0.59)
            : 1 - (baseColor.X * 0.3 + baseColor.Y * 0.59 + baseColor.Z * 0.11);
        double multiplier = chroma * body * luma * luma * luma;
        return baseColor * (1 - body) + tint * multiplier;
    }

    // Independent reference: RGB→HSV hue via the sector formula, then the original source equation.
    private static Vector3 ReferenceEquation(Vector3 color, Vector3 sample, Vector3 tint, double phase)
    {
        double max = Math.Max(sample.X, Math.Max(sample.Y, sample.Z));
        double min = Math.Min(sample.X, Math.Min(sample.Y, sample.Z));
        double delta = max - min;
        double hueTurns = delta == 0 ? 0 : max == sample.X
            ? ((sample.Y - sample.Z) / delta) % 6 / 6
            : max == sample.Y
                ? ((sample.Z - sample.X) / delta + 2) / 6
                : ((sample.X - sample.Y) / delta + 4) / 6;
        hueTurns = hueTurns < 0 ? hueTurns + 1 : hueTurns;
        double saturation = max == 0 ? 0 : delta / max;
        double chroma = saturation * max;
        double wave = (Math.Sin(-(hueTurns * 2 * Math.PI + phase)) + 1) / 2;
        double strength = wave * wave * wave;
        double inverseLuminance = 1 - (0.3 * color.X + 0.59 * color.Y + 0.11 * color.Z);
        double tintWeight = chroma * strength * Math.Pow(inverseLuminance, 3);
        return color * (1 - strength) + tint * tintWeight;
    }

    private static Vector2 PixelCenter(double x, double y) => new(
        (Math.Floor(x * 32) + 0.5) / 32,
        (Math.Floor(y * 32) + 0.5) / 32);

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectSettings/ProjectVersion.txt")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Unity project root.");
    }

    private static string ProjectPath(string relativePath) => Path.Combine(s_root, relativePath);
    private static double Next(Random random) => random.NextDouble();
    private static double Fraction(double value) => value - Math.Floor(value);
    private static double Distance(Vector3 first, Vector3 second) => Math.Sqrt(
        Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2) + Math.Pow(first.Z - second.Z, 2));

    private static void AssertNear(Vector3 expected, Vector3 actual, double tolerance, string context)
    {
        if (Distance(expected, actual) > tolerance)
        {
            throw new InvalidOperationException($"{context}: expected {expected}, got {actual}.");
        }
    }

    private static void RequireContains(string source, string expected, string path)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Production contract is missing '{expected}' in {path}.");
        }
    }

    private readonly record struct Vector2(double X, double Y);
    private readonly record struct Vector3(double X, double Y, double Z)
    {
        public static Vector3 operator +(Vector3 first, Vector3 second) => new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
        public static Vector3 operator *(Vector3 value, double scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }
}
