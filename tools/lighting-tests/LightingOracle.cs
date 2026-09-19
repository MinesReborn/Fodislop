#nullable enable

namespace Kern.LightingTests;

internal static class LightingOracle
{
    private const int Size = 32;

    public static int Run()
    {
        RunSingleTorch();
        RunDiagonalOcclusion();
        RunMultipleEmitters();
        RunCorridor();
        RunLightNearWall();
        Console.WriteLine("5 CPU golden lighting scenarios passed.");
        return 0;
    }

    private static void RunSingleTorch()
    {
        float[,] material = new float[Size, Size];
        float[,] emission = new float[Size, Size];
        for (int y = 15; y < 17; y++)
        {
            for (int x = 15; x < 17; x++)
            {
                emission[y, x] = 16f;
            }
        }

        float[,] radiance = Trace(material, emission);
        Check(radiance[16, 16] > 10f, "center torch is bright");
        Check(MathF.Abs(radiance[5, 5] - radiance[5, 26]) < 1e-3f, "horizontal symmetry");
        Check(MathF.Abs(radiance[5, 5] - radiance[26, 5]) < 1e-3f, "vertical symmetry");
        Check(radiance[16, 16] > radiance[16, 20] &&
            radiance[16, 20] > radiance[16, 26] &&
            radiance[16, 26] > radiance[16, 31], "monotonic falloff");
    }

    private static void RunDiagonalOcclusion()
    {
        float[,] material = new float[Size, Size];
        float[,] emission = new float[Size, Size];
        material[10, 10] = 1f;
        material[11, 11] = 1f;
        emission[9, 9] = 16f;
        float[,] radiance = Trace(material, emission);
        Check(radiance[12, 12] < radiance[9, 9] * .05f, "diagonal contact occlusion");
    }

    private static void RunMultipleEmitters()
    {
        float[,] material = new float[Size, Size];
        float[,] emission = new float[Size, Size];
        emission[6, 6] = 8f;
        emission[6, 25] = 8f;
        emission[25, 6] = 8f;
        emission[25, 25] = 8f;
        float[,] radiance = Trace(material, emission);
        Check(radiance[16, 16] > 0f, "multiple emitters reach center");
        Check(MathF.Abs(radiance[16, 10] - radiance[16, 21]) < 1e-3f, "multiple emitter symmetry");
    }

    private static void RunCorridor()
    {
        float[,] material = new float[Size, Size];
        float[,] emission = new float[Size, Size];
        for (int y = 0; y < Size; y++)
        {
            material[y, 16] = 1f;
        }

        material[15, 16] = 0f;
        material[16, 16] = 0f;
        emission[15, 5] = 16f;
        emission[16, 5] = 16f;
        float[,] radiance = Trace(material, emission);
        Check(radiance[16, 24] > radiance[5, 24] * 10f, "corridor aperture");
        Check(radiance[5, 24] < .01f, "corridor shadow");
    }

    private static void RunLightNearWall()
    {
        float[,] material = new float[Size, Size];
        float[,] emission = new float[Size, Size];
        for (int y = 14; y < 18; y++)
        {
            for (int x = 14; x < 18; x++)
            {
                material[y, x] = 1f;
            }
        }

        emission[16, 11] = 16f;
        float[,] radiance = Trace(material, emission);
        for (int y = 14; y < 18; y++)
        {
            for (int x = 14; x < 18; x++)
            {
                Check(radiance[y, x] == 0f, "solid wall receives no light");
            }
        }

        Check(radiance[16, 20] < .01f, "dynamic light shadow behind wall");
    }

    private static float[,] Trace(float[,] material, float[,] emission)
    {
        var result = new float[Size, Size];
        const int rays = 180;
        const float step = .2f;
        const float extinction = .2f;
        float angleStep = 2f * MathF.PI / rays;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (material[y, x] >= .5f)
                {
                    continue;
                }

                float total = 0f;
                for (int ray = 0; ray < rays; ray++)
                {
                    float angle = ray * angleStep;
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);
                    float transmittance = 1f;
                    float distance = 0f;
                    while (distance < Size)
                    {
                        distance += step;
                        int sampleX = (int)(x + .5f + cos * distance);
                        int sampleY = (int)(y + .5f + sin * distance);
                        if ((uint)sampleX >= Size || (uint)sampleY >= Size ||
                            material[sampleY, sampleX] >= .5f)
                        {
                            break;
                        }

                        if (emission[sampleY, sampleX] > 0f)
                        {
                            total += emission[sampleY, sampleX] * transmittance;
                            transmittance *= .5f;
                        }

                        transmittance *= MathF.Exp(-extinction * step);
                        if (transmittance < 1e-4f)
                        {
                            break;
                        }
                    }
                }

                result[y, x] = total / rays;
            }
        }

        return result;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Lighting oracle failed: {name}");
        }
    }
}
