#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class VfxPacketProcessor(
    IServerVfxService vfx,
    ILocalPlayerState localPlayer) : IPacketProcessor<VFXPacket>
{
    public void Process(VFXPacket packet)
    {
        if (packet.EffectType == VFX.Bz)
        {
            localPlayer.Current?.ConfirmDigAction(packet.X, packet.Y);
        }

        vfx.PlayEffect(packet);
    }
}
