using NUnit.Framework;
using Fodinae.Scripts;

namespace Fodinae.Tests.World
{
    [TestFixture]
    public class TileBitmaskConverterTests
    {
        [Test]
        public void GetDescriptor_ZeroMask_ReturnsBaseDescriptor()
        {
            byte descriptor = TileBitmaskConverter.GetDescriptor(0x00);
            Assert.AreEqual(0x00, descriptor, "Isolated tile with zero presence mask should return descriptor 0x00.");
        }

        [Test]
        public void GetDescriptor_FullPresenceMask_ReturnsFullTileDescriptor()
        {
            byte descriptor = TileBitmaskConverter.GetDescriptor(0xFF);
            Assert.AreEqual(0x0D, descriptor, "Full presence mask 255 (0xFF) should return full tile descriptor index 0x0D.");
        }

        [Test]
        public void GetDescriptor_AllMaskValuesFrom0To255_ReturnValidDescriptors()
        {
            for (int i = 0; i <= 255; i++)
            {
                byte desc = TileBitmaskConverter.GetDescriptor((byte)i);
                int baseIndex = desc & 0x1F;
                Assert.IsTrue(baseIndex <= 13, $"Descriptor for mask {i} has base tile index {baseIndex} exceeding maximum 13.");
            }
        }
    }
}
