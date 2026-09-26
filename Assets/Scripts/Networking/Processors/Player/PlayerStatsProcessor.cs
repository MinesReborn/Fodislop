#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;

namespace Kern.Networking.Processors;

public sealed class PlayerStatsProcessor(IPlayerStats stats) :
    IPacketProcessor<LevelPacket>,
    IPacketProcessor<HealthPacket>,
    IPacketProcessor<CurrencyPacket>,
    IPacketProcessor<GeologyPacket>,
    IPacketProcessor<BasketPacket>,
    IPacketProcessor<MaxDepthPacket>,
    IPacketProcessor<DailyBonusStatePacket>,
    IPacketProcessor<SkillProgressPacket>
{
    public void Process(LevelPacket packet) => stats.SetLevel(packet.Level);

    public void Process(HealthPacket packet) => stats.SetHealth(packet.Current, packet.Max);

    public void Process(CurrencyPacket packet) => stats.SetCurrency(packet.Money, packet.Creds);

    public void Process(GeologyPacket packet) =>
        stats.SetGeology(packet.Current, packet.Max, packet.Cell, packet.Text);

    public void Process(BasketPacket packet) =>
        stats.SetBasket(packet.Capacity, packet.Contents);

    public void Process(MaxDepthPacket packet) => stats.SetMaxDepth(packet.Depth);

    public void Process(DailyBonusStatePacket packet) =>
        stats.SetDailyBonusAvailable(packet.Enabled);

    public void Process(SkillProgressPacket packet) =>
        stats.SetSkillProgress(packet.Skill, packet.Current, packet.Max);
}
