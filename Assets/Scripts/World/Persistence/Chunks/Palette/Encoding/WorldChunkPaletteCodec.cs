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

internal static class WorldChunkPaletteCodec
{

    internal static int EncodeBytePalette(ReadOnlySpan<byte> values, Span<byte> destination)
    {
        Span<short> indexByValue = stackalloc short[256];
        indexByValue.Fill(-1);
        Span<byte> palette = stackalloc byte[256];
        int paletteCount = 0;
        foreach (byte value in values)
        {
            if (indexByValue[value] < 0)
            {
                indexByValue[value] = (short)paletteCount;
                palette[paletteCount++] = value;
            }
        }

        int bitsPerIndex = WorldChunkPaletteCodec.BitsRequired(paletteCount - 1);
        int packedLength = checked((int)(((long)values.Length * bitsPerIndex + 7) / 8));
        int encodedLength = 4 + paletteCount + packedLength;
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)paletteCount);
        destination[2] = (byte)bitsPerIndex;
        destination[3] = 0;
        palette[..paletteCount].CopyTo(destination[4..]);
        Span<byte> packed = destination.Slice(4 + paletteCount, packedLength);
        packed.Clear();
        int bitPosition = 0;
        foreach (byte value in values)
        {
            int paletteIndex = indexByValue[value];
            for (int bit = 0; bit < bitsPerIndex; bit++, bitPosition++)
            {
                if ((paletteIndex & (1 << bit)) != 0)
                {
                    packed[bitPosition >> 3] |= (byte)(1 << (bitPosition & 7));
                }
            }
        }

        return encodedLength;
    }

    internal static void ValidatePalette(ReadOnlySpan<byte> payload, int chunkArea)
    {
        if (payload.Length < 4)
        {
            throw new InvalidDataException("Byte-palette chunk header is truncated.");
        }

        int paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int bits = payload[2];
        int expectedBits = WorldChunkPaletteCodec.BitsRequired(paletteCount - 1);
        int packedLength = checked((int)(((long)chunkArea * bits + 7) / 8));
        if (paletteCount is < 1 or > 256 || bits != expectedBits || payload[3] != 0 ||
            payload.Length != 4 + paletteCount + packedLength)
        {
            throw new InvalidDataException("Byte-palette chunk has invalid framing.");
        }

        ReadOnlySpan<byte> packed = payload[(4 + paletteCount)..];
        for (int cell = 0, bitPosition = 0; cell < chunkArea; cell++, bitPosition += bits)
        {
            if (WorldChunkPaletteCodec.ReadBits(packed, bitPosition, bits) >= paletteCount)
            {
                throw new InvalidDataException("Byte-palette chunk contains an invalid palette index.");
            }
        }
    }

    internal static void DecodePalette(ReadOnlySpan<byte> payload, Span<byte> output)
    {
        int paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int bits = payload[2];
        ReadOnlySpan<byte> palette = payload.Slice(4, paletteCount);
        ReadOnlySpan<byte> packed = payload[(4 + paletteCount)..];
        for (int cell = 0, bitPosition = 0; cell < output.Length; cell++, bitPosition += bits)
        {
            output[cell] = palette[WorldChunkPaletteCodec.ReadBits(packed, bitPosition, bits)];
        }
    }

    internal static void VisitPalette<T>(ReadOnlySpan<byte> payload, int chunkArea, int chunkIndex, Action<int, T, int> visitor)
        where T : unmanaged
    {
        if (Unsafe.SizeOf<T>() != 1)
        {
            throw new InvalidDataException("Byte-palette codec requires one-byte values.");
        }

        int paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int bits = payload[2];
        ReadOnlySpan<byte> palette = payload.Slice(4, paletteCount);
        ReadOnlySpan<byte> packed = payload[(4 + paletteCount)..];
        int current = palette[WorldChunkPaletteCodec.ReadBits(packed, 0, bits)];
        int runLength = 1;
        for (int cell = 1, bitPosition = bits; cell < chunkArea; cell++, bitPosition += bits)
        {
            int value = palette[WorldChunkPaletteCodec.ReadBits(packed, bitPosition, bits)];
            if (value == current)
            {
                runLength++;
            }
            else
            {
                WorldChunkV2Codec.EmitByteRun<T>(visitor, chunkIndex, (byte)current, runLength);
                current = value;
                runLength = 1;
            }
        }

        WorldChunkV2Codec.EmitByteRun<T>(visitor, chunkIndex, (byte)current, runLength);
    }

    internal static int ReadBits(ReadOnlySpan<byte> source, int bitPosition, int bitCount)
    {
        if (bitCount == 0)
        {
            return 0;
        }

        int byteIndex = bitPosition >> 3;
        int bitOffset = bitPosition & 7;
        uint window = source[byteIndex];
        if (byteIndex + 1 < source.Length)
        {
            window |= (uint)source[byteIndex + 1] << 8;
        }

        return (int)((window >> bitOffset) & ((1u << bitCount) - 1));
    }

    internal static int BitsRequired(int value)
    {
        int bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }

        return bits;
    }

}
