#nullable enable

using System;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;

using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWindowResponder
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly DummyBuffManager _buffManager;
    private readonly DummyInventoryResponder _inventoryResponder;
    private readonly DummyTeleportManager _teleportManager;
    private readonly DummyClanManager _clanManager;
    private readonly DummyMissionRunner _missionRunner;

    public DummyWindowResponder(
        Action<ServerPacket> sendPacket,
        DummyBuffManager buffManager,
        DummyInventoryResponder inventoryResponder,
        DummyTeleportManager teleportManager,
        DummyClanManager clanManager,
        DummyMissionRunner missionRunner)
    {
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
        _buffManager = buffManager ?? throw new ArgumentNullException(nameof(buffManager));
        _inventoryResponder = inventoryResponder ??
            throw new ArgumentNullException(nameof(inventoryResponder));
        _teleportManager = teleportManager ??
            throw new ArgumentNullException(nameof(teleportManager));
        _clanManager = clanManager ?? throw new ArgumentNullException(nameof(clanManager));
        _missionRunner = missionRunner ?? throw new ArgumentNullException(nameof(missionRunner));
    }

    public void Handle(ElementClickPacket packet, ushort playerX, ushort playerY)
    {
        switch (packet.WindowTag)
        {
            case "daily_bonus":
                _buffManager.HandleDailyBonusClaim(_inventoryResponder.Items);
                break;
            case "teleport":
                HandleTeleport(packet);
                break;
            case "test_modal":
                _sendPacket(DummyWindowBuilder.BuildTestModalWindow());
                break;
            case "join_clan":
            case "leave_clan":
            case "clan_list":
            case "clan_info":
                _clanManager.HandleElementClick(packet);
                break;
            case "open_missions":
                _missionRunner.SendMissionWindow(playerX, playerY);
                break;
            case "missions":
                HandleMission(packet, playerX, playerY);
                break;
            case "open_url_test":
                _sendPacket(DummyWindowBuilder.BuildOpenUrlPacket("https://vk.ru/mines4reborn"));
                break;
            case "test_mission_arrow":
                _sendPacket(DummyWindowBuilder.BuildTestMissionArrowPacket(playerX, playerY));
                break;
            default:
                // Без этой ветки нажатие в незнакомом окне не делало ничего и
                // ничего не сообщало: окно просто не открывалось, и причину
                // приходилось искать глазами по коду.
                Debug.LogError(
                    $"[DummyWindowResponder] Окно '{packet.WindowTag}' не обработано: " +
                    "добавьте ветку сюда либо уберите тег на клиенте.");
                break;
        }
    }

    private void HandleTeleport(ElementClickPacket packet)
    {
        if (!_teleportManager.WindowOpen)
        {
            return;
        }

        if (packet.ElementIndex == 0)
        {
            _teleportManager.WindowOpen = false;
            _sendPacket(new ServerPacket(new CloseWindowPacket()));
            return;
        }

        _teleportManager.HandleTeleportClick(packet.ElementIndex - 1);
    }

    private void HandleMission(ElementClickPacket packet, ushort playerX, ushort playerY)
    {
        if (packet.ElementIndex == 0)
        {
            _sendPacket(new ServerPacket(new CloseWindowPacket()));
        }
        else if (packet.ElementIndex <= _missionRunner.MissionCount)
        {
            _missionRunner.StartMission(packet.ElementIndex - 1, playerX, playerY);
        }
        else
        {
            _missionRunner.CancelMission();
        }
    }
}
