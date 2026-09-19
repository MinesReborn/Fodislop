#nullable enable

using System.Linq;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using UnityEngine;

namespace Kern.Networking.Processors;

public sealed class StatusProcessor(
    IPlayerStats stats,
    NetworkStatusModel statusModel,
    INetworkService networkService,
    ILocalizationService? loc = null) :
    IPacketProcessor<OnlinePacket>,
    IPacketProcessor<PingPacket>,
    IPacketProcessor<OutdatedClientPacket>,
    IPacketProcessor<AddStatusLinePacket>,
    IPacketProcessor<ClearStatusLinePacket>,
    IPacketProcessor<ClearStatusPacket>
{
    private bool _outdatedClientHandled;

    public void Process(OnlinePacket packet) =>
        statusModel.SetOnline((int)packet.Players, (int)packet.Programmator);

    public void Process(PingPacket packet)
    {
        statusModel.SetPing(packet.PreviousPing);
        networkService.Send(new PongPacket(packet.SentAt));
    }

    public void Process(OutdatedClientPacket packet)
    {
        if (_outdatedClientHandled)
        {
            return;
        }

        _outdatedClientHandled = true;

        // Description приходит от сервера свободным текстом; известные
        // клиентские причины передаются ключами словаря — резолвим их.
        string description = loc != null && loc.HasKey(packet.Description)
            ? loc.Get(packet.Description)
            : packet.Description;
        string detail = loc != null
            ? loc.Get("network.error.outdated", packet.Name, description, packet.UpdateURL)
            : $"Версия: {packet.Name}\n{description}\nСкачать: {packet.UpdateURL}";
        Debug.LogWarning($"[StatusProcessor] Клиент устарел: {detail}");
        if (!string.IsNullOrWhiteSpace(packet.UpdateURL))
        {
            Application.OpenURL(packet.UpdateURL);
        }
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

        stats.AddStatusLine(packet.Tag, packet.Text.ToArray(), unityColor, packet.BlinkRate, expiry);
    }

    public void Process(ClearStatusLinePacket packet) =>
        stats.RemoveStatusLine(packet.Tag);

    public void Process(ClearStatusPacket packet) =>
        stats.ClearStatusLines();
}
