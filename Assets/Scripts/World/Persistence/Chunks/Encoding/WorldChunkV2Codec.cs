#nullable enable

namespace Kern.Persistence;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;

/// <summary>Current framed chunk codec: raw, RLE, LZ4 block, and byte palette.</summary>
internal static class WorldChunkV2Codec
{
    internal const int FrameHeaderSize = 20;
    private const byte Crc32CFlag = 1;
    internal const byte ChunkIndexBoundFlag = 2;

    internal sealed class VisitorCallbackException : Exception
    {
        private readonly ExceptionDispatchInfo _exceptionDispatchInfo;

        internal VisitorCallbackException(Exception exception)
            : base("A world chunk visitor callback failed.", exception)
        {
            _exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
        }

        internal void Rethrow() => _exceptionDispatchInfo.Throw();
    }

    private enum Codec : byte
    {
        Raw = 0,
        RLE = 1,
        LZ4 = 2,
        BytePalette = 3,
    }

    internal static uint SchemaId<T>() where T : unmanaged
    {
        return WorldChunkSchema.SchemaIdCache<T>.Value;
    }

    internal static void EncodeLegacyV2Chunk<T>(BinaryWriter writer, T[] chunk, int chunkArea)
        where T : unmanaged
    {
        EncodeChunkCore(writer, chunk, chunkArea, null);
    }

    internal static void EncodeChunk<T>(BinaryWriter writer, T[] chunk, int chunkArea, int chunkIndex)
        where T : unmanaged
    {
        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        EncodeChunkCore(writer, chunk, chunkArea, chunkIndex);
    }

    private static void EncodeChunkCore<T>(BinaryWriter writer, T[] chunk, int chunkArea, int? chunkIndex)
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
        byte[]? rleBuffer = null;
        byte[]? lz4Buffer = null;
        byte[]? paletteBuffer = null;

