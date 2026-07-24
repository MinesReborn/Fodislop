using NUnit.Framework;
using Fodinae.Scripts.World;
using UnityEngine;

namespace Fodinae.Tests.World
{
    [TestFixture]
    public class CoordinateUtilsTests
    {
        private const int TEST_WORLD_HEIGHT = 256;

        [Test]
        public void ServerToUnityY_TopLeftZero_ReturnsCorrectCenteredY()
        {
            // For world height 256, server Y=0 maps to (256 - 1 - 0) + 0.5 = 255.5f in Unity world space
            float unityY = CoordinateUtils.ServerToUnityY(0, TEST_WORLD_HEIGHT);
            Assert.AreEqual(255.5f, unityY, 0.001f);
        }

        [Test]
        public void UnityToServerY_FloorConversion_ReturnsOriginalServerY()
        {
            // Unity Y 255.5f corresponds to Server Y 0
            int serverY = CoordinateUtils.UnityToServerY(255.5f, TEST_WORLD_HEIGHT);
            Assert.AreEqual(0, serverY);

            // Unity Y 0.5f corresponds to Server Y 255
            serverY = CoordinateUtils.UnityToServerY(0.5f, TEST_WORLD_HEIGHT);
            Assert.AreEqual(255, serverY);
        }

        [Test]
        public void Roundtrip_ServerToUnityToServer_IsPreserved()
        {
            for (int y = 0; y < 100; y += 10)
            {
                float unityY = CoordinateUtils.ServerToUnityY(y, TEST_WORLD_HEIGHT);
                int roundtripY = CoordinateUtils.UnityToServerY(unityY, TEST_WORLD_HEIGHT);
                Assert.AreEqual(y, roundtripY, $"Roundtrip failed for server Y={y}");
            }
        }

        [Test]
        public void ServerToUnityPos_ValidCoordinates_ReturnsCenteredVector()
        {
            Vector3 unityPos = CoordinateUtils.ServerToUnityPos(10, 20, TEST_WORLD_HEIGHT, -1f);

            Assert.AreEqual(10.5f, unityPos.x, 0.001f);
            Assert.AreEqual(235.5f, unityPos.y, 0.001f); // 256 - 1 - 20 + 0.5 = 235.5
            Assert.AreEqual(-1f, unityPos.z, 0.001f);
        }

        [Test]
        public void UnityToServerPos_ValidVector_ReturnsExactGridPos()
        {
            Vector3 unityPos = new Vector3(10.2f, 235.8f, 0f);
            Vector2Int serverPos = CoordinateUtils.UnityToServerPos(unityPos, TEST_WORLD_HEIGHT);

            Assert.AreEqual(10, serverPos.x);
            Assert.AreEqual(20, serverPos.y);
        }

        [Test]
        public void UnityToServerY_NegativeUnityY_WrapsCorrectlyWithinBounds()
        {
            // Testing wrapping behavior for Y below zero
            int serverY = CoordinateUtils.UnityToServerY(-0.5f, TEST_WORLD_HEIGHT);
            Assert.IsTrue(serverY >= 0 && serverY < TEST_WORLD_HEIGHT, "Server Y should be wrapped within [0, worldHeight)");
        }
    }
}
