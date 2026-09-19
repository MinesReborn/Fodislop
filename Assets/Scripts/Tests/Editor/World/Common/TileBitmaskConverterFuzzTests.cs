#nullable enable

using Kern;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
[Category("FuzzPure")]
public class TileBitmaskConverterFuzzTests
{
    [TestCase((byte)0x00, (byte)0x00)]
    [TestCase((byte)0x01, (byte)0x01)]
    [TestCase((byte)0x02, (byte)0x00)]
    [TestCase((byte)0x04, (byte)0xC1)]
    [TestCase((byte)0x08, (byte)0x41)]
    [TestCase((byte)0x10, (byte)0x81)]
    [TestCase((byte)0x20, (byte)0x41)]
    [TestCase((byte)0x40, (byte)0x41)]
    [TestCase((byte)0x80, (byte)0x00)]
    [TestCase((byte)0xFF, (byte)0x0D)]
    public void GetDescriptor_LutBoundaryMasks(byte mask, byte expected)
    {
        Assert.That(TileBitmaskConverter.GetDescriptor(mask), Is.EqualTo(expected), $"mask=0x{mask:X2}");
    }

    [Test]
    public void GetDescriptor_EveryMask_BaseIndexIn0To13()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            byte desc = TileBitmaskConverter.GetDescriptor((byte)mask);
            int baseIndex = desc & 0x1F;
            Assert.That(baseIndex, Is.LessThanOrEqualTo(13), $"mask=0x{mask:X2}, baseIndex={baseIndex}");
        }
    }

    [Test]
    public void GetDescriptor_EveryMask_TransformationFlagsInRange()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            byte desc = TileBitmaskConverter.GetDescriptor((byte)mask);
            byte transformFlags = (byte)(desc >> 5);
            Assert.That(transformFlags, Is.LessThanOrEqualTo(0x07), $"mask=0x{mask:X2}, flags=0x{transformFlags:X2}");
        }
    }

    [Test]
    public void GetDescriptor_RotationSymmetry_TopBottomEdgesAgree()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            byte top = TileBitmaskConverter.GetDescriptor((byte)mask);
            byte bottom = TileBitmaskConverter.GetDescriptor((byte)(mask ^ 0x44));
            Assert.That(top, Is.EqualTo(bottom), $"mask=0x{mask:X2}");
        }
    }

    [Test]
    public void GetDescriptor_RotationSymmetry_LeftRightEdgesAgree()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            byte left = TileBitmaskConverter.GetDescriptor((byte)mask);
            byte right = TileBitmaskConverter.GetDescriptor((byte)(mask ^ 0x11));
            Assert.That(left, Is.EqualTo(right), $"mask=0x{mask:X2}");
        }
    }
}
