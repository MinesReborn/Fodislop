#nullable enable

namespace Kern.Tests.World;

using System;
using System.IO;
using Kern.Persistence;
using NUnit.Framework;

[TestFixture]
public class WorldChunkRleCodecTests
{
    [Test]
    public void EncodeAndDecode_UniformChunk_RestoresIdenticalData()
    {
        const int area = 1024;
        ushort[] original = new ushort[area];
        Array.Fill(original, (ushort)42);

        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkRleCodec.EncodeChunk(writer, original, area);
        }

        memory.Position = 0;
        using var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        ushort[] decoded = WorldChunkRleCodec.DecodeChunk<ushort>(reader, area);

        Assert.AreEqual(original, decoded);
    }

    [Test]
    public void EncodeAndDecode_AlternatingPattern_RestoresIdenticalData()
    {
        const int area = 256;
        int[] original = new int[area];
        for (int i = 0; i < area; i++)
        {
            original[i] = i % 2 == 0 ? 100 : 200;
        }

        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkRleCodec.EncodeChunk(writer, original, area);
        }

        memory.Position = 0;
        using var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        int[] decoded = WorldChunkRleCodec.DecodeChunk<int>(reader, area);

        Assert.AreEqual(original, decoded);
    }

    [Test]
    public void EncodeAndDecode_VariousRunLengths_RestoresIdenticalData()
    {
        const int area = 500;
        byte[] original = new byte[area];
        Array.Fill(original, (byte)1, 0, 50);
        Array.Fill(original, (byte)2, 50, 150);
        Array.Fill(original, (byte)3, 200, 300);

        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WorldChunkRleCodec.EncodeChunk(writer, original, area);
        }

        memory.Position = 0;
        using var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        byte[] decoded = WorldChunkRleCodec.DecodeChunk<byte>(reader, area);

        Assert.AreEqual(original, decoded);
    }

    [Test]
    public void Decode_IncompleteStream_ThrowsInvalidDataException()
    {
        const int area = 1024;
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)10); // only 10 cells encoded, expecting 1024
            writer.Write((ushort)1);
        }

        memory.Position = 0;
        using var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() =>
        {
            WorldChunkRleCodec.DecodeChunk<ushort>(reader, area);
        });
    }

    [Test]
    public void Encode_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            WorldChunkRleCodec.EncodeChunk<int>(null!, new int[10], 10);
        });

        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        Assert.Throws<ArgumentNullException>(() =>
        {
            WorldChunkRleCodec.EncodeChunk<int>(writer, null!, 10);
        });
    }

    [Test]
    public void Decode_NullReader_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            WorldChunkRleCodec.DecodeChunk<int>(null!, 10);
        });
    }

    [Test]
    public void EncodeAndDecode_NonPositiveArea_ThrowsArgumentOutOfRangeException()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            WorldChunkRleCodec.EncodeChunk(writer, new int[10], 0);
        });

        using var reader = new BinaryReader(memory);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            WorldChunkRleCodec.DecodeChunk<int>(reader, -1);
        });
    }
}
