#nullable enable

namespace Kern.Persistence;

using System;
using System.IO;
using System.Runtime.InteropServices;

public static class WorldLayerFileHeader
{
    public const int HeaderSize = 16; // 4 ints (width, height, chunk size, format version)
    public const int FormatVersionOffset = sizeof(int) * 3;
    public const int CurrentFormatVersion = 1;

    public static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer.Slice(total));
            if (n <= 0)
            {
                throw new EndOfStreamException();
            }

            total += n;
        }
    }

    public static bool TryReadHeader(
        Stream stream,
        int expectedWidth,
        int expectedHeight,
        int expectedChunkSize,
        long[] chunkOffsets)
    {
        long offsetTableBytes = (long)chunkOffsets.Length * sizeof(long);
        if (stream.Length < HeaderSize)
        {
            return false;
        }

        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            stream.Seek(0, SeekOrigin.Begin);
            int w = reader.ReadInt32();
            int h = reader.ReadInt32();
            int s = reader.ReadInt32();
            int formatVersion = reader.ReadInt32();

            if (w == expectedWidth && h == expectedHeight && s == expectedChunkSize &&
                formatVersion == CurrentFormatVersion &&
                stream.Length >= HeaderSize + offsetTableBytes)
            {
                var byteSpan = MemoryMarshal.AsBytes(chunkOffsets.AsSpan());
                ReadExactly(stream, byteSpan);
                return true;
            }
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        return false;
    }

    public static void WriteHeader(
        Stream stream,
        int widthChunks,
        int heightChunks,
        int chunkSize,
        long[] chunkOffsets)
    {
        Array.Fill(chunkOffsets, -1);
        stream.SetLength(0);
        stream.Seek(0, SeekOrigin.Begin);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(widthChunks);
        writer.Write(heightChunks);
        writer.Write(chunkSize);
        writer.Write(CurrentFormatVersion);
        var byteSpan = MemoryMarshal.AsBytes(chunkOffsets.AsSpan());
        stream.Write(byteSpan);
        stream.Flush();
    }

    public static void WriteChunkOffset(Stream stream, int chunkIndex, long offset)
    {
        long tablePos = HeaderSize + (chunkIndex * sizeof(long));
        stream.Seek(tablePos, SeekOrigin.Begin);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(offset);
    }

    public static int? TryReadFormatVersion(Stream stream)
    {
        try
        {
            if (stream.Length < HeaderSize)
            {
                return null;
            }

            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            return reader.ReadInt32();
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
