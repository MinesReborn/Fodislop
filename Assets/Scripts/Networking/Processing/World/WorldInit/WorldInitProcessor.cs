#nullable enable

using System.Collections.Generic;
using System.Diagnostics;
using Kern.Core.Interfaces;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Processing;

public sealed class WorldInitProcessor : System.IDisposable
{
    private readonly IMapDataProvider _mapManager;
    private readonly IWorldReadiness _gameManager;
    private readonly IRobotService _robotManager;
    private readonly IBuildingService _buildingManager;
    private readonly ILocalPlayerState _localPlayer;
    private readonly IAudioBankManifestState _audioBankManifest;
    private readonly ITextureService? _textureService;
    private bool _disposed;

    public WorldInitProcessor(
        IMapDataProvider mapManager,
        IWorldReadiness gameManager,
        IRobotService robotManager,
        IBuildingService buildingManager,
        ILocalPlayerState localPlayer,
        IAudioBankManifestState audioBankManifest,
        ITextureService? textureService = null)
    {
        _mapManager = mapManager;
        _gameManager = gameManager;
        _robotManager = robotManager;
        _buildingManager = buildingManager;
        _localPlayer = localPlayer;
        _audioBankManifest = audioBankManifest;
        _textureService = textureService;
    }

    public void Process(WorldInitPacket packet)
    {
        if (_disposed)
        {
            throw new System.ObjectDisposedException(nameof(WorldInitProcessor));
        }

        if (!_mapManager.IsStandaloneMode)
        {
            if (packet.AssetManifest == null)
            {
                throw new System.IO.InvalidDataException(
                    "Online WorldInitPacket is missing its world asset manifest.");
            }

            foreach (WorldAssetDefinitionPacket asset in packet.AssetManifest)
            {
                if (string.IsNullOrWhiteSpace(asset.ETag))
                {
                    throw new System.IO.InvalidDataException(
                        $"Online world asset '{asset.Filename}' is missing its server ETag.");
                }
            }
        }

        long started = Stopwatch.GetTimestamp();
        _robotManager.ClearAllRobots();
        long robotsCleared = Stopwatch.GetTimestamp();
        _buildingManager.ClearAllBuildings();
        long buildingsCleared = Stopwatch.GetTimestamp();
        _localPlayer.Current?.ResetServerPosition();
        _gameManager.NotifyWorldLoaded();
        long playerReset = Stopwatch.GetTimestamp();
        _mapManager.LoadWorldInit(packet);
        if (!_mapManager.TryGetAudioBankFilenames(out IReadOnlyList<string> bankFilenames))
        {
            throw new System.IO.InvalidDataException(
                "World asset manifest was not available after loading WorldInitPacket.");
        }

        _audioBankManifest.SetBankFilenames(bankFilenames);
        _textureService?.LoadWorldAssets(
            packet.AssetManifest ?? System.Array.Empty<WorldAssetDefinitionPacket>());
        long worldLoaded = Stopwatch.GetTimestamp();

        UnityEngine.Debug.Log(
            $"[WorldInit] stages ms: robots={(robotsCleared - started) * 1000d / Stopwatch.Frequency:F1}, " +
            $"buildings={(buildingsCleared - robotsCleared) * 1000d / Stopwatch.Frequency:F1}, " +
            $"player={(playerReset - buildingsCleared) * 1000d / Stopwatch.Frequency:F1}, " +
            $"map={(worldLoaded - playerReset) * 1000d / Stopwatch.Frequency:F1}, " +
            $"total={(worldLoaded - started) * 1000d / Stopwatch.Frequency:F1}.");
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
