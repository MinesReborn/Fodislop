#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace Fodinae.World;

internal sealed class GifStreamReader
{
    private readonly byte[] _data;
    private int _pos;

    public int Position
    {
        get => _pos;
        set => _pos = value;
    }

    public int Length => _data.Length;

    public GifStreamReader(byte[] data)
    {
        _data = data;
    }

    public Color32[] ReadColorTable(int size)
    {
        if (size < 2 || size > 256)
        {
            throw new InvalidDataException(
                $"GIF color table has invalid size {size}.");
        }

        EnsureAvailable(checked(size * 3));
        var table = new Color32[size];
        for (int i = 0; i < size; i++)
        {
            table[i] = new Color32(
                ReadByte(),
                ReadByte(),
                ReadByte(),
                255);
        }

        return table;
    }

    public void SkipDataSubBlocks()
    {
        while (true)
        {
            int size = ReadByte();
            if (size == 0)
            {
                return;
            }

            EnsureAvailable(size);
            _pos += size;
        }
    }

    public byte[] ReadDataSubBlocks()
    {
        using var stream = new MemoryStream();
        while (true)
        {
            int size = ReadByte();
            if (size == 0)
            {
                return stream.ToArray();
            }

            EnsureAvailable(size);
            stream.Write(_data, _pos, size);
            _pos += size;
        }
    }

    public int ReadUInt16()
    {
        int low = ReadByte();
        int high = ReadByte();
        return low | (high << 8);
    }

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _data[_pos++];
    }

    public void EnsureAvailable(int byteCount)
    {
        if (byteCount < 0 || _pos < 0 || _pos > _data.Length - byteCount)
        {
            throw new InvalidDataException(
                $"GIF stream is truncated at byte {_pos}; " +
                $"{byteCount} more byte(s) were required.");
        }
    }
}
