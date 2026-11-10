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
            if (packet.AssetsManifest == null)
            {
                throw new System.IO.InvalidDataException(
                    "Online WorldInitPacket is missing its world asset manifest.");
            }

            foreach (AssetsDefinitionPacket asset in packet.AssetsManifest)
            {
                if (string.IsNullOrWhiteSpace(asset.ETag))
                {
                    throw new System.IO.InvalidDataException(
                        $"Online world asset '{asset.Filename}' is missing its server ETag.");
                }
            }
        }

        // Тот же мир (имя и размер) и игра уже идёт: сервер сменил только содержимое — клетки, группы тайлов, ассеты.
        // Роботов, постройки и карту это не касается, и экран загрузки не нужен. Любое другое имя или размер — другой
        // мир, он входит заново целиком. Пока мир ещё грузится, тоже входим целиком: докручивать нечего
        if (_gameManager.IsWorldLoaded && _mapManager.IsSameWorld(packet))
        {
            ApplyContentUpdate(packet);
            return;
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
        if (!_mapManager.TryGetAudioBanks(out IReadOnlyList<AssetsDefinitionPacket> banks))
        {
            throw new System.IO.InvalidDataException(
                "World asset manifest was not available after loading WorldInitPacket.");
        }

        _audioBankManifest.SetBanks(banks);
        _textureService?.LoadWorldAssets(
            packet.AssetsManifest ?? System.Array.Empty<AssetsDefinitionPacket>());
        long worldLoaded = Stopwatch.GetTimestamp();

        UnityEngine.Debug.Log(
            $"[WorldInit] stages ms: robots={(robotsCleared - started) * 1000d / Stopwatch.Frequency:F1}, " +
            $"buildings={(buildingsCleared - robotsCleared) * 1000d / Stopwatch.Frequency:F1}, " +
            $"player={(playerReset - buildingsCleared) * 1000d / Stopwatch.Frequency:F1}, " +
            $"map={(worldLoaded - playerReset) * 1000d / Stopwatch.Frequency:F1}, " +
            $"total={(worldLoaded - started) * 1000d / Stopwatch.Frequency:F1}.");
    }

    private void ApplyContentUpdate(WorldInitPacket packet)
    {
        long started = Stopwatch.GetTimestamp();
        _mapManager.ApplyWorldContent(packet);
        if (!_mapManager.TryGetAudioBanks(out IReadOnlyList<AssetsDefinitionPacket> banks))
        {
            throw new System.IO.InvalidDataException(
                "World asset manifest was not available after applying world content.");
        }

        _audioBankManifest.SetBanks(banks);
        _textureService?.UpdateWorldAssets(
            packet.AssetsManifest ?? System.Array.Empty<AssetsDefinitionPacket>());
        UnityEngine.Debug.Log(
            $"[WorldInit] content update applied in place, ms={(Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency:F1}.");
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
