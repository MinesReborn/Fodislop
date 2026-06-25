using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.CompilerServices;
using Fodinae.Scripts.Audio;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.UI;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UI;
using MinesServer.Networking.Server.Packets.Inventory;



namespace MinesServer.Networking.Connection.Client
{
    public class DummyConnection : IServerConnection
    {
        private ConnectionStatus _status = ConnectionStatus.Disconnected;

        public ConnectionStatus ConnectionStatus => _status;

        public event Action<ServerPacket> OnReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnDisconnecting;
        public event Action OnConnecting;

        private const ushort _mockBotId = 456;
        private ushort _x = 0;
        private ushort _y = 0;
        private Direction _rot = Direction.Up;
        private ItemType _selectedItemType;
        private FPSCounter _fpsCounter;

        private static readonly CellType[] _allCellTypes = new CellType[]
        {
            CellType.Unloaded, CellType.Pregener,
            CellType.BuildingRoad, CellType.Gate, CellType.VolcanoBackground,
            CellType.BackgroundWithLightTraces, CellType.BackgroundWithHeavyTraces,
            CellType.Road, CellType.GoldenRoad, CellType.BuildingDoor, CellType.BuildingCorner, CellType.PolymerRoad,
            CellType.BlackBoulder1, CellType.BlackBoulder2, CellType.BlackBoulder3,
            CellType.MetalBoulder1, CellType.MetalBoulder2, CellType.MetalBoulder3,
            CellType.QuadBlock, CellType.Support,
            CellType.AliveCyan, CellType.AliveRed, CellType.AliveViol, CellType.AliveNigger, CellType.AliveWhite, CellType.AliveRainbow,
            CellType.WhiteSand, CellType.DarkWhiteSand, CellType.RustySand, CellType.DarkRustySand,
            CellType.BlackSand, CellType.DarkBlackSand, CellType.GrayAcid, CellType.PurpleAcid,
            CellType.Pearl, CellType.DeepLazuriteSand, CellType.DeepMagmaBoulder,
            CellType.XGreen, CellType.XBlue, CellType.XRed, CellType.XCyan, CellType.XViolet,
            CellType.DeepObsidianRock, CellType.DeepTurquoiseRock, CellType.DeepRainbowRock, CellType.DeepStripedRock,
            CellType.MilitaryBlockFrame, CellType.MilitaryBlock, CellType.MilitaryBlockSand, CellType.TeleportBlock,
            CellType.PassiveAcid, CellType.SuperRainbow, CellType.Skull, CellType.Box,
            CellType.Lava, CellType.Boulder1, CellType.Boulder2, CellType.Boulder3,
            CellType.LivingActiveAcid, CellType.CorrosiveActiveAcid,
            CellType.BlueSand, CellType.DarkBlueSand, CellType.YellowSand, CellType.DarkYellowSand,
            CellType.GreenBlock, CellType.YellowBlock, CellType.Rock, CellType.FedBlock, CellType.RedBlock, CellType.BuildingWall,
            CellType.Green, CellType.Red, CellType.Blue, CellType.Violet, CellType.White, CellType.Cyan,
            CellType.HeavyRock, CellType.NiggerRock, CellType.LivingBlackRock,
            CellType.AliveBlue, CellType.RedRock, CellType.AcidRock, CellType.HypnoRock,
            CellType.GoldenRock, CellType.DeepRock, CellType.GRock
        };

        public void Connect()
        {
            if (_status != ConnectionStatus.Disconnected)
                return;

            _status = ConnectionStatus.Connecting;
            OnConnecting?.Invoke();

            // Run asynchronously, but stay on the Unity Main Thread
            ConnectAsync().Forget();
        }

        private async UniTaskVoid ConnectAsync()
        {
            await UniTask.Delay(100);

            CreateFPSCounter();

            _status = ConnectionStatus.Connected;
            OnConnected?.Invoke();

            var minimapObj = new GameObject("MinimapRoot");
            minimapObj.AddComponent<MinimapPlaceholder>();

            var inventoryObj = new GameObject("InventoryRoot");
            inventoryObj.AddComponent<InventoryUI>();

            var hudObj = new GameObject("PlayerHUD");
            hudObj.AddComponent<PlayerStatsModel>();
            hudObj.AddComponent<PlayerHUD>();

            var pauseObj = new GameObject("PauseMenu");
            pauseObj.AddComponent<PauseMenu>();

            var audioObj = new GameObject("AudioManager");
            audioObj.AddComponent<AudioManager>();

            var chatObj = new GameObject("ChatSystem");
            chatObj.AddComponent<LocalChatPopup>();
            chatObj.AddComponent<GlobalChatUI>();
            chatObj.AddComponent<FloatingChatManager>();
        }

        private void CreateFPSCounter()
        {
            GameObject fpsObject = new GameObject("FPSCounter");
            _fpsCounter = fpsObject.AddComponent<FPSCounter>();
            UnityEngine.Object.DontDestroyOnLoad(fpsObject);
        }

        public void Disconnect()
        {
            if (_status != ConnectionStatus.Connected)
                return;
            _status = ConnectionStatus.Disconnecting;
            OnDisconnecting?.Invoke();
            DisconnectAsync().Forget();
        }

