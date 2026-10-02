#nullable enable

namespace Kern.Persistence;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;

/// <summary>Version 2 framed chunk codec: raw, RLE, LZ4 block, and byte palette.</summary>
internal static class WorldChunkV2Codec
{
    internal const int FrameHeaderSize = 20;
    private const uint Crc32CPolynomial = 0x82F63B78;
    private const uint SchemaHashSeed = 2166136261;
    private static readonly uint[] s_crc32CTable = CreateCrc32CTable();

    private enum Codec : byte
    {
        Raw = 0,
        RLE = 1,
        LZ4 = 2,
        BytePalette = 3,
    }

    internal static uint SchemaId<T>() where T : unmanaged
    {
        return SchemaIdCache<T>.Value;
    }

    internal static void EncodeChunk<T>(BinaryWriter writer, T[] chunk, int chunkArea)
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
        ValidateChunk(chunk.Length, chunkArea);

        int elementSize = Unsafe.SizeOf<T>();
        int rawLength = checked(chunkArea * elementSize);
        int rleCapacity = checked(chunkArea * (sizeof(ushort) + elementSize));
        byte[] rleBuffer = ArrayPool<byte>.Shared.Rent(rleCapacity);
        byte[] lz4Buffer = ArrayPool<byte>.Shared.Rent(LZ4Codec.MaximumOutputSize(rawLength));
        byte[]? paletteBuffer = elementSize == 1
            ? ArrayPool<byte>.Shared.Rent(checked(chunkArea + 260))
            : null;

