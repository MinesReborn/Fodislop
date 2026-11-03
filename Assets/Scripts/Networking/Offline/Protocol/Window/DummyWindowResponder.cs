#nullable enable

using System;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.Mission;

using UnityEngine;

namespace Kern.Networking.Offline;

internal sealed class DummyWindowResponder
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly DummyBuffManager _buffManager;
    private readonly DummyTeleportManager _teleportManager;
    private readonly DummyClanManager _clanManager;
    private readonly DummyMissionRunner _missionRunner;
    private readonly DummyBlockSpawner _blockSpawner;
    internal DummyWindowResponder(
    Action<ServerPacket> sendPacket,
    DummyBuffManager buffManager,
    DummyTeleportManager teleportManager,
    DummyClanManager clanManager,
    DummyMissionRunner missionRunner,
    DummyBlockSpawner blockSpawner)
    {
        _sendPacket = sendPacket;
        _buffManager = buffManager;
        _teleportManager = teleportManager;
        _clanManager = clanManager;
        _missionRunner = missionRunner;
        _blockSpawner = blockSpawner;
    }


    public void Handle(ElementClickPacket packet, ushort playerX, ushort playerY)
    {
        switch (packet.WindowTag)
        {
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
            case "donate":
                _sendPacket(new ServerPacket(new ModalWindowPacket(
                    "Донат недоступен",
                    "Пополнение баланса доступно только при подключении к серверу.",
                    "Понятно",
                    string.Empty)));
                break;
            case "toggle_block_spawner":
                _blockSpawner.Enabled = !_blockSpawner.Enabled;
                Debug.Log($"[DummyWindowResponder] Спавнер блоков: {(_blockSpawner.Enabled ? "включён" : "выключен")}.");
                break;
            case "test_mission_arrow":
                _sendPacket(new ServerPacket(new MissionInitPacket(
                    string.Empty,
                    0,
                    0,
                    "Тестовая миссия",
                    "Проверка центрального информационного кольца")));
                _sendPacket(new ServerPacket(new MissionProgressPacket(0, 10)));
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

        if (_teleportManager.IsCloseElement(packet.ElementIndex))
        {
            _teleportManager.WindowOpen = false;
            _sendPacket(new ServerPacket(new CloseWindowPacket()));
            return;
        }

        _teleportManager.HandleTeleportClick(_teleportManager.DestinationOfElement(packet.ElementIndex));
    }

    private void HandleMission(ElementClickPacket packet, ushort playerX, ushort playerY)
    {
        if (_missionRunner.IsCloseElement(packet.ElementIndex))
        {
            _sendPacket(new ServerPacket(new CloseWindowPacket()));
        }
        else if (_missionRunner.IsCancelElement(packet.ElementIndex))
        {
            _missionRunner.CancelMission();
        }
        else
        {
            _missionRunner.StartMission(_missionRunner.MissionOfElement(packet.ElementIndex), playerX, playerY);
        }
    }
}
