#nullable enable

using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Abs(float value) => MathF.Abs(value);
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static int CeilToInt(float value) => checked((int)MathF.Ceiling(value));
        public static int Min(int left, int right) => Math.Min(left, right);
        public static float Min(float left, float right) => MathF.Min(left, right);
        public static int Max(int left, int right) => Math.Max(left, right);
        public static float Max(float left, float right) => MathF.Max(left, right);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2Int one => new(1, 1);
        public static Vector2Int zero => new(0, 0);
        public static Vector2Int operator -(Vector2Int left, Vector2Int right) =>
            new(left.x - right.x, left.y - right.y);
        public static bool operator ==(Vector2Int left, Vector2Int right) => left.Equals(right);
        public static bool operator !=(Vector2Int left, Vector2Int right) => !left.Equals(right);
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object? obj) => obj is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
    }
    public readonly record struct Vector4(float x, float y, float z, float w);
    public readonly record struct Color(float r, float g, float b, float a = 1f)
    {
        public static Color white => new(1f, 1f, 1f, 1f);
        public static Color operator *(Color value, float multiplier) =>
            new(value.r * multiplier, value.g * multiplier, value.b * multiplier, value.a * multiplier);
    }
    public sealed class ComputeShader { }
    public sealed class ComputeBuffer { }
    public sealed class RenderTexture { }

    public static class Shader
    {
        public static int PropertyToID(string name) => StringComparer.Ordinal.GetHashCode(name);
    }

    public static class SystemInfo
    {
        public static bool graphicsUVStartsAtTop => false;
    }
}

namespace UnityEngine.Rendering
{
    public sealed class CommandBuffer
    {
        public Dictionary<int, UnityEngine.Color> Colors { get; } = [];

        public void SetComputeVectorParam(UnityEngine.ComputeShader _, int id, UnityEngine.Color value) => Colors[id] = value;
        public void SetComputeVectorParam(params object[] _) { }
        public void SetComputeTextureParam(params object[] _) { }
        public void SetComputeBufferParam(params object[] _) { }
        public void SetComputeFloatParam(params object[] _) { }
        public void SetComputeIntParams(params object[] _) { }
        public void SetComputeIntParam(params object[] _) { }
    }
}

namespace Kern.Core
{
    public readonly struct GraphicsQualitySettings;
}

namespace Kern.Rendering
{
}

namespace Kern.World.Terrain
{
    public static class TerrainLook
    {
        public const float AmbientOcclusionMip = 1.5f;
        public const float AmbientOcclusionStrength = 1f;
    }
}

namespace Kern.World.Lighting.Quality
{
    public enum LightingQualityMode
    {
        PerBlock = 0,
        Off = 1,
        PerPixel = 2,
        PerPixelBilinearFix = 3,
        PerPixelBilinearFixBounce = 4,
    }
}

namespace Kern.World.Lighting
{
    [Flags]
    public enum LightingFeatureFlags
    {
        None = 0,
        StaticRC = 1 << 0,
        DynamicLights = 1 << 1,
        DiffuseBounce = 1 << 2,
        VisibilityAwareMerge = 1 << 3,
        WallAwareUpsample = 1 << 4,
    }

    public static class LightingConfigHolder
    {
        public static LightingFeatureFlags EnabledFeatures { get; set; } = LightingFeatureFlags.StaticRC;
        public const float AmbientIntensity = 0f;
        public const float EmissionScale = 16f;
        public const float BounceStrength = 1f;
        public const float MaximumLightMultiplier = 1f;
        public const float EmptyExtinctionMultiplier = 0.2f;
        public const float SolidExtinctionMultiplier = 1f;
        public static bool BounceEnabled => true;
        public static UnityEngine.Color AmbientColor => UnityEngine.Color.white;
        public static UnityEngine.Color EmptyExtinctionRGB => UnityEngine.Color.white;
        public static UnityEngine.Color SolidExtinctionRGB => UnityEngine.Color.white;
    }

    public sealed class LightingEngine
    {
        public enum DebugView
        {
            FinalLighting,
        }
    }
}