        try
        {
            ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(chunk.AsSpan(0, chunkArea));

            Codec selectedCodec = Codec.Raw;
            ReadOnlySpan<byte> selectedPayload = raw;
            int selectedLength = rawLength;

            int rleLength = EncodeRLE<T>(chunk.AsSpan(0, chunkArea), rleBuffer);
            if (rleLength < selectedLength)
            {
                selectedCodec = Codec.RLE;
                selectedPayload = rleBuffer.AsSpan(0, rleLength);
                selectedLength = rleLength;
            }

            if (paletteBuffer != null)
            {
                int paletteLength = EncodeBytePalette(raw, paletteBuffer);
                if (paletteLength > 0 && paletteLength < selectedLength)
                {
                    selectedCodec = Codec.BytePalette;
                    selectedPayload = paletteBuffer.AsSpan(0, paletteLength);
                    selectedLength = paletteLength;
                }
            }

            int lz4Length = LZ4Codec.Encode(raw, lz4Buffer, LZ4Level.L00_FAST);
            if (lz4Length > 0 && lz4Length <= selectedLength - 16)
            {
                selectedCodec = Codec.LZ4;
                selectedPayload = lz4Buffer.AsSpan(0, lz4Length);
                selectedLength = lz4Length;
            }

            Span<byte> header = stackalloc byte[FrameHeaderSize];
            header.Clear();
            header[0] = (byte)'K';
            header[1] = (byte)'C';
            header[2] = (byte)'H';
            header[3] = (byte)'2';
            header[4] = (byte)selectedCodec;
            header[5] = 1; // CRC32C is mandatory in v2.
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], checked((ushort)elementSize));
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], checked((uint)selectedLength));
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], SchemaId<T>());
            uint crc = UpdateCrc32C(uint.MaxValue, header[..16]);
            crc = UpdateCrc32C(crc, selectedPayload);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], ~crc);

            writer.Write(header);
            writer.Write(selectedPayload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rleBuffer);
            ArrayPool<byte>.Shared.Return(lz4Buffer);
            if (paletteBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(paletteBuffer);
            }
        }
    }

    internal static T[] DecodeChunk<T>(BinaryReader reader, int chunkArea)
        where T : unmanaged
    {
        var chunk = new T[chunkArea];
        DecodeChunk(reader, chunkArea, chunk);
        return chunk;
    }

    internal static void DecodeChunk<T>(BinaryReader reader, int chunkArea, T[] destination)
        where T : unmanaged
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }
        ValidateChunk(destination.Length, chunkArea);
        int elementSize = Unsafe.SizeOf<T>();
        int rawLength = checked(chunkArea * elementSize);
        Span<byte> header = stackalloc byte[FrameHeaderSize];
        ReadExactly(reader, header);
        int payloadLength = ValidateHeader<T>(header, rawLength);
        byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
        try
        {
            Span<byte> payload = payloadBuffer.AsSpan(0, payloadLength);
            ReadExactly(reader, payload);
            VerifyChecksum(header, payload);
            DecodePayload<T>(header[4], payload, destination.AsSpan(0, chunkArea), rawLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payloadBuffer);
        }
    }

    internal static void VisitChunkRuns<T>(
        BinaryReader reader,
        int chunkArea,
        int chunkIndex,
        Action<int, T, int> visitor)
        where T : unmanaged
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (visitor == null)
        {
            throw new ArgumentNullException(nameof(visitor));
        }
        if (chunkArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkArea));
        }

        int elementSize = Unsafe.SizeOf<T>();
        int rawLength = checked(chunkArea * elementSize);
        Span<byte> headerBuffer = stackalloc byte[FrameHeaderSize];
        ReadExactly(reader, headerBuffer);
        int payloadLength = ValidateHeader<T>(headerBuffer, rawLength);
        byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
        byte[]? decodedBuffer = null;
        try
        {
            Span<byte> payload = payloadBuffer.AsSpan(0, payloadLength);
            ReadExactly(reader, payload);
            VerifyChecksum(headerBuffer, payload);
            byte codec = headerBuffer[4];
            if (codec == (byte)Codec.RLE)
            {
                ValidateRLE<T>(payload, chunkArea);
                VisitRLE<T>(payload, chunkIndex, visitor);
            }
            else if (codec == (byte)Codec.BytePalette)
            {
                ValidatePalette(payload, chunkArea);
                VisitPalette<T>(payload, chunkArea, chunkIndex, visitor);
            }
            else if (codec == (byte)Codec.Raw)
            {
                if (payload.Length != rawLength)
                {
                    throw new InvalidDataException("Raw world chunk length does not match its dimensions.");
                }

                VisitRawRuns<T>(payload, chunkArea, chunkIndex, visitor);
            }
            else
            {
                decodedBuffer = ArrayPool<byte>.Shared.Rent(rawLength);
                Span<byte> decoded = decodedBuffer.AsSpan(0, rawLength);
                DecodeRawPayload(codec, payload, decoded, rawLength);
                VisitRawRuns<T>(decoded, chunkArea, chunkIndex, visitor);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payloadBuffer);
            if (decodedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(decodedBuffer);
            }
        }
    }

    internal static int MigrateV1ToV2<T>(
        string filePath,
        int expectedWidth,
        int expectedHeight,
        int chunkSize)
        where T : unmanaged
    {
        string tempPath = filePath + ".v2.migrate.tmp";
        string backupPath = filePath + ".v1.backup";
        int migratedChunks = 0;
        try
        {
            using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var reader = new BinaryReader(source, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                if (source.Length < WorldLayerFileHeader.HeaderSize)
                {
                    throw new InvalidDataException($"Map file '{filePath}' is not a compatible v1 map.");
                }

                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                int storedChunkSize = reader.ReadInt32();
                int version = reader.ReadInt32();
                if (version != WorldLayerFileHeader.LegacyRLEFormatVersion)
                {
                    throw new InvalidDataException($"Map file '{filePath}' is not a v1 RLE map.");
                }

                if (width != expectedWidth || height != expectedHeight || storedChunkSize != chunkSize)
                {
                    return -1;
                }

                int chunkCount = checked(expectedWidth * expectedHeight);
                long tableEnd = checked(WorldLayerFileHeader.HeaderSize + (long)chunkCount * sizeof(long));
                if (source.Length < tableEnd)
                {
                    throw new InvalidDataException($"Map file '{filePath}' has a truncated offset table.");
                }

                long[] oldOffsets = new long[chunkCount];
                WorldLayerFileHeader.ReadExactly(source, MemoryMarshal.AsBytes(oldOffsets.AsSpan()));
                foreach (long oldOffset in oldOffsets)
                {
                    if (oldOffset != -1 && (oldOffset < tableEnd || oldOffset >= source.Length))
                    {
                        throw new InvalidDataException($"Map file '{filePath}' contains an invalid chunk offset.");
                    }
                }

                using var destination = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                long[] newOffsets = new long[chunkCount];
                WorldLayerFileHeader.WriteHeader(destination, expectedWidth, expectedHeight, chunkSize, newOffsets);
                int chunkArea = checked(chunkSize * chunkSize);
                T[] decoded = new T[chunkArea];
                T[] verified = new T[chunkArea];
                using var destinationWriter = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
                using var destinationReader = new BinaryReader(destination, System.Text.Encoding.UTF8, leaveOpen: true);
                var comparer = EqualityComparer<T>.Default;
                for (int index = 0; index < oldOffsets.Length; index++)
                {
                    long oldOffset = oldOffsets[index];
                    if (oldOffset < 0)
                    {
                        continue;
                    }

                    source.Seek(oldOffset, SeekOrigin.Begin);
                    try
                    {
                        WorldChunkRLECodec.DecodeChunk(reader, chunkArea, decoded);
                    }
                    catch (InvalidDataException)
                    {
                        Array.Clear(decoded, 0, decoded.Length);
                    }

                    destination.Seek(0, SeekOrigin.End);
                    long newOffset = destination.Position;
                    WorldChunkV2Codec.EncodeChunk(destinationWriter, decoded, chunkArea);
                    newOffsets[index] = newOffset;
                    WorldLayerFileHeader.WriteChunkOffset(destination, index, newOffset);
                    destination.Seek(newOffset, SeekOrigin.Begin);
                    WorldChunkV2Codec.DecodeChunk(destinationReader, chunkArea, verified);
                    for (int cell = 0; cell < chunkArea; cell++)
                    {
                        if (!comparer.Equals(decoded[cell], verified[cell]))
                        {
                            throw new InvalidDataException($"Map chunk {index} failed v2 migration verification.");
                        }
                    }

                    migratedChunks++;
                }

                destination.Flush(true);
            }

            if (!File.Exists(backupPath))
            {
                string backupTempPath = backupPath + ".tmp";
                try
                {
                    using (var backupSource = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var backup = new FileStream(backupTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        backupSource.CopyTo(backup);
                        backup.Flush(true);
                    }

                    File.Move(backupTempPath, backupPath);
                }
                finally
                {
                    if (File.Exists(backupTempPath))
                    {
                        File.Delete(backupTempPath);
                    }
                }
            }

            File.Replace(tempPath, filePath, destinationBackupFileName: null);
            return migratedChunks;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static int ValidateHeader<T>(ReadOnlySpan<byte> header, int rawLength)
        where T : unmanaged
    {
        if (header.Length != FrameHeaderSize || header[0] != 'K' || header[1] != 'C' ||
            header[2] != 'H' || header[3] != '2' || header[5] != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != Unsafe.SizeOf<T>() ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) != SchemaId<T>())
        {
            throw new InvalidDataException("World chunk has an invalid v2 frame header.");
        }

        uint rawLengthUnsigned = checked((uint)rawLength);
        uint payloadLengthUnsigned = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (payloadLengthUnsigned > int.MaxValue || payloadLengthUnsigned > MaximumPayloadLength<T>(rawLengthUnsigned))
        {
            throw new InvalidDataException("World chunk payload length exceeds the codec bound.");
        }

        byte codec = header[4];
        if (codec > (byte)Codec.BytePalette)
        {
            throw new InvalidDataException($"World chunk codec {codec} is not supported.");
        }

        return (int)payloadLengthUnsigned;
    }

    private static uint MaximumPayloadLength<T>(uint rawLength) where T : unmanaged
    {
        uint rleMaximum = checked((uint)(Unsafe.SizeOf<T>() + sizeof(ushort)) * rawLength /
            (uint)Unsafe.SizeOf<T>());
        uint lz4Maximum = checked((uint)LZ4Codec.MaximumOutputSize(checked((int)rawLength)));
        return Math.Max(rleMaximum, Math.Max(rawLength, lz4Maximum));
    }

    private static int EncodeRLE<T>(ReadOnlySpan<T> values, Span<byte> destination)
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

    private static int EncodeBytePalette(ReadOnlySpan<byte> values, Span<byte> destination)
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

        int bitsPerIndex = BitsRequired(paletteCount - 1);
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

    private static void DecodePayload<T>(byte codec, ReadOnlySpan<byte> payload, Span<T> destination, int rawLength)
        where T : unmanaged
    {
        if (codec == (byte)Codec.RLE)
        {
            DecodeRLE(payload, destination);
            return;
        }

        if (codec == (byte)Codec.BytePalette)
        {
            DecodePalette(payload, MemoryMarshal.AsBytes(destination));
            return;
        }

        Span<byte> raw = MemoryMarshal.AsBytes(destination);
        DecodeRawPayload(codec, payload, raw, rawLength);
    }

    private static void DecodeRawPayload(byte codec, ReadOnlySpan<byte> payload, Span<byte> output, int rawLength)
    {
        if (codec == (byte)Codec.Raw)
        {
            if (payload.Length != rawLength)
            {
                throw new InvalidDataException("Raw world chunk length does not match its dimensions.");
            }

            payload.CopyTo(output);
            return;
        }

        if (codec == (byte)Codec.LZ4)
        {
            int decoded = LZ4Codec.Decode(payload, output);
            if (decoded != rawLength)
            {
                throw new InvalidDataException("LZ4 world chunk decoded to an unexpected length.");
            }

            return;
        }

        throw new InvalidDataException($"World chunk codec {codec} cannot be decoded as raw data.");
    }

    private static void DecodeRLE<T>(ReadOnlySpan<byte> payload, Span<T> destination)
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

    private static void ValidateRLE<T>(ReadOnlySpan<byte> payload, int chunkArea) where T : unmanaged
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

    private static void VisitRLE<T>(ReadOnlySpan<byte> payload, int chunkIndex, Action<int, T, int> visitor)
        where T : unmanaged
    {
        int elementSize = Unsafe.SizeOf<T>();
        int offset = 0;
        while (offset < payload.Length)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);
            T value = MemoryMarshal.Read<T>(payload.Slice(offset, elementSize));
            visitor(chunkIndex, value, count);
            offset += elementSize;
        }
    }

    private static void ValidatePalette(ReadOnlySpan<byte> payload, int chunkArea)
    {
        if (payload.Length < 4)
        {
            throw new InvalidDataException("Byte-palette chunk header is truncated.");
        }

        int paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int bits = payload[2];
        int expectedBits = BitsRequired(paletteCount - 1);
        int packedLength = checked((int)(((long)chunkArea * bits + 7) / 8));
        if (paletteCount is < 1 or > 256 || bits != expectedBits || payload[3] != 0 ||
            payload.Length != 4 + paletteCount + packedLength)
        {
            throw new InvalidDataException("Byte-palette chunk has invalid framing.");
        }

        ReadOnlySpan<byte> packed = payload[(4 + paletteCount)..];
        for (int cell = 0, bitPosition = 0; cell < chunkArea; cell++, bitPosition += bits)
        {
            if (ReadBits(packed, bitPosition, bits) >= paletteCount)
            {
                throw new InvalidDataException("Byte-palette chunk contains an invalid palette index.");
            }
        }
    }

    private static void DecodePalette(ReadOnlySpan<byte> payload, Span<byte> output)
    {
        int paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int bits = payload[2];
        ReadOnlySpan<byte> palette = payload.Slice(4, paletteCount);
        ReadOnlySpan<byte> packed = payload[(4 + paletteCount)..];
        for (int cell = 0, bitPosition = 0; cell < output.Length; cell++, bitPosition += bits)
        {
            output[cell] = palette[ReadBits(packed, bitPosition, bits)];
        }
    }

    private static void VisitPalette<T>(ReadOnlySpan<byte> payload, int chunkArea, int chunkIndex, Action<int, T, int> visitor)
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
        int current = palette[ReadBits(packed, 0, bits)];
        int runLength = 1;
        for (int cell = 1, bitPosition = bits; cell < chunkArea; cell++, bitPosition += bits)
        {
            int value = palette[ReadBits(packed, bitPosition, bits)];
            if (value == current)
            {
                runLength++;
            }
            else
            {
                EmitByteRun<T>(visitor, chunkIndex, (byte)current, runLength);
                current = value;
                runLength = 1;
            }
        }

        EmitByteRun<T>(visitor, chunkIndex, (byte)current, runLength);
    }

    private static void EmitByteRun<T>(Action<int, T, int> visitor, int chunkIndex, byte value, int count)
        where T : unmanaged
    {
        Span<byte> valueBytes = stackalloc byte[1];
        valueBytes[0] = value;
        T typed = MemoryMarshal.Read<T>(valueBytes);
        visitor(chunkIndex, typed, count);
    }

    private static void VisitRawRuns<T>(ReadOnlySpan<byte> raw, int chunkArea, int chunkIndex, Action<int, T, int> visitor)
        where T : unmanaged
    {
        ReadOnlySpan<T> values = MemoryMarshal.Cast<byte, T>(raw);
        var comparer = EqualityComparer<T>.Default;
        int start = 0;
        while (start < chunkArea)
        {
            T value = values[start];
            int end = start + 1;
            while (end < chunkArea && comparer.Equals(values[end], value))
            {
                end++;
            }

            visitor(chunkIndex, value, end - start);
            start = end;
        }
    }

    private static int ReadBits(ReadOnlySpan<byte> source, int bitPosition, int bitCount)
    {
        if (bitCount == 0)
        {
            return 0;
        }

        int value = 0;
        for (int bit = 0; bit < bitCount; bit++)
        {
            int position = bitPosition + bit;
            value |= ((source[position >> 3] >> (position & 7)) & 1) << bit;
        }

        return value;
    }

    private static int BitsRequired(int value)
    {
        int bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }

        return bits;
    }

    private static void VerifyChecksum(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        uint crc = UpdateCrc32C(uint.MaxValue, header[..16]);
        crc = UpdateCrc32C(crc, payload);
        if (~crc != expected)
        {
            throw new InvalidDataException("World chunk CRC32C mismatch.");
        }
    }

    private static uint UpdateCrc32C(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc = (crc >> 8) ^ s_crc32CTable[(int)((crc ^ value) & 0xff)];
        }

        return crc;
    }

    private static uint[] CreateCrc32CTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : Crc32CPolynomial);
            }

            table[(int)index] = value;
        }

        return table;
    }

    private static class SchemaIdCache<T> where T : unmanaged
    {
        internal static readonly uint Value = ComputeSchemaId<T>();
    }

    private static uint ComputeSchemaId<T>() where T : unmanaged
    {
        string typeName = typeof(T).FullName ?? typeof(T).Name;
        uint hash = SchemaHashSeed;
        foreach (char character in typeName)
        {
            hash = unchecked((hash ^ (byte)character) * 16777619);
            hash = unchecked((hash ^ (byte)(character >> 8)) * 16777619);
        }

        return hash;
    }

    private static void ReadExactly(BinaryReader reader, Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = reader.Read(destination[total..]);
            if (read == 0)
            {
                throw new InvalidDataException("World chunk frame is truncated.");
            }

            total += read;
        }
    }

    private static void ValidateChunk(int bufferLength, int chunkArea)
    {
        if (chunkArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkArea));
        }

        if (bufferLength < chunkArea)
        {
            throw new ArgumentException("Chunk buffer is smaller than the declared chunk area.");
        }
    }
}
