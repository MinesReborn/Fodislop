#nullable enable

using System;
using System.Text;
using NUnit.Framework;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class ETagCalculatorTests
{
    [Test]
    public void Calculate_NullOrEmpty_ReturnsNull()
    {
        Assert.That(ETagCalculator.Calculate((byte[]?)null), Is.Null);
        Assert.That(ETagCalculator.Calculate(Array.Empty<byte>()), Is.Null);
        Assert.That(ETagCalculator.Calculate(ReadOnlySpan<byte>.Empty), Is.Null);
    }

    [Test]
    public void Calculate_KnownInput_ReturnsCorrectMd5HexString()
    {
        // MD5("hello") = 5d41402abc4b2a76b9719d911017c592
        byte[] input = Encoding.UTF8.GetBytes("hello");
        string? resultFromArray = ETagCalculator.Calculate(input);
        string? resultFromSpan = ETagCalculator.Calculate((ReadOnlySpan<byte>)input);

        Assert.That(resultFromArray, Is.EqualTo("5d41402abc4b2a76b9719d911017c592"));
        Assert.That(resultFromSpan, Is.EqualTo("5d41402abc4b2a76b9719d911017c592"));
    }
}
