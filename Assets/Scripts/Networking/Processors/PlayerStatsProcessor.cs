using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;

namespace Fodinae.Scripts.Networking.Processors
{
    public class PlayerStatsProcessor : IPacketProcessor<LevelPacket>, IPacketProcessor<HealthPacket>, IPacketProcessor<CurrencyPacket>, IPacketProcessor<GeologyPacket>, IPacketProcessor<BasketPacket>, IPacketProcessor<MaxDepthPacket>, IPacketProcessor<DailyBonusStatePacket>, IPacketProcessor<SkillProgressPacket>
    {
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>();

        public void Process(LevelPacket packet) => Stats.SetLevel(packet.Level);
        public void Process(HealthPacket packet) => Stats.SetHealth(packet.Current, packet.Max);
        public void Process(CurrencyPacket packet) => Stats.SetCurrency(packet.Money, packet.Creds);
        public void Process(GeologyPacket packet) => Stats.SetGeology(packet.Current, packet.Max, packet.Cell, packet.Text);
        public void Process(BasketPacket packet) => Stats.SetBasket(packet.Capacity, packet.Contents);
        public void Process(MaxDepthPacket packet) => Stats.SetMaxDepth(packet.Depth);
        public void Process(DailyBonusStatePacket packet) => Stats.SetDailyBonusAvailable(packet.Enabled);
        public void Process(SkillProgressPacket packet) => Stats.SetSkillProgress(packet.Skill, packet.Current, packet.Max);
    }
}
