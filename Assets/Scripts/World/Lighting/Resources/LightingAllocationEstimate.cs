#nullable enable

using System;

namespace Kern.World.Lighting;

// Payload of a complete proposed generation, before releasing existing resources.
// Driver overhead and retained old generations are accounted for by MemoryAllocationGuard.
internal static class LightingAllocationEstimate
{
    public static long TextureBytes(int width, int height, int layers, int bytesPerPixel)
    {
        if (width <= 0 || height <= 0 || layers <= 0 || bytesPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Allocation dimensions must be positive.");
        }
        return checked((long)width * height * layers * bytesPerPixel);
    }

    public static long FieldBytes(int width, int height, int lightWidth, int lightHeight, int aoWidth, int aoHeight,
        int gridWidth, int gridHeight, long cascadeEntries, int maximumLights) => checked(
            TextureBytes(width, height, 1, 12) + // Material32 + emission RGBHalf.
            TextureBytes(lightWidth, lightHeight, 1, 32) + // Four RGBHalf receiver textures.
            TextureBytes(aoWidth, aoHeight, 1, 4) +
            TextureBytes(gridWidth, gridHeight, 1, 8) +
            cascadeEntries * 16 + // Packed RGB radiance + changed mask.
            (long)maximumLights * 32 + 24); // Source buffer, two counter buffers.
}