        try
        {
            rleBuffer = ArrayPool<byte>.Shared.Rent(rleCapacity);
            lz4Buffer = ArrayPool<byte>.Shared.Rent(LZ4Codec.MaximumOutputSize(rawLength));
            if (elementSize == 1)
            {
                paletteBuffer = ArrayPool<byte>.Shared.Rent(checked(chunkArea + 260));
            }

            ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(chunk.AsSpan(0, chunkArea));

            Codec selectedCodec = Codec.Raw;
            ReadOnlySpan<byte> selectedPayload = raw;
            int selectedLength = rawLength;

            int rleLength = WorldChunkRunLengthCodec.EncodeRLE<T>(chunk.AsSpan(0, chunkArea), rleBuffer);
            if (rleLength < selectedLength)
            {
                selectedCodec = Codec.RLE;
                selectedPayload = rleBuffer.AsSpan(0, rleLength);
                selectedLength = rleLength;
            }

            if (paletteBuffer != null)
            {
                int paletteLength = WorldChunkPaletteCodec.EncodeBytePalette(raw, paletteBuffer);
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
            header[5] = chunkIndex.HasValue
                ? (byte)(Crc32CFlag | ChunkIndexBoundFlag)
                : Crc32CFlag;
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], checked((ushort)elementSize));
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], checked((uint)selectedLength));
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], SchemaId<T>());
            uint crc = WorldChunkIntegrity.UpdateCrc32C(uint.MaxValue, header[..16]);
            if (chunkIndex.HasValue)
            {
                Span<byte> chunkIndexBytes = stackalloc byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(chunkIndexBytes, chunkIndex.Value);
                crc = WorldChunkIntegrity.UpdateCrc32C(crc, chunkIndexBytes);
            }

            crc = WorldChunkIntegrity.UpdateCrc32C(crc, selectedPayload);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], ~crc);

            writer.Write(header);
            writer.Write(selectedPayload);
        }
        finally
        {
            if (rleBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(rleBuffer);
            }

            if (lz4Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(lz4Buffer);
            }

            if (paletteBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(paletteBuffer);
            }
        }
    }

    internal static T[] DecodeChunk<T>(BinaryReader reader, int chunkArea)
        where T : unmanaged
    {
        return DecodeChunk<T>(reader, chunkArea, (int?)null);
    }

    internal static T[] DecodeChunk<T>(BinaryReader reader, int chunkArea, int chunkIndex)
        where T : unmanaged
    {
        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        return DecodeChunk<T>(reader, chunkArea, (int?)chunkIndex);
    }

    private static T[] DecodeChunk<T>(BinaryReader reader, int chunkArea, int? chunkIndex)
        where T : unmanaged
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (chunkArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkArea));
        }

        var chunk = new T[chunkArea];
        DecodeChunk(reader, chunkArea, chunk, chunkIndex);
        return chunk;
    }

    internal static void DecodeChunk<T>(BinaryReader reader, int chunkArea, T[] destination)
        where T : unmanaged
    {
        DecodeChunk(reader, chunkArea, destination, null);
    }

    internal static void DecodeChunk<T>(BinaryReader reader, int chunkArea, T[] destination, int chunkIndex)
        where T : unmanaged
    {
        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        DecodeChunk(reader, chunkArea, destination, (int?)chunkIndex);
    }

    private static void DecodeChunk<T>(BinaryReader reader, int chunkArea, T[] destination, int? chunkIndex)
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
            WorldChunkIntegrity.VerifyChecksum(header, payload, chunkIndex);
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
        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
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
            WorldChunkIntegrity.VerifyChecksum(headerBuffer, payload, chunkIndex);
            byte codec = headerBuffer[4];
            if (codec == (byte)Codec.RLE)
            {
                WorldChunkRunLengthCodec.ValidateRLE<T>(payload, chunkArea);
                WorldChunkRunLengthCodec.VisitRLE<T>(payload, chunkIndex, visitor);
            }
            else if (codec == (byte)Codec.BytePalette)
            {
                WorldChunkPaletteCodec.ValidatePalette(payload, chunkArea);
                WorldChunkPaletteCodec.VisitPalette<T>(payload, chunkArea, chunkIndex, visitor);
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

    private static int ValidateHeader<T>(ReadOnlySpan<byte> header, int rawLength)
        where T : unmanaged
    {
        byte flags = header.Length > 5 ? header[5] : (byte)0;
        if (header.Length != FrameHeaderSize || header[0] != 'K' || header[1] != 'C' ||
            header[2] != 'H' || header[3] != '2' ||
            (flags & Crc32CFlag) == 0 || (flags & ~(Crc32CFlag | ChunkIndexBoundFlag)) != 0 ||
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

    private static void DecodePayload<T>(byte codec, ReadOnlySpan<byte> payload, Span<T> destination, int rawLength)
        where T : unmanaged
    {
        if (codec == (byte)Codec.RLE)
        {
            WorldChunkRunLengthCodec.DecodeRLE(payload, destination);
            return;
        }

        if (codec == (byte)Codec.BytePalette)
        {
            WorldChunkPaletteCodec.DecodePalette(payload, MemoryMarshal.AsBytes(destination));
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

    internal static void EmitByteRun<T>(Action<int, T, int> visitor, int chunkIndex, byte value, int count)
        where T : unmanaged
    {
        Span<byte> valueBytes = stackalloc byte[1];
        valueBytes[0] = value;
        T typed = MemoryMarshal.Read<T>(valueBytes);
        InvokeVisitor(visitor, chunkIndex, typed, count);
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

            InvokeVisitor(visitor, chunkIndex, value, end - start);
            start = end;
        }
    }

    internal static void InvokeVisitor<T>(Action<int, T, int> visitor, int chunkIndex, T value, int count)
    {
        try
        {
            visitor(chunkIndex, value, count);
        }
        catch (InvalidDataException exception)
        {
            throw new VisitorCallbackException(exception);
        }
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
