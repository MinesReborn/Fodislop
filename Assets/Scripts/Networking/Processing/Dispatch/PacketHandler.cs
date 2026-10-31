#nullable enable

using Kern.Core.Platform;

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer.Unity;


namespace Kern.Networking.Processing;

// Чистый сервис контейнера (docs/architecture/SCENE_STANDARD.md §1): подписка на пакеты при
// старте scope, отписка при его уничтожении.
public sealed class PacketHandler : IStartable, IDisposable
{
    private readonly INetworkService _networkService;
    private readonly WorldInitProcessor _worldInit;
    private readonly PlayerInfoProcessor _playerInfo;
    private readonly WindowPacketProcessor _windowProcessor;
    private readonly MapRegionProcessor _mapRegion;
    private readonly BuildingProcessor _building;
    private readonly PlayerStatsProcessor _playerStats;
    private readonly ChatProcessor _chat;
    private readonly StatusProcessor _status;
    private readonly AudioPacketProcessor _audio;
    private readonly VisualPacketProcessor _visual;
    private readonly InventoryProcessor _inventory;
    private readonly ClanProcessor _clan;
    private readonly MissionProcessor _mission;
    private readonly MissionArrowProcessor _missionArrow;
    private readonly ConnectionProcessor _connection;
    private readonly AuthTokenProcessor _authToken;
    private readonly ClientConfigProcessor _clientConfig;
    private readonly List<Action> _unsubscribers = new();
    private readonly IBatchAwareProcessor[] _batchAwareProcessors;

    public PacketHandler(
        INetworkService networkService,
        WorldInitProcessor worldInit,
        PlayerInfoProcessor playerInfo,
        WindowPacketProcessor windowProcessor,
        MapRegionProcessor mapRegion,
        BuildingProcessor building,
        PlayerStatsProcessor playerStats,
        ChatProcessor chat,
        StatusProcessor status,
        AudioPacketProcessor audio,
        VisualPacketProcessor visual,
        InventoryProcessor inventory,
        ClanProcessor clan,
        MissionProcessor mission,
        MissionArrowProcessor missionArrow,
        ConnectionProcessor connection,
        AuthTokenProcessor authToken,
        ClientConfigProcessor clientConfig)
    {
        _networkService = networkService;
        _worldInit = worldInit;
        _playerInfo = playerInfo;
        _windowProcessor = windowProcessor;
        _mapRegion = mapRegion;
        _building = building;
        _playerStats = playerStats;
        _chat = chat;
        _status = status;
        _audio = audio;
        _visual = visual;
        _inventory = inventory;
        _clan = clan;
        _mission = mission;
        _missionArrow = missionArrow;
        _connection = connection;
        _authToken = authToken;
        _clientConfig = clientConfig;
        _batchAwareProcessors = new object[]
        {
            worldInit,
            playerInfo,
            windowProcessor,
            mapRegion,
            building,
            playerStats,
            chat,
            status,
            audio,
            visual,
            inventory,
            clan,
            mission,
            missionArrow,
            connection,
            authToken,
            clientConfig,
        }.OfType<IBatchAwareProcessor>().ToArray();
    }

    public bool IsSubscribed { get; private set; }

    void IStartable.Start() => Subscribe();

    // Вызывается конвейером старта: подписка идемпотентна.
    public void EnsureInitialized() => Subscribe();

    public void Shutdown() => Unsubscribe();

    public void Dispose() => Unsubscribe();

    private void BeginPacketBatch()
    {
        for (int i = 0; i < _batchAwareProcessors.Length; i++)
        {
            _batchAwareProcessors[i].BeginBatch();
        }
    }

    private void EndPacketBatch()
    {
        for (int i = 0; i < _batchAwareProcessors.Length; i++)
        {
            _batchAwareProcessors[i].EndBatch();
        }
    }

    // Protocol packets may be value types, so this helper must remain unconstrained.
    private void On<T>(Action<T> handler)
    {
        _networkService.Subscribe(handler);
        _unsubscribers.Add(() => _networkService.Unsubscribe(handler));
    }

    private void Subscribe()
    {
        if (IsSubscribed)
        {
            return;
        }

        _networkService.PacketBatchStarted += BeginPacketBatch;
        _networkService.PacketBatchCompleted += EndPacketBatch;
        _unsubscribers.Add(() => _networkService.PacketBatchStarted -= BeginPacketBatch);
        _unsubscribers.Add(() => _networkService.PacketBatchCompleted -= EndPacketBatch);

        On<WorldInitPacket>(_worldInit.Process);
        On<RobotInfoPacket>(_playerInfo.Process);
        On<PlayerInfoPacket>(_playerInfo.Process);
        On<MovementSpeedPacket>(_playerInfo.Process);
        On<OpenWindowPacket>(_windowProcessor.Process);
        On<CloseWindowPacket>(_windowProcessor.Process);
        On<RobotPositionPacket>(_playerInfo.Process);
        On<MapRegionPacket>(_mapRegion.Process);
        On<BuildingPacket>(_building.Process);
        On<RemoveBuildingPacket>(_building.Process);

        On<LevelPacket>(_playerStats.Process);
        On<HealthPacket>(_playerStats.Process);
        On<CurrencyPacket>(_playerStats.Process);
        On<GeologyPacket>(_playerStats.Process);
        On<BasketPacket>(_playerStats.Process);
        On<MaxDepthPacket>(_playerStats.Process);

        On<AutoMineStatePacket>(_playerInfo.Process);
        On<SkillProgressPacket>(_playerStats.Process);
        On<DailyBonusStatePacket>(_playerStats.Process);
        On<TeleportPacket>(_playerInfo.Process);
        On<ChatMessageListPacket>(_chat.Process);
        On<LocalChatMessagePacket>(_chat.Process);
        On<ChatMutePacket>(_chat.Process);
        On<ChatListPacket>(_chat.Process);

        On<OnlinePacket>(_status.Process);
        On<PingPacket>(_status.Process);
        On<OutdatedClientPacket>(_status.Process);
        On<AudioPacket>(_audio.Process);
        On<VisualPacket>(_visual.Process);
        On<InventoryPacket>(_inventory.Process);
        On<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(_inventory.Process);
        On<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(_inventory.Process);
        On<AddStatusLinePacket>(_status.Process);
        On<ClearStatusLinePacket>(_status.Process);
        On<ClearStatusPacket>(_status.Process);
        On<ModalWindowPacket>(_windowProcessor.Process);
        On<ShowClanPacket>(_clan.Process);
        On<HideClanPacket>(_clan.Process);
        On<MissionInitPacket>(_mission.Process);
        On<MissionProgressPacket>(_mission.Process);
        On<DisconnectPacket>(_connection.Process);
        On<ReconnectPacket>(_connection.Process);
        On<AuthTokenPacket>(_authToken.Process);
        On<OpenURLPacket>(packet => ExternalUrlOpener.TryOpen(packet.URL, nameof(PacketHandler)));
        On<MissionArrowPacket>(_missionArrow.Process);
        On<ClientConfigPacket>(_clientConfig.Process);

        IsSubscribed = true;
    }

    private void Unsubscribe()
    {
        foreach (Action unsubscribe in _unsubscribers)
        {
            unsubscribe();
        }

        _unsubscribers.Clear();
        IsSubscribed = false;
    }
}
