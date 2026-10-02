#nullable enable

using System;
using NUnit.Framework;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class SmartBufferTests
{
    [Test]
    public void PushAndIndex_WithinBounds_ReturnsPushedValues()
    {
        using var buffer = new SmartBuffer(16, usePool: true);
        buffer.Push(10);
        buffer.Push(20);
        buffer.Push(30);

        Assert.That(buffer.Length, Is.EqualTo(3));
        Assert.That(buffer[0], Is.EqualTo(10));
        Assert.That(buffer[1], Is.EqualTo(20));
        Assert.That(buffer[2], Is.EqualTo(30));
    }

    [Test]
    public void Indexer_AtOrPastLength_ThrowsIndexOutOfRangeException()
    {
        using var buffer = new SmartBuffer(16, usePool: false);
        buffer.Push(42);

        Assert.That(buffer.Length, Is.EqualTo(1));
        Assert.That(buffer[0], Is.EqualTo(42));
        Assert.Throws<IndexOutOfRangeException>(() => _ = buffer[1]);
        Assert.Throws<IndexOutOfRangeException>(() => _ = buffer[2]);
        Assert.Throws<IndexOutOfRangeException>(() => _ = buffer[-1]);
    }

    [Test]
    public void CopyFromArray_PopulatesBufferAndSetsLength()
    {
        using var buffer = new SmartBuffer(16, usePool: true);
        byte[] source = [1, 2, 3, 4, 5];

        int copied = buffer.CopyFromArray(source);

        Assert.That(copied, Is.EqualTo(5));
        Assert.That(buffer.Length, Is.EqualTo(5));
        Assert.That(buffer.Span.ToArray(), Is.EqualTo(source));
    }

    [Test]
    public void Clear_ResetsLengthToZero()
    {
        using var buffer = new SmartBuffer(8, usePool: false);
        buffer.Push(1);
        buffer.Push(2);

        buffer.Clear();

        Assert.That(buffer.Length, Is.EqualTo(0));
        Assert.Throws<IndexOutOfRangeException>(() => _ = buffer[0]);
    }
}
