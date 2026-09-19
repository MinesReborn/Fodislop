#nullable enable

namespace Kern.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class WorldChunkRleCodec
{
    public static void EncodeChunk<T>(BinaryWriter writer, T[] chunk, int chunkArea)
        where T : unmanaged
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (chunk == null)
        {
            throw new ArgumentNullException(nameof(chunk));
        }

        if (chunkArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkArea), "Chunk area must be positive.");
        }

        // EqualityComparer<T>.Default resolves to a specialized non-boxing
        // implementation for unmanaged types, avoiding ValueType.Equals boxing.
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        int ptr = 0;
        while (ptr < chunkArea)
        {
            T current = chunk[ptr];
            ushort count = 1;
            ptr++;
            while (ptr < chunkArea && count < ushort.MaxValue && comparer.Equals(chunk[ptr], current))
            {
                count++;
                ptr++;
            }

            writer.Write(count);
            WriteT(writer, current);
        }
    }

    public static T[] DecodeChunk<T>(BinaryReader reader, int chunkArea)
        where T : unmanaged
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (chunkArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkArea), "Chunk area must be positive.");
        }

        T[] chunk = new T[chunkArea];
        int ptr = 0;
        try
        {
            while (ptr < chunkArea)
            {
                ushort count = reader.ReadUInt16();
                T value = ReadT<T>(reader);
                if (count == 0)
                {
                    break;
                }

                int fill = Math.Min(count, chunkArea - ptr);
                chunk.AsSpan(ptr, fill).Fill(value);
                ptr += fill;
                if (fill < count)
                {
                    break;
                }
            }
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException(
                $"World layer chunk ended before {chunkArea} cells were decoded.");
        }

        if (ptr != chunkArea)
        {
            throw new InvalidDataException(
                $"World layer chunk contains {ptr} cells; expected {chunkArea}.");
        }

        return chunk;
    }

    private static void WriteT<T>(BinaryWriter writer, T value)
        where T : unmanaged
    {
        Span<T> span = stackalloc T[1];
        span[0] = value;
        writer.Write(MemoryMarshal.AsBytes(span));
    }

    private static T ReadT<T>(BinaryReader reader)
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        // ReadBytes allocates a fresh array per call, and this runs once per
        // RLE run: a varied chunk decoded hundreds of arrays.
        Span<byte> bytes = stackalloc byte[size];
        int received = 0;
        while (received < size)
        {
            int read = reader.Read(bytes[received..]);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"Expected {size} bytes for a world-layer value, received {received}.");
            }

            received += read;
        }

        return MemoryMarshal.Read<T>(bytes);
    }
}