        private async UniTaskVoid DisconnectAsync()
        {
            await UniTask.Delay(100);
            _status = ConnectionStatus.Disconnected;
            OnDisconnected?.Invoke();
            if (_fpsCounter != null)
            {
                UnityEngine.Object.Destroy(_fpsCounter.gameObject);
                _fpsCounter = null;
            }
        }

        private async UniTaskVoid UpdatePosition() {
            await UniTask.Delay(200);
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { new RobotPositionPacket(_mockBotId, _x, _y, (byte)_rot) })));
        }

        public void Dispose()
        {
            if (_fpsCounter != null)
            {
                UnityEngine.Object.Destroy(_fpsCounter.gameObject);
                _fpsCounter = null;
            }
        }

        public void SendAsync(ClientPacket packet)
        {
            if (packet.Data is ActionClientPacket actionPacket)
            {
                Debug.Log($"[DummyConnection] Received ActionClientPacket: X={actionPacket.X}, Y={actionPacket.Y}, Payload={actionPacket.Payload.GetType().Name}");
                if (actionPacket.Payload is MovePacket move)
                {
                    Debug.Log($"  - Move to ({move.X}, {move.Y})");
                    _x = move.X;
                    _y = move.Y;
                    UpdatePosition().Forget();
                }
                else if (actionPacket.Payload is RotatePacket rotate)
                {
                    Debug.Log($"  - Rotate to {rotate.Direction}");
                    _rot = rotate.Direction;
                    UpdatePosition().Forget();
                }
                else if (actionPacket.Payload is UnmappedKeyPacket key)
                {
                    Debug.Log($"  - Unmapped Key: Code={key.Code}, Ctrl={key.Control}, Alt={key.Alt}, Shift={key.Shift}");
                }
                else if (actionPacket.Payload is BzPacket)
                {
                    ushort cellX = actionPacket.X;
                    ushort cellY = actionPacket.Y;
                    Debug.Log($"[DummyConnection] DIG at ({cellX}, {cellY})");

                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                    {
                        new SFXPacket(SFX.Bz, _mockBotId, cellX, cellY, Array.Empty<StringPairPacket>())
                    })));

                    if (MapStorage.Instance.CellLayer != null && MapStorage.Instance.IsReady)
                    {
                        var cellType = MapStorage.Instance.GetCell(cellX, cellY);
                        int crystalIdx = GetCrystalBasketIndex(cellType);
                        var cellConfig = MapManager.Instance.GetCellConfig(cellType);
                        bool isBreakable = ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Breakable);

                        if (!isBreakable)
                        {
                            Debug.Log($"[DummyConnection] Cell ({cellX}, {cellY}) = {cellType} is not breakable");
                            return;
                        }

                        MapStorage.Instance.SetCell(cellX, cellY, CellType.Empty);

                        if (crystalIdx >= 0)
                        {
                            var stats = PlayerStatsModel.Instance;
                            if (stats.BasketContents.Length > crystalIdx)
                            {
                                var newContents = new long[stats.BasketContents.Length];
                                Array.Copy(stats.BasketContents, newContents, newContents.Length);
                                newContents[crystalIdx] += UnityEngine.Random.Range(1, 101);
                                OnReceived?.Invoke(new ServerPacket(new BasketPacket(stats.BasketCapacity, newContents)));
                            }
                        }

                        OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                        {
                            new MapRegionPacket(cellX, cellY, 0, 0, new[] { CellType.Empty }),
                            new SFXPacket(SFX.Destroy, _mockBotId, cellX, cellY, Array.Empty<StringPairPacket>())
                        })));
                        Debug.Log($"[DummyConnection] Cell ({cellX}, {cellY}) broken → Empty");
                    }
                }

                else if (actionPacket.Payload is SuicidePacket)
                {
                    Debug.Log("[DummyConnection] Suicide / Respawn");
                    ushort spawnX = 25;
                    ushort spawnY = 50;
                    var effectX = _x;
                    var effectY = _y;
                    _x = spawnX;
                    _y = spawnY;
                    _rot = Direction.Up;

                    OnReceived?.Invoke(new ServerPacket(new TeleportPacket(spawnX, spawnY, false)));
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] {
                        new RobotPositionPacket(_mockBotId, spawnX, spawnY, (byte)_rot),
                        new SFXPacket(SFX.Death, _mockBotId, effectX, effectY, Array.Empty<StringPairPacket>())
                    })));
                }
                return;
            }

            switch (packet.Data)
            {
                case ClientHelloPacket clientHello:

                    if (clientHello.ClientVersion < 1)
                    {
                        OnReceived?.Invoke(new ServerPacket(new OutdatedClientPacket(
                            2, "Mines 3", "Ваша версия устарела. Скачайте новую!",
                            "https://minesgame.ru/download", "")));
                        return;
                    }

                    var cellConfigs = CreateTestCellConfigurations();
                    const int testWorldWidth = 500;
                    const int testWorldHeight = 500;
                    OnReceived?.Invoke(new ServerPacket(new WorldInitPacket(
                        "pallada",
                        "Pallada",
                        testWorldWidth,
                        testWorldHeight,
                        cellConfigs,
                        new byte[][] {
                            new byte[] { 37, 38, 106 }
                        })));
                    SendTestWorldMapData(testWorldWidth, testWorldHeight);
                    CreateCellTypeLabels(testWorldWidth, testWorldHeight);
                    OnReceived?.Invoke(new ServerPacket(new PlayerInfoPacket(999, _mockBotId, "Darkar25")));
                    var robotPos = new RobotPositionPacket(_mockBotId, 25, 50, 0);
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { robotPos })));
                    HandleRobotInfoMock(_mockBotId).Forget();
                    RunCircularBots(10).Forget();
                    //RunTilingTestLoop().Forget();
                    OnReceived?.Invoke(new ServerPacket(new AggressionStatePacket(false)));
                    OnReceived?.Invoke(new ServerPacket(new AutoMineStatePacket(false)));
                    OnReceived?.Invoke(new ServerPacket(new CurrencyPacket(123456, 1234)));
                    OnReceived?.Invoke(new ServerPacket(new HealthPacket(250, 500)));
                    OnReceived?.Invoke(new ServerPacket(new BasketPacket(50000, new[] { 0L, 0L, 0L, 0L, 0L, 0L })));
                    OnReceived?.Invoke(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
                    OnReceived?.Invoke(new ServerPacket(new LevelPacket(12345)));

                    SendSkillProgressMock().Forget();
                    SendChatMock().Forget();

                    OnReceived?.Invoke(new ServerPacket(new OnlinePacket(42, 3)));
                    SendPingMock().Forget();

                    OnReceived?.Invoke(new ServerPacket(new MovementSpeedPacket(new Dictionary<CellType, ushort>
                    {
                        [CellType.Empty] = 20,
                        [CellType.Road] = 100
                    })));

                    var inventoryData = new Dictionary<ItemType, long>();
                    foreach (var type in ItemRegistry.AllTypes)
                        inventoryData[type] = 1;
                    OnReceived?.Invoke(new ServerPacket(new InventoryPacket(inventoryData)));

                    var placeholderMsg = new ChatMessagePacket(0, 0, 0, 0,
                    System.Drawing.Color.White, "", System.Drawing.Color.White, "");
                    OnReceived?.Invoke(new ServerPacket(new ChatListPacket(new[] { ("global", "Global", placeholderMsg) })));

                    // Send test packs
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] {
                        new PackPacket(27, 50, PackType.Teleport, 0, 1),
                        new PackPacket(25, 48, PackType.Market, 0, 0)
                    })));
                    break;
                case RuntimeAssetRequestPacket runtimeAssets:
                    HandleAssetRequest(runtimeAssets).Forget();
                    break;
                case OpenHelpClickPacket:
                    SendMockWindow(false);
                    break;
                case OpenSettingsClickPacket:
                    SendMockWindow(true);
                    break;
                case SendLocalChatMessagePacket localMsg:
                    Debug.Log($"[DummyConnection] Local chat: {localMsg.Message}");
                    OnReceived?.Invoke(new ServerPacket(new LocalChatMessagePacket(_mockBotId, _x, _y, localMsg.Message)));
                    break;

                case SendChatMessagePacket globalMsg:
                    Debug.Log($"[DummyConnection] Global chat ({globalMsg.Tag}): {globalMsg.Message}");
                    var chatMsg = new ChatMessagePacket(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        999, 1,
                        System.Drawing.Color.FromArgb(255, 200, 180, 100),
                        "You",
                        System.Drawing.Color.White,
                        globalMsg.Message
                    );
                    OnReceived?.Invoke(new ServerPacket(new ChatMessageListPacket("global", new[] { chatMsg })));
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.SelectItemPacket selectItem:
                    Debug.Log($"[DummyConnection] SelectItem: {selectItem.Item}");
                    _selectedItemType = selectItem.Item;
                    OnReceived?.Invoke(new ServerPacket(GetItemInfoPacket(selectItem.Item)));
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.UseItemPacket:
                    Debug.Log($"[DummyConnection] UseItem: {_selectedItemType}");
                    HandleUseItem();
                    break;
                default:
                    Debug.Log($"[DummyConnection] Unhandled packet: {packet.Data.GetType().Name}");
                    break;
            }
        }

        private static MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket GetItemInfoPacket(ItemType item)
        {
            var (name, desc) = GetItemInfo(item);
            return new MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket(
                item, name, desc, 1, 1, 3, false, new BitArray(0));
        }

        private static (string name, string desc) GetItemInfo(ItemType i) => i switch
        {
            ItemType.Teleport => ("Телепорт", "Строительный пак, который позволяет игрокам телепортироваться на другой телепорт"),
            ItemType.Resp => ("Респаун", "Строительный пак, который позволяет возрождаться и ремонтировать робота"),
            ItemType.Up => ("UP", "Строительный пак, который позволяет игрокам устанавливать и прокачивать умения"),
            ItemType.Market => ("Маркет", "Строительный пак, который позволяет игрокам покупать и продавать кристаллы, а также обмениваться друг с другом"),
            ItemType.Clans => ("Кланс", "Строительный пак, который позволяет просмотреть список кланов и вступить в один из кланов"),
            ItemType.PlasmBomb => ("Плазменная бомба", "Предмет, который позволяет взорвать блоки в радиусе 3 клеток(Красноскал с 1% шансом)"),
            ItemType.ProtonBomb => ("Протонная бомба", "Предмет, который позволяет взорвать блоки 3х3 от центра(Красноскал с 100% шансом)"),
            ItemType.RazBomb => ("Бомба-разряд", "Предмет, который позволяет нанести урон игрокам (500 HP) и Строительным пакам(10 HP)"),
            ItemType.Cred => ("Кредиты", "Валюта, которая позволяет увеличивать слоты роботов, создавать кланы и покупать скины для роботов"),
            ItemType.Rem => ("Ремонтный бот", "Предмет, который позволяет полностью восстановить здоровье робота"),
            ItemType.Geopack => ("Геопак", "Предмет, который позволяет упаковать живой кристалл в инвентарь"),
            ItemType.GeoCyan => ("Голубая жива", "Живка, которая даёт плод голубыми кристаллами"),
            ItemType.GeoRed => ("Красная жива", "Живка, которая даёт плод красными кристаллами, если поблизости есть черноскал"),
            ItemType.GeoViolet => ("Фиолетовая жива", "Живка, которая даёт плод фиолетовыми кристаллами, если поблизости есть черноскал"),
            ItemType.GeoBlack => ("Чёрная жива", "Живка, которая даёт плод голубыми и красными кристаллами, если стоит вплотную к такой же живке"),
            ItemType.GeoWhite => ("Белая жива", "Живка, которая даёт плод белыми кристаллами, если сверху стоит магма"),
            ItemType.GeoBlue => ("Синяя жива", "Живка, которая даёт плод синими кристаллами, если есть место для передвижения живки"),
            ItemType.VulkanRadar => ("Радар вулканов", "Предмет, который позволяет обнаружить вулканы"),
            ItemType.AliveRadar => ("Радар живок", "Предмет, который позволяет обнаружить живые кристаллы в радиусе 200 блоков"),
            ItemType.RobotRadar => ("Радар роботов", "Предмет, который позволяет обнаружить роботов в радиусе 300 блоков"),
            ItemType.PortableTeleporter => ("ТПР", "Предмет, который позволяет игроку телепортироваться на Респаун без потери кристаллов"),
            ItemType.ConstructionBot => ("Конструкционный бот", "Предмет, увеличивающий вместимость кристаллов в строительных паках"),
            ItemType.Generator => ("Боевой Генератор", "Предмет, увеличивающий урон пушки"),
            ItemType.Charge => ("Заряд защиты", "Предмет, увеличивающий здоровье строительных паков"),
            ItemType.Craft => ("Крафт", "Строительный пак, в котором можно создать паки и предметы"),
            ItemType.BombShop => ("Магазин бомб", "Строительный пак, в котором продаются бомбы за кредиты"),
            ItemType.Gun => ("Клановая Пушка", "Строительный клановый пак, позволяющий защитить территорию клана"),
            ItemType.Gate => ("Ворота", "Строительный клановый пак, через который могут пройти только участники клана"),
            ItemType.Disassembler => ("Диззассемблер", "Предмет, позволяющий собрать строительный пак в инвентарь"),
            ItemType.Storage => ("Склад", "Строительный пак, в котором можно хранить кристаллы"),
            ItemType.Scanner => ("Сканер паков", "Предмет, при использовании которого показываются характеристики строительного пака"),
            ItemType.UpgradeBooster => ("Прокачка x3", "Предмет, который ускоряет прокачку в 3 раза (24ч)"),
            ItemType.FreeUp => ("Freeup", "Предмет, который увеличивает оптимизацию до 75% на прокачку (12ч)"),
            ItemType.MineBooster => ("Добыча x4", "Предмет, который увеличивает добычу кристалла в 4 раза (12ч)"),
            ItemType.GeoHypno => ("Гипноскал", "Блок, который защищает вместе с пушкой территорию клана"),
            ItemType.Poly => ("Полимер", "Компонент/Предмет используемый в крафтинге и при помощи которого можно строить полимерную дорогу"),
            ItemType.Nano => ("Нано бот", "Компонент/Предмет используемый в крафтинге и при помощи которого можно восстановить здоровье робота на 50 HP"),
            ItemType.Battery => ("Аккумулятор", "Компонент/Предмет используемый в крафтинге и при помощи которого можно увеличить скорость робота"),
            ItemType.Trans => ("Транслятор", "Компонент/Предмет используемый в крафтинге и при помощи которого можно между своими роботами переключаться и передавать кристаллы"),
            ItemType.Compressor => ("Компрессор", "Компонент/Предмет используемый в крафтинге"),
            ItemType.C190 => ("С-190", "Компонент/Предмет используемый в крафтинге и при помощи которого можно наносить урон другим игрокам"),
            ItemType.FED => ("Fed база", "Предмет, который позволяет ставить золотую дорогу"),
            ItemType.GeoBlackRock => ("Чёрная скала", "Предмет, который мгновенно ставит черноскал на пустоте"),
            ItemType.GeoRedRock => ("Красная скала", "Предмет, который мгновенно ставит красноскал на пустоте"),
            ItemType.Auto => ("Автоматизатор", "Предмет, который пополняет кристаллами из ближайшего кланового/личного склада"),
            ItemType.EMI => ("ЭМИ", "Предмет, который запрещает игрокам в радиусе 20 блоков использовать инвентарь/копать"),
            ItemType.GeoRainbow => ("Радужная жива", "Живка, которая даёт плод любым блоком, если с одной из сторон по горизонтали или вертикали не пусто"),
            ItemType.BotSpot => ("Спот", "Предмет, который создаёт робота-клона"),
            ItemType.ScienceCentre => ("Научный центр", "Строительный пак, в котором можно изучить мир, и ознакомиться со списком лучших игроков/кланов"),
            ItemType.Currency => ("Валюта", "Валюта, которая является основной для торговли и прокачки умений."),
            ItemType.OPP => ("ОПП", "Очки, которые дают возможность купить другие умения, которые лучше чем начальные"),
            _ => (i.ToString(), string.Empty)
        };

        private void HandleUseItem()
        {
            if (IsBuildingPack(_selectedItemType))
            {
                var packType = ItemTypeToPackType(_selectedItemType);
                if (packType == PackType.None) return;

                ushort frontX = _x;
                ushort frontY = _y;
                switch (_rot)
                {
                    case Direction.Up: frontY++; break;
                    case Direction.Down: frontY--; break;
                    case Direction.Left: frontX--; break;
                    case Direction.Right: frontX++; break;
                }

                OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new PackPacket(frontX, frontY, packType, 0, 0)
                })));
                ConsumeItem(_selectedItemType, 1);
            }
            else if (_selectedItemType == ItemType.Rem)
            {
                OnReceived?.Invoke(new ServerPacket(new HealthPacket(500, 500)));
                ConsumeItem(_selectedItemType, 1);
            }
            else
            {
                ConsumeItem(_selectedItemType, 1);
            }
        }

        private void ConsumeItem(ItemType type, long count)
        {
            OnReceived?.Invoke(new ServerPacket(new InventoryPacket(
                new Dictionary<ItemType, long> { { type, 0 } })));
        }

        private static bool IsBuildingPack(ItemType type) => type switch
        {
            ItemType.Teleport or ItemType.Resp or ItemType.Up or ItemType.Market or
            ItemType.Clans or ItemType.Craft or ItemType.BombShop or ItemType.Gun or
            ItemType.Gate or ItemType.Storage or ItemType.ScienceCentre => true,
            _ => false
        };

        private static PackType ItemTypeToPackType(ItemType type) => type switch
        {
            ItemType.Teleport => PackType.Teleport,
            ItemType.Resp => PackType.Resp,
            ItemType.Up => PackType.Up,
            ItemType.Market => PackType.Market,
            ItemType.Clans => PackType.Clans,
            ItemType.Craft => PackType.Craft,
            ItemType.BombShop => PackType.BombShop,
            ItemType.Gun => PackType.Gun,
            ItemType.Storage => PackType.Storage,
            ItemType.ScienceCentre => PackType.Science,
            _ => PackType.None
        };

        private async UniTaskVoid RunTilingTestLoop()
        {
            ushort baseX = 144;
            ushort baseY = 9;
            int counter = 0;

            while (_status == ConnectionStatus.Connected)
            {
                // We need a 3x3 grid (9 cells)
                // Layout:
                // 0 1 2
                // 3 4 5
                // 6 7 8
                // Index 4 is the center (static)

                var grid = new CellType[9];
                int bit = 0;

                for (int i = 0; i < 9; i++)
                {
                    if (i == 4)
                    {
                        // The center cell remains static
                        grid[i] = CellType.BuildingDoor;
                        continue;
                    }

                    // Check if the N-th bit is set in our counter
                    bool isNeighborPresent = ((counter >> bit) & 1) == 1;
                    grid[i] = isNeighborPresent ? CellType.BuildingDoor : CellType.Empty;
                    bit++;
                }

                var mapRegionPacket = new MapRegionPacket
                {
                    X = baseX,
                    Y = baseY,
                    Width = 2,  // 3 cells wide (Width is Size - 1)
                    Height = 2, // 3 cells high
                    Payload = grid
                };

                OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { mapRegionPacket })));

                // Increment counter and wrap at 256
                counter = (counter + 1) % 256;

                // Delay to make the animation visible (e.g., 200ms)
                await UniTask.Delay(200);
            }
        }

        public void SendMockWindow(bool comprehensive)
        {
            var windowPacket = comprehensive ? CreateComprehensiveMockWindow() : CreateMockWindow();
        }

        private OpenWindowPacket CreateMockWindow()
        {
            var rootElement = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(255, 66, 66, 66),
                    Padding = new Margins(10, 10, 10, 10)
                },
                Children = new List<IGUIComponentPacket>
                {
                    new TextPacket
                    {
                        Text = "<color=white>Top 0</color>",
                        Style = new GUIStylePacket {
                            Background = System.Drawing.Color.Blue,
                            Padding = new Margins(5,5,5,5)
                        },
                        AttachedProperties = new StringPairPacket[] {
                            new("DockPanel.Dock", "Top")
                        }
                    },
                    new TextPacket
                    {
                        Text = "<color=white>Left 1</color>",
                        Style = new GUIStylePacket {
                            Background = System.Drawing.Color.Red,
                            Padding = new Margins(5,5,5,5)
                        },
                        AttachedProperties = new StringPairPacket[] {
                            new("DockPanel.Dock", "Left")
                        }
                    },
                    new TextPacket
                    {
                        Text = "<color=white>Bottom 2</color>",
                        Style = new GUIStylePacket {
                            Background = System.Drawing.Color.Blue,
                            Padding = new Margins(5,5,5,5)
                        },
                        AttachedProperties = new StringPairPacket[] {
                            new("DockPanel.Dock", "Bottom")
                        }
                    },
                    new TextPacket
                    {
                        Text = "<color=white>Right 3</color>",
                        Style = new GUIStylePacket {
                            Background = System.Drawing.Color.Red,
                            Padding = new Margins(5,5,5,5)
                        },
                        AttachedProperties = new StringPairPacket[] {
                            new("DockPanel.Dock", "Right")
                        }
                    },
                    new GridPacket
                    {
                        Columns = new byte[] { 1, 0, 1, 1 },
                        Rows = new byte[] { 1, 0, 1, 1 },
                        Children = new IGUIComponentPacket[]
                        {
                            new TextPacket {
                                Text = "(0,0)",
                                AttachedProperties = new StringPairPacket[] {
                                    new("Grid.Row", "0"),
                                    new("Grid.Column", "0")
                                },
                                Style = new GUIStylePacket{
                                    Background = System.Drawing.Color.Yellow
                                }
                            },
                            new TextPacket {
                                Text = "Auto-Row",
                                AttachedProperties = new StringPairPacket[] {
                                    new("Grid.Row", "1"),
                                    new("Grid.Column", "0")
                                },
                                Style = new GUIStylePacket{
                                    Background = System.Drawing.Color.CornflowerBlue,
                                    Padding = new Margins(5,5,15,5)
                                }
                            },
                        }
                    }
                }
            };

            return new OpenWindowPacket("TestWindow", 800, 600, rootElement);
        }

        private OpenWindowPacket CreateComprehensiveMockWindow()
        {
            var @checked = new ImagePacket()
            {
                URI = "/ui/checked.png",
                Width = 32,
                Height = 32
            };
            var @unchecked = new ImagePacket()
            {
                URI = "/ui/unchecked.png",
                Width = 32,
                Height = 32
            };
            var selected = new ImagePacket()
            {
                URI = "/ui/selected.png",
                Width = 32,
                Height = 32
            };
            var deselected = new ImagePacket()
            {
                URI = "/ui/deselected.png",
                Width = 32,
                Height = 32
            };

            var rootElement = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(255, 22, 22, 22),
                    Padding = new Margins(5, 5, 5, 5)
                },
                Children = new List<IGUIComponentPacket>
                {
                    new TextPacket
                    {
                        Text = "<color=white>Header</color>",
                        Style = new GUIStylePacket {
                            Background = System.Drawing.Color.DarkBlue,
                            Padding = new Margins(5,5,5,5)
                        },
                        AttachedProperties = new StringPairPacket[] {
                            new("DockPanel.Dock", "Top")
                        }
                    },
                    new ScrollViewerPacket
                    {
                         Children = new IGUIComponentPacket[]
                         {
                             new SelectablePacket
                             {
                                 Name = "testcheckbox",
                                 Checked = @checked,
                                 Unchecked = @unchecked
                             },
                             new TextBoxPacket {
                                 DefaultValue = "123123123",
                                 Name = "textbox",
                                 Regex = "^\\d*$",
                                 Style = new GUIStylePacket{
                                     Background = System.Drawing.Color.LightGray
                                 }
                            },
                            new SliderPacket {
                                DefaultValue = 0,
                                MinValue = 0,
                                MaxValue = 100,
                                Name = "slider",
                                Knob = new()
                                {
                                    URI = "/ui/knob.png",
                                    Width = 16,
                                    Height = 16
                                }
                            },
                            new ImagePacket {
                                URI = "/test.png",
                                Width = 50,
                                Height = 50
                            }
                         }
                    }
                }
            };

            return new OpenWindowPacket("ComprehensiveTestWindow", 1200, 800, rootElement);
        }

        /// <summary>
        /// Create test cell configurations for different cell types
        /// </summary>
        private CellConfigurationPacket[] CreateTestCellConfigurations()
        {
            var configs = new CellConfigurationPacket[256];
            for (int i = 0; i < 256; i++)
            {
                configs[i] = new CellConfigurationPacket
                {
                    Animation = CellAnimationType.None,
                    AnimationSpeed = 0,
                    Color = unchecked((int)0xFF808080),
                    FrameOffset = 0,
                    Properties = CellConfigProperties.None
                };
            }
            configs[(int)CellType.Empty] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF808080),
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow
            };
            configs[(int)CellType.Road] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFCCCCCC),
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow
            };
            configs[(int)CellType.Boulder1] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF000000),
                FrameOffset = 0,
                Distortion = CellDistortionType.Block,
                Properties = CellConfigProperties.None
            };
            configs[(int)CellType.WhiteSand] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFFFFF00),
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.DarkWhiteSand] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFCCCC00),
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.GrayAcid] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.Blinking,
                AnimationSpeed = 5,
                Color = unchecked((int)0xFF00FF00),
                FrameOffset = 1,
                Properties = CellConfigProperties.None | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.PurpleAcid] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.Shimmer,
                AnimationSpeed = 50, // Time.x speed
                Color = unchecked((int)0xFF800080),
                FrameOffset = 1,
                Properties = CellConfigProperties.None | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.Lava] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.Rainbow,
                AnimationSpeed = 50,
                Color = unchecked((int)0xFFFF4500),
                FrameOffset = 1,
                Distortion = CellDistortionType.Cause,
                Properties = CellConfigProperties.None | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.BuildingDoor] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF8B4513),
                FrameOffset = 0,
                ReliefGroup = 1,
                Properties = CellConfigProperties.None | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.BuildingCorner] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF555555),
                FrameOffset = 0,
                ReliefGroup = 1,
                Properties = CellConfigProperties.None | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.BuildingWall] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF666666),
                FrameOffset = 0,
                ReliefGroup = 1,
                Properties = CellConfigProperties.None | CellConfigProperties.DropsShadow
            };
            configs[(int)CellType.Green] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF00FF00),
                FrameOffset = 0,
                Properties = CellConfigProperties.Breakable
            };

            return configs;
        }

        /// <summary>
        /// Send test world map data using MapRegionPackets
        /// </summary>
        private void SendTestWorldMapData(int testWorldWidth, int testWorldHeight)
        {
            var testMap = CreateTestMapData(testWorldWidth, testWorldHeight);
            const int chunkSize = 32;
            for (int y = 0; y < testWorldHeight; y += chunkSize)
            {
                for (int x = 0; x < testWorldWidth; x += chunkSize)
                {
                    int chunkWidth = Math.Min(chunkSize, testWorldWidth - x);
                    int chunkHeight = Math.Min(chunkSize, testWorldHeight - y);
                    var chunkData = new CellType[chunkWidth * chunkHeight];
                    int dataIndex = 0;
                    for (int cy = 0; cy < chunkHeight; cy++)
                    {
                        for (int cx = 0; cx < chunkWidth; cx++)
                        {
                            chunkData[dataIndex++] = testMap[x + cx, y + cy];
                        }
                    }
                    var mapRegionPacket = new MapRegionPacket
                    {
                        X = (ushort)x,
                        Y = (ushort)y,
                        Width = (byte)(chunkWidth - 1),
                        Height = (byte)(chunkHeight - 1),
                        Payload = chunkData
                    };
                    var hbPacket = new HBPacket(new IHBPacket[] { mapRegionPacket });
                    OnReceived?.Invoke(new ServerPacket(hbPacket));
                }
            }
        }

        /// <summary>
        /// Create test map data with various cell types for renderer testing
        /// </summary>
        private CellType[,] CreateTestMapData(int width, int height)
        {
            var map = new CellType[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    map[x, y] = CellType.Empty;

            int galleryX = 5;
            int galleryY = 5;
            int squaresPerRow = (width - galleryX) / 15;

            for (int i = 0; i < _allCellTypes.Length; i++)
            {
                int row = i / squaresPerRow;
                int col = i % squaresPerRow;
                int startX = galleryX + col * 15;
                int startY = galleryY + row * 15;

                for (int dx = 0; dx < 10; dx++)
                    for (int dy = 0; dy < 10; dy++)
                        map[startX + dx, startY + dy] = _allCellTypes[i];
            }

            // Clear BuildingDoor square (index 9) for tiling test
            // Square starts at (140, 5), 10×10
            for (int dx = 0; dx < 10; dx++)
                for (int dy = 0; dy < 10; dy++)
                    map[140 + dx, 5 + dy] = CellType.Empty;

            int lastGalleryRow = (_allCellTypes.Length - 1) / squaresPerRow;
            int tilingStartY = galleryY + (lastGalleryRow + 1) * 15 + 5;
            int tilingStartX = galleryX;
            int tilingPerRow = (width - tilingStartX) / 4;

            for (int variant = 0; variant < 256; variant++)
            {
                int tRow = variant / tilingPerRow;
                int tCol = variant % tilingPerRow;
                int bx = tilingStartX + tCol * 4;
                int by = tilingStartY + tRow * 4;

                for (int dy = 0; dy < 3; dy++)
                {
                    for (int dx = 0; dx < 3; dx++)
                    {
                        if (dx == 1 && dy == 1)
                        {
                            map[bx + dx, by + dy] = CellType.BuildingDoor;
                            continue;
                        }

                        int bitIdx = dy * 3 + dx;
                        if (bitIdx > 4) bitIdx--;

                        map[bx + dx, by + dy] = ((variant >> bitIdx) & 1) == 1
                            ? CellType.BuildingDoor
                            : CellType.Empty;
                    }
                }
            }

            return map;
        }

        private void CreateCellTypeLabels(int worldWidth, int worldHeight)
        {
            var parent = new GameObject("CellTypeLabels");
            UnityEngine.Object.DontDestroyOnLoad(parent);

            int squaresPerRow = (worldWidth - 5) / 15;
            int galleryX = 5;
            int galleryY = 5;

            for (int i = 0; i < _allCellTypes.Length; i++)
            {
                int row = i / squaresPerRow;
                int col = i % squaresPerRow;
                int serverX = galleryX + col * 15 + 5;
                int serverY = galleryY + row * 15 + 10;

                float unityX = serverX + 0.5f;
                float unityY = worldHeight - 1 - serverY - 0.5f;

                var labelGO = new GameObject($"Label_{i}");
                labelGO.transform.SetParent(parent.transform);
                labelGO.transform.position = new Vector3(unityX, unityY, 0);

                var tm = labelGO.AddComponent<TextMesh>();
                tm.text = $"{_allCellTypes[i]} ({(int)_allCellTypes[i]})";
                tm.fontSize = 20;
                tm.characterSize = 0.5f;
                tm.color = new Color(1f, 1f, 1f, 0.9f);
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
            }
        }

        private async UniTaskVoid HandleRobotInfoMock(ushort botId)
        {
            await UniTask.Delay(2000);
            OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(botId, 999, 1, "skin/bee.png", "tail/default.png", "BeeBot")));
        }

        private async UniTaskVoid RunCircularBots(int count)
        {
            int baseId = 1000;

            var bots = new List<(ushort id, float cx, float cy, float r, float a, float speed)>();
            for (int i = 0; i < count; i++)
            {
                ushort botId = (ushort)(baseId + i);
                OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(botId, 1000, 0,
                    "skin/bee.png", "tail/default.png", $"")));

                float radius = UnityEngine.Random.Range(0.5f, 5f);
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float speed = 0.3f + UnityEngine.Random.Range(-0.1f, 0.1f);
                bots.Add((botId, 50f, 50f, radius, angle, speed));
            }

            while (_status == ConnectionStatus.Connected)
            {
                var positions = new List<IHBPacket>(bots.Count);
                for (int i = 0; i < bots.Count; i++)
                {
                    var b = bots[i];
                    int x = Mathf.RoundToInt(b.cx + Mathf.Cos(b.a) * b.r);
                    int y = Mathf.RoundToInt(b.cy + Mathf.Sin(b.a) * b.r);
                    float deg = (Mathf.Atan2(Mathf.Sin(b.a), Mathf.Cos(b.a)) * Mathf.Rad2Deg + 360) % 360;
                    byte rot = deg switch
                    {
                        > 225 and <= 315 => 0,
                        > 135 and <= 225 => 1,
                        > 45 and <= 135 => 2,
                        _ => 3
                    };
                    positions.Add(new RobotPositionPacket(b.id, (ushort)x, (ushort)y, rot));
                    bots[i] = (b.id, b.cx, b.cy, b.r, b.a + b.speed * 0.1f, b.speed);
                }
                OnReceived?.Invoke(new ServerPacket(new HBPacket(positions.ToArray())));
                await UniTask.Delay(20);
            }
        }

        private async UniTaskVoid HandleAssetRequest(RuntimeAssetRequestPacket runtimeAssets)
        {
            foreach (var assetEntry in runtimeAssets.Assets)
            {
                var data = await Fodinae.Scripts.Networking.Connection.Client.TextureStorageManager.Instance.GetTextureData(assetEntry.Filename.TrimStart('/'));

                if (data != null)
                {
                    var response = new RuntimeAssetPacket(assetEntry.Filename, Guid.NewGuid().ToString(), data);
                    OnReceived?.Invoke(new ServerPacket(response));
                }
                else
                {
                    Debug.LogError($"[DummyConnection] Failed to get asset data for: {assetEntry.Filename}");
                }
            }
        }

        private async UniTaskVoid SendSkillProgressMock()
        {
            var skills = new (SkillType type, long current, long max)[]
            {
        (SkillType.MineGeneral, 75, 100),
        (SkillType.Extraction, 120, 100),
        (SkillType.Health, 40, 100),
        (SkillType.Movement, 10, 100)
            };

            while (_status == ConnectionStatus.Connected)
            {
                foreach (var s in skills)
                    OnReceived?.Invoke(new ServerPacket(new SkillProgressPacket(s.type, s.current, s.max)));
                await UniTask.Delay(1000);
            }
        }

        private async UniTaskVoid SendChatMock()
        {
            var names = new[] { "Alice", "Bob", "Charlie", "Darkar25", "Eve" };
            var messages = new[] { "gg", "welcome!", "как дела?", "lol", "nice",
        "gl hf", "куда бежать?", "фармим)", "👋", "подскажите кто знает" };
            var rng = new System.Random();

            while (_status == ConnectionStatus.Connected)
            {
                await UniTask.Delay(8000 + rng.Next(4000));

                string name = names[rng.Next(names.Length)];
                string msg = messages[rng.Next(messages.Length)];
                System.Drawing.Color nickColor = System.Drawing.Color.FromArgb(
                    255, rng.Next(100, 256), rng.Next(100, 256), rng.Next(100, 256));

                var chatMsg = new ChatMessagePacket(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    rng.Next(100, 999), (byte)rng.Next(0, 3),
                    nickColor, name,
                    System.Drawing.Color.White, msg
                );
                OnReceived?.Invoke(new ServerPacket(new ChatMessageListPacket("global", new[] { chatMsg })));
            }
        }

        private async UniTaskVoid SendPingMock()
        {
            await UniTask.Delay(2000);
            while (_status == ConnectionStatus.Connected)
            {
                OnReceived?.Invoke(new ServerPacket(new PingPacket(DateTimeOffset.UtcNow.Ticks, UnityEngine.Random.Range(15, 60))));
                await UniTask.Delay(5000);
            }
        }

        private static int GetCrystalBasketIndex(CellType cell)
        {
            return cell switch
            {
                CellType.Green => 0,
                CellType.Blue => 1,
                CellType.Red => 2,
                CellType.Violet => 3,
                CellType.White => 4,
                CellType.Cyan => 5,
                _ => -1
            };
        }
    }
}
