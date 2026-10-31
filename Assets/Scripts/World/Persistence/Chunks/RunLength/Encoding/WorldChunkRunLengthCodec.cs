#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;

namespace Kern.Persistence;

internal static class WorldChunkRunLengthCodec
{

    internal static int EncodeRLE<T>(ReadOnlySpan<T> values, Span<byte> destination)
        where T : unmanaged
    {
        var comparer = EqualityComparer<T>.Default;
        int elementSize = Unsafe.SizeOf<T>();
        int output = 0;
        int index = 0;
        Span<T> valueStorage = stackalloc T[1];
        while (index < values.Length)
        {
            T value = values[index];
            int count = 1;
            while (index + count < values.Length && count < ushort.MaxValue && comparer.Equals(values[index + count], value))
            {
                count++;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(destination[output..], (ushort)count);
            output += sizeof(ushort);
            valueStorage[0] = value;
            MemoryMarshal.AsBytes(valueStorage).CopyTo(destination[output..]);
            output += elementSize;
            index += count;
        }

        return output;
    }

    internal static void DecodeRLE<T>(ReadOnlySpan<byte> payload, Span<T> destination)
        where T : unmanaged
    {
        int elementSize = Unsafe.SizeOf<T>();
        int offset = 0;
        int cell = 0;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < sizeof(ushort) + elementSize)
            {
                throw new InvalidDataException("RLE world chunk has a truncated run.");
            }

            ushort count = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);
            if (count == 0 || count > destination.Length - cell)
            {
                throw new InvalidDataException("RLE world chunk contains an invalid run length.");
            }

            T value = MemoryMarshal.Read<T>(payload.Slice(offset, elementSize));
            destination.Slice(cell, count).Fill(value);
            cell += count;
            offset += elementSize;
        }

        if (cell != destination.Length)
        {
            throw new InvalidDataException("RLE world chunk has an unexpected cell count.");
        }
    }

    internal static void ValidateRLE<T>(ReadOnlySpan<byte> payload, int chunkArea) where T : unmanaged
    {
        int elementSize = Unsafe.SizeOf<T>();
        int offset = 0;
        int cells = 0;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < sizeof(ushort) + elementSize)
            {
                throw new InvalidDataException("RLE world chunk has a truncated run.");
            }

            ushort count = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort) + elementSize;
            if (count == 0 || count > chunkArea - cells)
            {
                throw new InvalidDataException("RLE world chunk contains an invalid run length.");
            }

            cells += count;
        }

        if (cells != chunkArea)
        {
            throw new InvalidDataException("RLE world chunk has an unexpected cell count.");
        }
    }

    internal static void VisitRLE<T>(ReadOnlySpan<byte> payload, int chunkIndex, Action<int, T, int> visitor)
        where T : unmanaged
    {
        int elementSize = Unsafe.SizeOf<T>();
        int offset = 0;
        while (offset < payload.Length)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);
            T value = MemoryMarshal.Read<T>(payload.Slice(offset, elementSize));
            WorldChunkV2Codec.InvokeVisitor(visitor, chunkIndex, value, count);
            offset += elementSize;
        }
    }

}
