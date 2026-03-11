using Cysharp.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared;
using MinesServer.Networking.Shared.Packets;
using System;
using System.Collections.Generic;
using UnityEngine;

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
            _status = ConnectionStatus.Connected;
            OnConnected?.Invoke();
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
        }

        public void Dispose()
        {
        }

        public void SendAsync(ClientPacket packet)
        {
            switch (packet.Data)
            {
                case ClientHelloPacket clientHello:
                    // Send world initialization with proper cell configurations
                    var cellConfigs = CreateTestCellConfigurations();
                    const int testWorldWidth = 100;
                    const int testWorldHeight = 100;
                    OnReceived?.Invoke(new ServerPacket(new WorldInitPacket(
                        "pallada",
                        "Pallada",
                        testWorldWidth,
                        testWorldHeight,
                        cellConfigs,
                        new byte[][] {
                            new byte[] { 37, 38, 106 }
                        })));

                    // Send test world map data in chunks
                    SendTestWorldMapData(testWorldWidth, testWorldHeight);

                    // Send other initial packets

                    // Send mock robot position first (loading state)
                    ushort mockBotId = 456;
                    OnReceived?.Invoke(new ServerPacket(new PlayerInfoPacket(999, mockBotId, "Darkar25")));
                    var robotPos = new RobotPositionPacket(mockBotId, 50, 50, 0);
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { robotPos })));

                    // Send robot metadata after a delay to show loading state
                    HandleRobotInfoMock(mockBotId).Forget();

                    // Start circular robot
                    ushort circularBotId = 789;
                    RunCircularRobot(circularBotId).Forget();

                    OnReceived?.Invoke(new ServerPacket(new AggressionStatePacket(false)));
                    OnReceived?.Invoke(new ServerPacket(new AutoMineStatePacket(false)));
                    OnReceived?.Invoke(new ServerPacket(new CurrencyPacket(123456, 1234)));
                    OnReceived?.Invoke(new ServerPacket(new HealthPacket(250, 500)));
                    OnReceived?.Invoke(new ServerPacket(new BasketPacket(123, new[] { 1L, 2L, 3L, 4L, 5L, 6L })));
                    OnReceived?.Invoke(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
                    OnReceived?.Invoke(new ServerPacket(new LevelPacket(12345)));
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
                default:
                    break;
            }
        }

        public void SendMockWindow(bool comprehensive)
        {
            var windowPacket = comprehensive ? CreateComprehensiveMockWindow() : CreateMockWindow();
            OnReceived?.Invoke(new ServerPacket(windowPacket));
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
            // Create array for all possible cell types (256 max)
            var configs = new CellConfigurationPacket[256];

            // Initialize all to default values
            for (int i = 0; i < 256; i++)
            {
                configs[i] = new CellConfigurationPacket
                {
                    Animation = CellAnimationType.None,
                    AnimationSpeed = 0,
                    Color = unchecked((int)0xFF808080), // Default gray
                    FrameOffset = 0,
                    Properties = CellConfigProperties.None
                };
            }

            // Configure specific cell types we use in our test map
            configs[(int)CellType.Empty] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF808080), // Gray
                FrameOffset = 22,
                Properties = CellConfigProperties.Passable
            };

            configs[(int)CellType.Road] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFCCCCCC), // Light gray
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable
            };

            configs[(int)CellType.Boulder1] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF000000), // Black
                FrameOffset = 0,
                Properties = CellConfigProperties.None
            };

            configs[(int)CellType.WhiteSand] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFFFFF00), // Yellow
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable
            };

            configs[(int)CellType.DarkWhiteSand] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFCCCC00), // Dark yellow
                FrameOffset = 0,
                Properties = CellConfigProperties.Passable
            };

            configs[(int)CellType.GrayAcid] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF00FF00), // Green
                FrameOffset = 0,
                Properties = CellConfigProperties.None
            };

            configs[(int)CellType.PurpleAcid] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF800080), // Purple
                FrameOffset = 0,
                Properties = CellConfigProperties.None
            };

            configs[(int)CellType.Lava] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFFFF4500), // OrangeRed
                FrameOffset = 0,
                Properties = CellConfigProperties.None
            };

            return configs;
        }

        /// <summary>
        /// Send test world map data using MapRegionPackets
        /// </summary>
        private void SendTestWorldMapData(int testWorldWidth, int testWorldHeight)
        {
            // Create test map data
            var testMap = CreateTestMapData(testWorldWidth, testWorldHeight);

            // Send the map data in chunks (e.g., 32x32 chunks)
            const int chunkSize = 32;

            for (int y = 0; y < testWorldHeight; y += chunkSize)
            {
                for (int x = 0; x < testWorldWidth; x += chunkSize)
                {
                    int chunkWidth = Math.Min(chunkSize, testWorldWidth - x);
                    int chunkHeight = Math.Min(chunkSize, testWorldHeight - y);

                    // Extract chunk data
                    var chunkData = new CellType[chunkWidth * chunkHeight];
                    int dataIndex = 0;

                    for (int cy = 0; cy < chunkHeight; cy++)
                    {
                        for (int cx = 0; cx < chunkWidth; cx++)
                        {
                            chunkData[dataIndex++] = testMap[x + cx, y + cy];
                        }
                    }

                    // Create and send MapRegionPacket
                    var mapRegionPacket = new MapRegionPacket
                    {
                        X = (ushort)x,
                        Y = (ushort)y,
                        Width = (byte)(chunkWidth - 1),
                        Height = (byte)(chunkHeight - 1),
                        Payload = chunkData
                    };

                    // Send as part of HBPacket
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

            // Fill with default empty cells
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    map[x, y] = CellType.Empty;
                }
            }

            // Create test patterns to exercise different rendering scenarios

            // 1. Border around the map (Road)
            for (int x = 0; x < width; x++)
            {
                map[x, 0] = CellType.Road;
                map[x, height - 1] = CellType.Road;
            }
            for (int y = 0; y < height; y++)
            {
                map[0, y] = CellType.Road;
                map[width - 1, y] = CellType.Road;
            }

            // 2. Cross pattern in the center (Boulders)
            int centerX = width / 2;
            int centerY = height / 2;
            for (int x = centerX - 10; x <= centerX + 10; x++)
            {
                if (x >= 0 && x < width)
                {
                    map[x, centerY] = CellType.Boulder1;
                }
            }
            for (int y = centerY - 10; y <= centerY + 10; y++)
            {
                if (y >= 0 && y < height)
                {
                    map[centerX, y] = CellType.Boulder1;
                }
            }

            // 3. Sand areas
            for (int x = 20; x < 40; x++)
            {
                for (int y = 20; y < 40; y++)
                {
                    map[x, y] = CellType.WhiteSand;
                }
            }

            // 4. Acid pools
            for (int x = 60; x < 80; x++)
            {
                for (int y = 60; y < 80; y++)
                {
                    map[x, y] = (x + y) % 2 == 0 ? CellType.GrayAcid : CellType.PurpleAcid;
                }
            }

            // 5. Lava area (animated)
            for (int x = 45; x < 55; x++)
            {
                for (int y = 45; y < 55; y++)
                {
                    map[x, y] = CellType.Lava;
                }
            }

            // 6. Random noise pattern
            var random = new System.Random(12345); // Fixed seed for reproducible test data
            for (int y = 10; y < height - 10; y += 3)
            {
                for (int x = 10; x < width - 10; x += 3)
                {
                    if (random.Next(100) < 30) // 30% chance
                    {
                        map[x, y] = CellType.Boulder1;
                    }
                }
            }

            return map;
        }

        private async UniTaskVoid HandleRobotInfoMock(ushort botId)
        {
            await UniTask.Delay(2000); // 2 second delay to see "loading" state
            OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(botId, 999, "skin/bee.png", "", "BeeBot")));
        }

        private async UniTaskVoid RunCircularRobot(ushort botId)
        {
            // Initial position
            int centerX = 55;
            int centerY = 55;
            float angle = 0;
            float radius = 3.0f;

            // Send initial info
            OnReceived?.Invoke(new ServerPacket(new PlayerInfoPacket(1000, botId, "CircularBot")));
            OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(botId, 1000, "skin/bee.png", "", "CircularBot")));

            while (_status == ConnectionStatus.Connected)
            {
                int x = centerX + Mathf.RoundToInt(Mathf.Cos(angle) * radius);
                int y = centerY + Mathf.RoundToInt(Mathf.Sin(angle) * radius);
                byte rotation = (byte)((Mathf.Atan2(Mathf.Sin(angle), Mathf.Cos(angle)) * Mathf.Rad2Deg + 360) % 360 / 90);

                var robotPos = new RobotPositionPacket(botId, (ushort)x, (ushort)y, rotation);
                OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { robotPos })));

                angle += 0.5f;
                await UniTask.Delay(500);
            }
        }

        private async UniTaskVoid HandleAssetRequest(RuntimeAssetRequestPacket runtimeAssets)
        {
            foreach (var assetEntry in runtimeAssets.Assets)
            {
                // Use TextureStorageManager to get texture data
                // This will load from local storage if available, or generate random texture as fallback
                var pngData = await Fodinae.Assets.Scripts.Networking.Connection.Client.TextureStorageManager.Instance.GetTextureData(assetEntry.Filename);

                if (pngData != null)
                {
                    var response = new RuntimeAssetPacket(assetEntry.Filename, Guid.NewGuid().ToString(), pngData);
                    OnReceived?.Invoke(new ServerPacket(response));
                }
                else
                {
                    Debug.LogError($"[DummyConnection] Failed to get texture data for: {assetEntry.Filename}");
                }
            }
        }
    }
}
