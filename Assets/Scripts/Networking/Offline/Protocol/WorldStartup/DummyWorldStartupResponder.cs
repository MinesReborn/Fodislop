#nullable enable

using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.AssetPipeline.Loading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Contracts.Packets;

namespace Kern.Networking.Offline;

internal sealed class DummyWorldStartupResponder
{
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IDummyClock _clock;
    private readonly IItemCatalog _itemCatalog;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyBuffManager _buffManager;
    private readonly DummyChatSimulator _chatSimulator;
    private readonly DummyInventoryResponder _inventoryResponder;
    private readonly DummyMissionRunner _missionRunner;
    private readonly List<(ushort X, ushort Y)> _teleportPositions;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Func<int, bool> _loopAlive;
    private readonly DummyBlockSpawner _blockSpawner;
    private readonly ITextureStorageService _textureStorage;
    private readonly IRuntimeAssetPaths _assetPaths;
    private readonly DummyGameplayActionResponder _actionResponder;
    internal DummyWorldStartupResponder(
    IAsyncOperationSupervisor operations,
    IDummyClock clock,
    IItemCatalog itemCatalog,
    DummyWorldSimulationState worldState,
    DummyPlayerSimulationState playerState,
    DummyBuffManager buffManager,
    DummyChatSimulator chatSimulator,
    DummyInventoryResponder inventoryResponder,
    DummyMissionRunner missionRunner,
    List<(ushort X, ushort Y)> teleportPositions,
    Action<ServerPacket> sendPacket,
    Func<int, bool> loopAlive,
    DummyBlockSpawner blockSpawner,
    DummyGameplayActionResponder actionResponder,
    ITextureStorageService textureStorage,
    IRuntimeAssetPaths assetPaths)
    {
        _operations = operations;
        _clock = clock;
        _itemCatalog = itemCatalog;
        _worldState = worldState;
        _playerState = playerState;
        _buffManager = buffManager;
        _chatSimulator = chatSimulator;
        _inventoryResponder = inventoryResponder;
        _missionRunner = missionRunner;
        _teleportPositions = teleportPositions;
        _sendPacket = sendPacket;
        _loopAlive = loopAlive;
        _blockSpawner = blockSpawner;
        _textureStorage = textureStorage;
        _assetPaths = assetPaths;
        _actionResponder = actionResponder;
    }


