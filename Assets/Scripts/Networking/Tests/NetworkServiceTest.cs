using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using System.Collections.Generic;
using MinesServer.Networking.Server;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Assets.Scripts.Networking.Tests
{
    public class NetworkServiceTest : MonoBehaviour
    {
        private int _worldInitCount = 0;
        private int _hbCount = 0;
        private int _robotPosCount = 0;

        void Start()
        {
            Debug.Log("[NetworkServiceTest] Starting test...");

            var ns = NetworkService.Instance;
            ns.Subscribe<WorldInitPacket>(OnWorldInit);
            ns.Subscribe<HBPacket>(OnHB);
            ns.Subscribe<RobotPositionPacket>(OnRobotPos);

            // Simulate packet reception
            Debug.Log("[NetworkServiceTest] Simulating WorldInitPacket...");
            var worldInit = new WorldInitPacket("test", "Test World", 100, 100, new CellConfigurationPacket[0], new byte[0][]);
            SimulatePacket(worldInit);

            Debug.Log("[NetworkServiceTest] Simulating HBPacket with RobotPositionPacket...");
            var robotPos = new RobotPositionPacket(1, 10, 10, 0);
            var hb = new HBPacket(new IHBPacket[] { robotPos });
            SimulatePacket(hb);

            // Verify
            if (_worldInitCount == 1 && _hbCount == 1 && _robotPosCount == 1)
            {
                Debug.Log("[NetworkServiceTest] SUCCESS: All packets received correctly!");
            }
            else
            {
                Debug.LogError($"[NetworkServiceTest] FAILURE: Expected (1,1,1), got ({_worldInitCount}, {_hbCount}, {_robotPosCount})");
            }

            // Cleanup
            ns.Unsubscribe<WorldInitPacket>(OnWorldInit);
            ns.Unsubscribe<HBPacket>(OnHB);
            ns.Unsubscribe<RobotPositionPacket>(OnRobotPos);

            Destroy(this);
        }

        private void SimulatePacket(IRootServerPacket payload)
        {
            // We use Reflection to call the private OnPacketReceived for testing
            var method = typeof(NetworkService).GetMethod("OnPacketReceived", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(NetworkService.Instance, new object[] { new ServerPacket(payload) });
            }
            else
            {
                Debug.LogError("[NetworkServiceTest] Could not find OnPacketReceived method via reflection");
            }
        }

        private void OnWorldInit(WorldInitPacket p) => _worldInitCount++;
        private void OnHB(HBPacket p) => _hbCount++;
        private void OnRobotPos(RobotPositionPacket p) => _robotPosCount++;
    }
}
