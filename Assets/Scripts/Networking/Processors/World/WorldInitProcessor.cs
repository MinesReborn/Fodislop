#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Processors;

public sealed class WorldInitProcessor : System.IDisposable
{
    private readonly IMapDataProvider _mapManager;
    private readonly IWorldReadiness _gameManager;
    private readonly IRobotService _robotManager;
    private readonly IBuildingService _buildingManager;
    private readonly ILocalPlayerState _localPlayer;

    public WorldInitProcessor(
        IMapDataProvider mapManager,
        IWorldReadiness gameManager,
        IRobotService robotManager,
        IBuildingService buildingManager,
        ILocalPlayerState localPlayer)
    {
        _mapManager = mapManager;
        _gameManager = gameManager;
        _robotManager = robotManager;
        _buildingManager = buildingManager;
        _localPlayer = localPlayer;
        mapManager.OnWorldInitialized += gameManager.NotifyWorldLoaded;
    }

    public void Process(WorldInitPacket packet)
    {
        _robotManager.ClearAllRobots();
        _buildingManager.ClearAllBuildings();
        _localPlayer.Current?.ResetServerPosition();
        _mapManager.LoadWorldInit(packet);
    }

    public void Dispose()
    {
        _mapManager.OnWorldInitialized -= _gameManager.NotifyWorldLoaded;
    }
}
