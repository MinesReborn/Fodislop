#nullable enable

using System;
using System.IO;
using Kern.Persistence;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public class WorldChunkRleCodecBoundaryTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(255)]
    [TestCase(256)]
    [TestCase(65535)]
    [TestCase(65536)]
    public void EncodeDecode_UniformChunk_RoundTrips(int area)
    {
        byte[] data = new byte[area];
        for (int i = 0; i < area; i++) data[i] = (byte)(i & 0xFF);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            WorldChunkRleCodec.EncodeChunk(w, data, area);

        ms.Position = 0;
        using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        byte[] decoded = WorldChunkRleCodec.DecodeChunk<byte>(r, area);

        Assert.That(decoded, Is.EqualTo(data), $"area={area}");
    }

    [Test]
    public void EncodeDecode_AllSameByte_RoundTrips()
    {
        byte[] data = new byte[4096];
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            WorldChunkRleCodec.EncodeChunk(w, data, data.Length);

        ms.Position = 0;
        using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        byte[] decoded = WorldChunkRleCodec.DecodeChunk<byte>(r, data.Length);
        Assert.That(decoded, Is.EqualTo(data));
    }

    [Test]
    public void EncodeDecode_ReEncoding_SameBytes()
    {
        byte[] data = new byte[2048];
        var rng = new Random(42);
        for (int i = 0; i < data.Length; i++) data[i] = (byte)rng.Next(0, 256);

        using var ms1 = new MemoryStream();
        using (var w = new BinaryWriter(ms1, System.Text.Encoding.UTF8, leaveOpen: true))
            WorldChunkRleCodec.EncodeChunk(w, data, data.Length);

        using var ms2 = new MemoryStream();
        using (var w = new BinaryWriter(ms2, System.Text.Encoding.UTF8, leaveOpen: true))
            WorldChunkRleCodec.EncodeChunk(w, data, data.Length);

        Assert.That(ms1.ToArray(), Is.EqualTo(ms2.ToArray()));
    }

    [Test]
    public void Decode_TruncatedStream_Throws()
    {
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            WorldChunkRleCodec.EncodeChunk(w, data, 1024);

        byte[] truncated = new byte[16];
        ms.Position = 0;
        ms.Read(truncated, 0, truncated.Length);

        using var r = new BinaryReader(new MemoryStream(truncated), System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() => WorldChunkRleCodec.DecodeChunk<byte>(r, 1024));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void EncodeDecode_NonPositiveArea_Throws(int area)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldChunkRleCodec.EncodeChunk<int>(w, new int[10], area));
        using var r = new BinaryReader(ms);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldChunkRleCodec.DecodeChunk<int>(r, area));
    }

    [Test]
    public void EncodeDecode_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WorldChunkRleCodec.EncodeChunk<int>(null!, new int[10], 10));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        Assert.Throws<ArgumentNullException>(() => WorldChunkRleCodec.EncodeChunk<int>(w, null!, 10));
    }
}
