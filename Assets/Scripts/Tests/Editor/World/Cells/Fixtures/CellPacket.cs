#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Tests;

internal static class CellPacket
{
    public static CellConfigurationPacket Create(
        bool? passable = null,
        CellRenderLayer drawLayer = CellRenderLayer.Foreground,
        CellOutlineKind outline = CellOutlineKind.Pliant,
        byte reliefGroup = 0,
        CellAnimationType animationType = CellAnimationType.None,
        float frameAnimationSpeed = 0f,
        int mapColor = 0) =>
        new(
            Properties: (passable ?? drawLayer != CellRenderLayer.Foreground)
                ? CellConfigProperties.Passable
                : CellConfigProperties.None,
            DrawLayer: drawLayer,
            Outline: outline,
            ReliefGroup: reliefGroup,
            AnimationType: animationType,
            AnimationPalette: 0,
            FrameAnimationSpeed: frameAnimationSpeed,
            MapColor: mapColor);
}
