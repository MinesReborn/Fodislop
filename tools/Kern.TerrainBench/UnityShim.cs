#nullable enable

// Управляемая подмена ровно тех типов UnityEngine, что нужны файлам игры в
// бенчмарке. Нативный UnityEngine.CoreModule вне движка падает на вызовах
// Mathf, а структуры здесь раскладкой совпадают с настоящими.
namespace UnityEngine
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector2(float x, float y) : IEquatable<Vector2>
    {
        public float x = x;
        public float y = y;

        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);

        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);

        public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);

        public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

        public float sqrMagnitude => (x * x) + (y * y);

        public bool Equals(Vector2 other) => x == other.x && y == other.y;

        public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector2Int(int x, int y) : IEquatable<Vector2Int>
    {
        public int x = x;
        public int y = y;

        public static Vector2Int zero => default;

        public static Vector2Int operator +(Vector2Int a, Vector2Int b) =>
            new(a.x + b.x, a.y + b.y);

        public static Vector2Int operator -(Vector2Int a, Vector2Int b) =>
            new(a.x - b.x, a.y - b.y);

        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);

        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);

        public bool Equals(Vector2Int other) => x == other.x && y == other.y;

        public override bool Equals(object? obj) => obj is Vector2Int other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3(float x, float y, float z) : IEquatable<Vector3>
    {
        public float x = x;
        public float y = y;
        public float z = z;

        public static Vector3 zero => default;

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d);

        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);

        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;

        public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);

        public override string ToString() => $"({x:F5}, {y:F5}, {z:F5})";

        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector4(float x, float y, float z, float w) : IEquatable<Vector4>
    {
        public float x = x;
        public float y = y;
        public float z = z;
        public float w = w;

        public static Vector4 zero => default;

        public static bool operator ==(Vector4 a, Vector4 b) => a.Equals(b);

        public static bool operator !=(Vector4 a, Vector4 b) => !a.Equals(b);

        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";

        public bool Equals(Vector4 other) => x == other.x && y == other.y && z == other.z && w == other.w;

        public override bool Equals(object? obj) => obj is Vector4 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Color(float r, float g, float b, float a)
    {
        public float r = r;
        public float g = g;
        public float b = b;
        public float a = a;

        public static Color white => new(1, 1, 1, 1);

        public static implicit operator Color32(Color c) =>
            new((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));

        public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Color32(byte r, byte g, byte b, byte a) : IEquatable<Color32>
    {
        public byte r = r;
        public byte g = g;
        public byte b = b;
        public byte a = a;

        public bool Equals(Color32 other) => r == other.r && g == other.g && b == other.b && a == other.a;

        public override bool Equals(object? obj) => obj is Color32 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RectInt(int x, int y, int width, int height)
    {
        public int x = x;
        public int y = y;
        public int width = width;
        public int height = height;

        public int xMin => x;

        public int yMin => y;

        public int xMax => x + width;

        public int yMax => y + height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect(float x, float y, float width, float height)
    {
        public float x = x;
        public float y = y;
        public float width = width;
        public float height = height;

        public float xMin => x;

        public float yMin => y;

        public float xMax => x + width;

        public float yMax => y + height;

        public bool Contains(Vector2 point) =>
            point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax;
    }

    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);

        public static int Min(int a, int b) => Math.Min(a, b);

        public static float Max(float a, float b) => Math.Max(a, b);

        public static float Min(float a, float b) => Math.Min(a, b);

        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

        public static int Abs(int value) => Math.Abs(value);

        public static float Abs(float value) => Math.Abs(value);

        public static float Round(float value) => MathF.Round(value);

        public static int RoundToInt(float value) => (int)MathF.Round(value);

        public static int FloorToInt(float value) => (int)MathF.Floor(value);

        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);

        public static float HalfToFloat(ushort half) => (float)BitConverter.UInt16BitsToHalf(half);
    }
}
