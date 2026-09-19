#nullable enable

using System;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Kern.Core.Interfaces;
public interface IMapDataProvider
{
    ushort WorldWidth { get; }
    ushort WorldHeight { get; }
    Camera MainCamera { get; }
    bool IsStandaloneMode { get; }
    CellConfigurationPacket GetCellConfig(CellType type);
    float GetMoveCooldown(CellType cellType);
    bool TryGetTileGroup(CellType type, out int groupID);
    Color GetCellMinimapColor(CellType type);
    void UpdateMovementSpeeds(MovementSpeedPacket packet);
    void LoadWorldInit(WorldInitPacket packet);
    Action? OnWorldInitialized { get; set; }
    Action? OnWorldDataLoaded { get; set; }
    void ResetWorldState();
}
