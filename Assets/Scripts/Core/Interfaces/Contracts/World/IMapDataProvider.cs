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

    /// <summary>
    /// Тот же цвет, что и <see cref="GetCellMinimapColor"/>, но точные байты.
    /// Нужен там, где результат кладётся в <see cref="Color32"/>: путь через
    /// <see cref="Color"/> срезает единицу в каналах на обратном касте.
    /// </summary>
    Color32 GetCellMinimapColor32(CellType type);
    void UpdateMovementSpeeds(MovementSpeedPacket packet);
    void LoadWorldInit(WorldInitPacket packet);
    Action? OnWorldInitialized { get; set; }
    Action? OnWorldDataLoaded { get; set; }
    void ResetWorldState();
}
