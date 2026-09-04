#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Core.Interfaces;
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

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWorldStartupResponder
{
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IItemCatalog _itemCatalog;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyBuffManager _buffManager;
    private readonly DummyChatSimulator _chatSimulator;
    private readonly DummyInventoryResponder _inventoryResponder;
    private readonly List<(ushort X, ushort Y)> _teleportPositions;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Func<int, bool> _loopAlive;
    private static readonly System.Random Rng = new();

    public DummyWorldStartupResponder(
        IAsyncOperationSupervisor operations,
        IItemCatalog itemCatalog,
        DummyWorldSimulationState worldState,
        DummyPlayerSimulationState playerState,
        DummyBuffManager buffManager,
        DummyChatSimulator chatSimulator,
        DummyInventoryResponder inventoryResponder,
        List<(ushort X, ushort Y)> teleportPositions,
        Action<ServerPacket> sendPacket,
        Func<int, bool> loopAlive)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _itemCatalog = itemCatalog ?? throw new ArgumentNullException(nameof(itemCatalog));
        _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
        _playerState = playerState ?? throw new ArgumentNullException(nameof(playerState));
        _buffManager = buffManager ?? throw new ArgumentNullException(nameof(buffManager));
        _chatSimulator = chatSimulator ?? throw new ArgumentNullException(nameof(chatSimulator));
        _inventoryResponder = inventoryResponder ??
            throw new ArgumentNullException(nameof(inventoryResponder));
        _teleportPositions = teleportPositions ??
            throw new ArgumentNullException(nameof(teleportPositions));
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
        _loopAlive = loopAlive ?? throw new ArgumentNullException(nameof(loopAlive));
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
        SendWorldIdentity(worldCodeName, world, playerName, playerBotId);
        StartBotSimulation(lifecycleVersion);

        _playerState.SetPosition(25, 50);
        _sendPacket(new ServerPacket(new AggressionStatePacket(false)));
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
        _worldState.SendChunksAround(_playerState.X, _playerState.Y, _sendPacket);

        SendSkillProgress();
        _chatSimulator.SendChatMock();
        StartStatusSimulation(lifecycleVersion);

        _sendPacket(new ServerPacket(
            new MovementSpeedPacket(
                DummyCellConfigurationUtilities.CreateMovementSpeeds(
                    world.CellConfigurations))));

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
            new[] { ("global", "Global", placeholder) })));
        SendTestPacks();
        SendWorldMusic();
    }

    /// <summary>
    /// Заказывает клиенту музыку при входе в мир.
    /// </summary>
    /// <remarks>
    /// В протоколе музыка — это обычный <c>AudioPacket</c> со значением
    /// <see cref="SFX.Music"/>, оно так и подписано в перечислении. Клиент
    /// разбирал его и раньше: <c>ServerAudioEventManager.PlayEffect</c>
    /// уводит эту ветку в <c>Play2D</c> на шину музыки, минуя пул VFX —
    /// у трека нет ни позиции, ни визуального представления. Не хватало
    /// только отправителя: заглушка не слала пакет, поэтому шина музыки
    /// молчала всю игру, хотя громкость для неё была и в конфиге, и в меню.
    ///
    /// Координаты для музыки роли не играют, но пакет обязан быть
    /// осмысленным, поэтому берётся позиция игрока. Целевой бот нулевой:
    /// трек ни к кому не привязан.
    /// </remarks>
    private void SendWorldMusic()
    {
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new AudioPacket(
                SFX.Music,
                0,
                _playerState.X,
                _playerState.Y,
                Array.Empty<StringPairPacket>()),
        })));
    }

    internal static Dictionary<ItemType, long> CreateInitialInventory(IItemCatalog itemCatalog)
    {
        var inventory = new Dictionary<ItemType, long>();
        foreach (ItemType type in itemCatalog.AllTypes)
        {
            inventory[type] = 1;
        }

        inventory[ItemType.Battery] = 2;
        return inventory;
    }

    private void SendWorldIdentity(
        string worldCodeName,
        DummyWorldDescriptor world,
        string playerName,
        ushort playerBotId)
    {
        _sendPacket(new ServerPacket(new WorldInitPacket(
            worldCodeName,
            "Pallada",
            (ushort)world.Width,
            (ushort)world.Height,
            world.CellConfigurations,
            new byte[][]
            {
                new byte[] { 37, 38, 106 },
            })));
        _sendPacket(new ServerPacket(new PlayerInfoPacket(999, playerBotId, playerName)));
        _sendPacket(new ServerPacket(new RobotInfoPacket(
            playerBotId,
            999,
            1,
            "Skin/bee.png",
            "Tail/default.png",
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
            _ => DummyBotRunner.RunCircularBots(
                6,
                lifecycleVersion,
                _sendPacket,
                () => _loopAlive(lifecycleVersion)));
    }

    private void StartStatusSimulation(int lifecycleVersion)
    {
        _sendPacket(new ServerPacket(new OnlinePacket(42, 3)));
        _sendPacket(new ServerPacket(default(ClearStatusPacket)));
        _buffManager.SendStatusPackets();
        _buffManager.StartBuffLoop(lifecycleVersion);
        _operations.Run("dummy_ping_loop", _ => SendPingLoopAsync(lifecycleVersion));
        _operations.Run("dummy_online_loop", _ => SendOnlineLoopAsync(lifecycleVersion));
        _buffManager.StartDailyBonusLoop(lifecycleVersion);
    }

    private void SendSkillProgress()
    {
        var skills = new (SkillType Type, long Current, long Max)[]
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

    private void SendTestPacks()
    {
        _teleportPositions.Clear();
        _teleportPositions.Add((27, 50));
        _teleportPositions.Add((227, 50));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new PackPacket(27, 50, PackType.Teleport, 0, 1),
            new PackPacket(227, 50, PackType.Teleport, 0, 1),
            new PackPacket(25, 48, PackType.Market, 0, 0),
        })));
    }

    private async UniTask SendPingLoopAsync(int lifecycleVersion)
    {
        await UniTask.Delay(2000);
        while (_loopAlive(lifecycleVersion))
        {
            _sendPacket(new ServerPacket(new PingPacket(
                DateTimeOffset.UtcNow.Ticks,
                Rng.Next(15, 60))));
            await UniTask.Delay(5000);
        }
    }

    private async UniTask SendOnlineLoopAsync(int lifecycleVersion)
    {
        await UniTask.Delay(3000);
        while (_loopAlive(lifecycleVersion))
        {
            ushort players = (ushort)(38 + Rng.Next(0, 9));
            _sendPacket(new ServerPacket(new OnlinePacket(players, 3)));
            await UniTask.Delay(12000);
        }
    }
}
