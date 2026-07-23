using System.Linq;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Client.Packets.Connection;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class StatusProcessor : IPacketProcessor<OnlinePacket>, IPacketProcessor<PingPacket>, IPacketProcessor<OutdatedClientPacket>, IPacketProcessor<AddStatusLinePacket>, IPacketProcessor<ClearStatusLinePacket>, IPacketProcessor<ClearStatusPacket>
    {
        private static IPlayerStats Stats => ServiceLocator.Resolve<IPlayerStats>() ?? PlayerStatsModel.Instance;

        public void Process(OnlinePacket packet)
        {
            var fps = FPSCounter.Instance;
            if (fps != null)
            {
                fps.SetOnline((int)packet.Players, (int)packet.Programmator);
            }
        }

        public void Process(PingPacket packet)
        {
            var fps = FPSCounter.Instance;
            if (fps != null)
            {
                fps.SetPing(packet.PreviousPing);
            }

            Fodinae.Scripts.Networking.NetworkService.Send(new PongPacket(packet.SentAt));
        }

        public void Process(OutdatedClientPacket packet)
        {
            Debug.LogError($"[StatusProcessor] Клиент устарел: {packet.Name}");
            Debug.LogError($"[StatusProcessor] {packet.Description}");
            Debug.LogError($"[StatusProcessor] Скачать: {packet.UpdateURL}");
            Application.OpenURL(packet.UpdateURL);
        }

        public void Process(AddStatusLinePacket packet)
        {
            var sysColor = packet.Color;
            var unityColor = new Color(sysColor.R / 255f, sysColor.G / 255f, sysColor.B / 255f, sysColor.A / 255f);
            long expiry = 0;
            if (packet.Text.Count > 1)
            {
                long.TryParse(packet.Text[1], out expiry);
            }

            Stats.AddStatusLine(packet.Tag, packet.Text.ToArray(), unityColor, packet.BlinkRate, expiry);
        }

        public void Process(ClearStatusLinePacket packet) => Stats.RemoveStatusLine(packet.Tag);
        public void Process(ClearStatusPacket packet) => Stats.ClearStatusLines();
    }
}
