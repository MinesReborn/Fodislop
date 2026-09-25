#nullable enable

namespace UnityEngine
{
    public class Camera;

    public class RenderTexture;

    public readonly struct Vector4(float x, float y, float z, float w)
    {
        public float x { get; } = x;
        public float y { get; } = y;
        public float z { get; } = z;
        public float w { get; } = w;
    }

    public readonly struct Vector2(float x, float y)
    {
        public float x { get; } = x;
        public float y { get; } = y;

        public static float Distance(Vector2 left, Vector2 right)
        {
            float deltaX = left.x - right.x;
            float deltaY = left.y - right.y;
            return System.MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }
    }

    public readonly struct Vector2Int(int x, int y)
    {
        public int x { get; } = x;
        public int y { get; } = y;
    }

    public static class Mathf
    {
        public static int RoundToInt(float value) => (int)System.MathF.Round(value);

        public static int Max(int left, int right) => System.Math.Max(left, right);
    }

    public readonly struct RectInt(int x, int y, int width, int height)
    {
        public int x { get; } = x;
        public int y { get; } = y;
        public int width { get; } = width;
        public int height { get; } = height;
        public int xMin => x;
        public int yMin => y;
        public int xMax => x + width;
        public int yMax => y + height;
    }
}

namespace UnityEngine.Rendering
{
    public class CommandBuffer;
}
