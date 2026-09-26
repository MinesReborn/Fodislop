#nullable enable

using System;

namespace Kern.World.Terrain;

/// <summary>
/// Fixed-size logical grid whose origin moves by changing two indices.
/// Scrolling therefore costs O(1); only the newly exposed band is filled by
/// the caller.
/// </summary>
public sealed class TerrainRingGrid<T>
{
    private T[] _data = Array.Empty<T>();
    private int _offsetX;
    private int _offsetY;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsAllocated => _data.Length != 0;

    public int GetLength(int dimension) => dimension switch
    {
        0 => Width,
        1 => Height,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    public ref T this[int x, int y]
    {
        get => ref _data[PhysicalIndex(x, y)];
    }

    public void EnsureSize(int width, int height)
    {
        if (Width == width && Height == height && _data.Length != 0)
        {
            return;
        }

        Width = width;
        Height = height;
        _offsetX = 0;
        _offsetY = 0;
        _data = new T[checked(width * height)];
    }

    public void Clear()
    {
        Array.Clear(_data, 0, _data.Length);
    }

    public void Scroll(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        if (Math.Abs(dx) >= Width || Math.Abs(dy) >= Height)
        {
            Clear();
            _offsetX = 0;
            _offsetY = 0;
            return;
        }

        _offsetX = PositiveModulo(_offsetX + dx, Width);
        _offsetY = PositiveModulo(_offsetY + dy, Height);
    }

    public T[,] Clone()
    {
        var clone = new T[Width, Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                clone[x, y] = this[x, y];
            }
        }

        return clone;
    }

    private int PhysicalIndex(int x, int y)
    {
        return WrapCoordinate(x, _offsetX, Width) * Height +
            WrapCoordinate(y, _offsetY, Height);
    }

    private static int WrapCoordinate(int coordinate, int offset, int length)
    {
        // Logical grid accesses are usually in range, so the ring offset needs
        // at most one wrap. Keep the modulo fallback for arbitrary indices.
        int value = coordinate + offset;
        if ((uint)value < (uint)length)
        {
            return value;
        }

        if (value >= length && value - length < length)
        {
            return value - length;
        }

        if (value < 0 && value >= -length)
        {
            return value + length;
        }

        return PositiveModulo(value, length);
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
