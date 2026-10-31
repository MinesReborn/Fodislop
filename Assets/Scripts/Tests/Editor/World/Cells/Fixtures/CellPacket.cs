#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Tests;

internal static class CellPacket
{
    public static CellConfigurationPacket Create(
        CellConfigProperties properties = CellConfigProperties.None,
        CellAnimationType shaderAnimation = CellAnimationType.None,
        float frameAnimationSpeed = 0f,
        int mapColor = 0,
        byte reliefGroup = 0) =>
        new(
            properties,
            CellDistortionType.Neutral,
            shaderAnimation,
            (byte)frameAnimationSpeed,
            0,
            mapColor,
            reliefGroup);
}