    public async UniTask InitializeAsync(
        string worldCodeName,
        int lifecycleVersion,
        string playerName,
        long level,
        long currency,
        ushort playerBotId)
    {
        DummyWorldDescriptor world = await _worldState.OpenAsync(worldCodeName);
        await SendWorldIdentityAsync(worldCodeName, world, playerName, playerBotId);
        SendClientConfig();

        _playerState.SetPosition(25, 50);
        StartBotSimulation(lifecycleVersion);
        _sendPacket(new ServerPacket(new AutoMineStatePacket(false)));
        _sendPacket(new ServerPacket(new DailyBonusStatePacket(false)));
        _buffManager.ResetDailyBonus();
        _sendPacket(new ServerPacket(new CurrencyPacket(currency, 1234)));
        _playerState.SetHealth(250);
        _sendPacket(new ServerPacket(new HealthPacket(250, 500)));
        long[] basketContents = _playerState.ResetBasket();
        _sendPacket(new ServerPacket(new BasketPacket(50000, basketContents)));
        _sendPacket(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
        _sendPacket(new ServerPacket(new LevelPacket(level)));
        await _worldState.SendChunksAroundAsync(_playerState.X, _playerState.Y, _sendPacket);
        _blockSpawner.Start(lifecycleVersion, world);

        SendSkillProgress();
        _chatSimulator.SendChatMock(lifecycleVersion);
        StartStatusSimulation(lifecycleVersion);

        _sendPacket(new ServerPacket(
            new MovementSpeedPacket(
                DummyCellConfigurationFactory.CreateMovementSpeeds())));

        Dictionary<ItemType, long> inventory = CreateInitialInventory(_itemCatalog);
        _inventoryResponder.ReplaceItems(inventory);
        _sendPacket(new ServerPacket(new InventoryPacket(inventory)));

        var placeholder = new ChatMessagePacket(
            0,
            0,
            0,
            0,
            System.Drawing.Color.White,
            string.Empty,
            System.Drawing.Color.White,
            string.Empty);
        _sendPacket(new ServerPacket(new ChatListPacket(
            new[] { (ChatProtocolLimits.GlobalChannelTag, "Global", placeholder) })));
        SendTestPacks();
        _operations.Run(
            "dummy_gun_loop",
            cancellationToken => RunGunAsync(lifecycleVersion, playerBotId, cancellationToken));
        _missionRunner.StartPersistentMission(_playerState.X, _playerState.Y);
        SendWorldMusic();
    }

    private void SendWorldMusic()
    {
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new AudioPacket(
                AudioEventType.Music,
                0,
                _playerState.X,
                _playerState.Y,
                Array.Empty<StringPairPacket>()),
        })));
    }

    internal static Dictionary<ItemType, long> CreateInitialInventory(IItemCatalog _itemCatalog)
    {
        var inventory = new Dictionary<ItemType, long>();
        foreach (ItemType type in _itemCatalog.AllTypes)
        {
            inventory[type] = 1;
        }

        inventory[ItemType.Battery] = 2;
        return inventory;
    }

    private async UniTask SendWorldIdentityAsync(
        string worldCodeName,
        DummyWorldDescriptor world,
        string playerName,
        ushort playerBotId)
    {
        CellConfigurationPacket[] cellConfigurations = (CellConfigurationPacket[])world.CellConfigurations.Clone();
        WorldAssetDefinitionPacket[] assetManifest =
            await DummyCellConfigurationFactory.CreateAssetManifestAsync(
                _textureStorage,
                _assetPaths,
                cellConfigurations);
        _sendPacket(new ServerPacket(new WorldInitPacket(
            worldCodeName,
            "Pallada",
            (ushort)world.Width,
            (ushort)world.Height,
            cellConfigurations,
            DummyCellConfigurationFactory.CreateTileGroups(),
            assetManifest)));
        _sendPacket(new ServerPacket(new PlayerInfoPacket(999, playerBotId, playerName)));
        _sendPacket(new ServerPacket(new RobotInfoPacket(
            playerBotId,
            999,
            1,
            "Visual/Skin/bee.png",
            "Visual/Tail/default.png",
            string.Empty)));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new RobotPositionPacket(playerBotId, 25, 50, 0),
        })));
    }

    private void StartBotSimulation(int lifecycleVersion)
    {
        _operations.Run(
            "dummy_bot_loop",
            cancellationToken => DummyBotRunner.RunCircularBots(
                6,
                lifecycleVersion,
                _clock,
                _sendPacket,
                () => _loopAlive(lifecycleVersion),
                () => (_playerState.X, _playerState.Y),
                cancellationToken));
    }

    private void StartStatusSimulation(int lifecycleVersion)
    {
        _sendPacket(new ServerPacket(new OnlinePacket(42, 3)));
        _sendPacket(new ServerPacket(default(ClearStatusPacket)));
        _buffManager.SendStatusPackets();
        _buffManager.StartBuffLoop(lifecycleVersion);
        _operations.Run("dummy_ping_loop", cancellationToken => SendPingLoopAsync(lifecycleVersion, cancellationToken));
        _operations.Run("dummy_online_loop", cancellationToken => SendOnlineLoopAsync(lifecycleVersion, cancellationToken));
        _buffManager.StartDailyBonusLoop(lifecycleVersion);
    }

    private void SendSkillProgress()
    {
        (SkillType Type, long Current, long Max)[] skills =
        {
            (SkillType.MineGeneral, 75, 100),
            (SkillType.Extraction, 120, 100),
            (SkillType.Health, 40, 100),
            (SkillType.Movement, 10, 100),
        };
        foreach ((SkillType type, long current, long max) in skills)
        {
            _sendPacket(new ServerPacket(new SkillProgressPacket(type, current, max)));
        }
    }

    private const ushort GunX = 34;
    private const ushort GunY = 50;
    private const int GunRadiusCells = 20;
    private const int GunShotIntervalMilliseconds = 500;

    private async UniTask RunGunAsync(
        int lifecycleVersion,
        ushort playerBotId,
        CancellationToken cancellationToken)
    {
        while (_loopAlive(lifecycleVersion))
        {
            await _clock.Delay(GunShotIntervalMilliseconds, cancellationToken);
            if (!_loopAlive(lifecycleVersion))
            {
                return;
            }

            int deltaX = _playerState.X - GunX;
            int deltaY = _playerState.Y - GunY;
            if ((deltaX * deltaX) + (deltaY * deltaY) > GunRadiusCells * GunRadiusCells)
            {
                continue;
            }

            // Match PlayerEntity.SendEffects: the position is the gun, the bot is its target.
            _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
            {
                new VisualPacket(VisualEffect.GunShot, playerBotId, GunX, GunY, Array.Empty<StringPairPacket>()),
                new AudioPacket(AudioEventType.GunShot, playerBotId, GunX, GunY, Array.Empty<StringPairPacket>()),
            })));
            _actionResponder.HandleGunHit();
        }
    }

    private void SendTestPacks()
    {
        _teleportPositions.Clear();
        _teleportPositions.Add((27, 50));
        _teleportPositions.Add((227, 50));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new BuildingPacket(27, 50, BuildingType.Teleport, 0, 1),
            new BuildingPacket(227, 50, BuildingType.Teleport, 0, 1),
            new BuildingPacket(25, 48, BuildingType.Market, 0, 0),
            new BuildingPacket(GunX, GunY, BuildingType.Gun, 0, 0),
        })));
    }

    private async UniTask SendPingLoopAsync(int lifecycleVersion, CancellationToken cancellationToken)
    {
        await _clock.Delay(2000, cancellationToken);
        while (_loopAlive(lifecycleVersion))
        {
            _sendPacket(new ServerPacket(new PingPacket(
                _clock.UtcNowTicks,
                _clock.Random.Next(15, 60))));
            await _clock.Delay(5000, cancellationToken);
        }
    }

    private async UniTask SendOnlineLoopAsync(int lifecycleVersion, CancellationToken cancellationToken)
    {
        await _clock.Delay(3000, cancellationToken);
        while (_loopAlive(lifecycleVersion))
        {
            ushort players = (ushort)(38 + _clock.Random.Next(0, 9));
            _sendPacket(new ServerPacket(new OnlinePacket(players, 3)));
            await _clock.Delay(12000, cancellationToken);
        }
    }

    private void SendClientConfig()
    {
        IReadOnlyList<string> textures = GetAvailableTextures();
        _sendPacket(new ServerPacket(new ClientConfigPacket(
            Array.Empty<StringPairPacket>(),
            textures)));
    }

    private IReadOnlyList<string> GetAvailableTextures()
    {
        return _assetPaths.GetTextureFilenames()
            .Where(AssetBatchDispatcher.IsTextureFile)
            .ToArray();
    }

}
