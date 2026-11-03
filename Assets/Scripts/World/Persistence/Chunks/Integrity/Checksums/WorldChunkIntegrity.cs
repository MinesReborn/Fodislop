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

internal static class WorldChunkIntegrity
{
    internal const uint Crc32CPolynomial = 0x82F63B78;
    internal static readonly uint[] s_crc32CTable = WorldChunkIntegrity.CreateCrc32CTable();

    internal static void VerifyChecksum(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, int? chunkIndex)
    {
        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        uint crc = WorldChunkIntegrity.UpdateCrc32C(uint.MaxValue, header[..16]);
        bool isChunkIndexBound = (header[5] & WorldChunkV2Codec.ChunkIndexBoundFlag) != 0;
        if (chunkIndex.HasValue && !isChunkIndexBound)
        {
            throw new InvalidDataException("Current world chunk frame is not bound to its chunk index.");
        }

        if (isChunkIndexBound)
        {
            if (!chunkIndex.HasValue)
            {
                throw new InvalidDataException("World chunk checksum requires its chunk index.");
            }

            Span<byte> chunkIndexBytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(chunkIndexBytes, chunkIndex.Value);
            crc = WorldChunkIntegrity.UpdateCrc32C(crc, chunkIndexBytes);
        }

        crc = WorldChunkIntegrity.UpdateCrc32C(crc, payload);
        if (~crc != expected)
        {
            throw new InvalidDataException("World chunk CRC32C mismatch.");
        }
    }

    internal static uint UpdateCrc32C(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc = (crc >> 8) ^ WorldChunkIntegrity.s_crc32CTable[(int)((crc ^ value) & 0xff)];
        }

        return crc;
    }

    internal static uint[] CreateCrc32CTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : WorldChunkIntegrity.Crc32CPolynomial);
            }

            table[(int)index] = value;
        }

        return table;
    }

}
