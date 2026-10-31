#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processing;

public sealed class VisualPacketProcessor : IPacketProcessor<VisualPacket>
{
    private readonly IServerVisualEffectService _visualService;
    private readonly ILocalPlayerState _localPlayer;
    public VisualPacketProcessor(
    IServerVisualEffectService visualService,
    ILocalPlayerState localPlayer)
    {
        _visualService = visualService;
        _localPlayer = localPlayer;
    }

    public void Process(VisualPacket packet)
    {
        if (packet.EffectType == VisualEffect.Bz)
        {
            _localPlayer.Current?.ConfirmDigAction(packet.X, packet.Y);
        }

        _visualService.PlayEffect(packet);
    }
}
