using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;

namespace Fodinae.Scripts.Networking.Processors
{
    public class PlayerStatsProcessor : IPacketProcessor<LevelPacket>, IPacketProcessor<HealthPacket>, IPacketProcessor<CurrencyPacket>, IPacketProcessor<GeologyPacket>, IPacketProcessor<BasketPacket>, IPacketProcessor<MaxDepthPacket>, IPacketProcessor<DailyBonusStatePacket>, IPacketProcessor<SkillProgressPacket>
    {
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>() ?? PlayerStatsModel.Instance;

        public void Process(LevelPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetLevel(packet.Level);
            }
        }

        public void Process(HealthPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetHealth(packet.Current, packet.Max);
            }
        }

        public void Process(CurrencyPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetCurrency(packet.Money, packet.Creds);
            }
        }

        public void Process(GeologyPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetGeology(packet.Current, packet.Max, packet.Cell, packet.Text);
            }
        }

        public void Process(BasketPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetBasket(packet.Capacity, packet.Contents);
            }
        }

        public void Process(MaxDepthPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetMaxDepth(packet.Depth);
            }
        }

        public void Process(DailyBonusStatePacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetDailyBonusAvailable(packet.Enabled);
            }
        }

        public void Process(SkillProgressPacket packet)
        {
            var s = Stats;
            if (s != null)
            {
                s.SetSkillProgress(packet.Skill, packet.Current, packet.Max);
            }
        }
    }
}
