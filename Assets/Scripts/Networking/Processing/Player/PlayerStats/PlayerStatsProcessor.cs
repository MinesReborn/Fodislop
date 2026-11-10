#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;

namespace Kern.Networking.Processing;

public sealed class PlayerStatsProcessor :
    IPacketProcessor<LevelPacket>,
    IPacketProcessor<HealthPacket>,
    IPacketProcessor<CurrencyPacket>,
    IPacketProcessor<GeologyPacket>,
    IPacketProcessor<BasketPacket>,
    IPacketProcessor<MaxDepthPacket>,
    IPacketProcessor<SkillProgressPacket>,
    IBatchAwareProcessor
{
    private readonly IPlayerStats _stats;
    public PlayerStatsProcessor(IPlayerStats stats)
    {
        _stats = stats;
        _batchStats = _stats as IBatchAwareProcessor;
    }

    private readonly IBatchAwareProcessor? _batchStats;

    public void BeginBatch() => _batchStats?.BeginBatch();

    public void EndBatch() => _batchStats?.EndBatch();

    public void Process(LevelPacket packet) => _stats.SetLevel(packet.Level);

    public void Process(HealthPacket packet) => _stats.SetHealth(packet.Current, packet.Max);

    public void Process(CurrencyPacket packet) => _stats.SetCurrency(packet.Money, packet.Creds);

    public void Process(GeologyPacket packet) =>
        _stats.SetGeology(packet.Current, packet.Max, packet.Cell, packet.Text);

    public void Process(BasketPacket packet) =>
        _stats.SetBasket(packet.Capacity, packet.Contents);

    public void Process(MaxDepthPacket packet) => _stats.SetMaxDepth(packet.Depth);

    public void Process(SkillProgressPacket packet) =>
        _stats.SetSkillProgress(packet.Skill, packet.Current, packet.Max);
}
