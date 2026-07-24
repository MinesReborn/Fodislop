using System;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IMapDataProvider
    {
        ushort WorldWidth { get; }
        ushort WorldHeight { get; }
        Camera MainCamera { get; }
        bool IsStandaloneMode { get; }
        CellConfigurationPacket GetCellConfig(CellType type);
        bool TryGetTileGroup(CellType type, out int groupId);
        Color GetCellMinimapColor(CellType type);
        void UpdateMovementSpeeds(MovementSpeedPacket packet);
        void LoadWorldInit(WorldInitPacket packet);
        Action OnWorldInitialized { get; }
        Action OnWorldDataLoaded { get; }
    }
}
