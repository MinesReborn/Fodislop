#nullable enable

using System;

namespace Kern.Core.Interfaces;
public enum WorldLoadPhase
{
    Handshake = 0,

    WorldManifest = 1,

    SpawnSync = 2,

    TerrainMesh = 3,

    Done = 4,
}

public readonly record struct AssetSyncProgress(int FilesChecked, int FilesTotal, long BytesDone, long BytesTotal);

public interface IWorldLoadProgress
{
    event Action<WorldLoadPhase>? PhaseChanged;

    event Action<Exception>? Failed;

    WorldLoadPhase CurrentPhase { get; }

    // Синхронизация ресурсов мира: сначала проверка кэша (файлов проверено из скольких), потом скачивание того, чего нет
    // или что повреждено (байт из скольких). Нули — размеры или проверка ещё неизвестны
    AssetSyncProgress AssetSync { get; }

    void Report(WorldLoadPhase phase);

    void ReportFailure(Exception exception);

    void ReportAssetCheck(int filesChecked, int filesTotal);

    void ReportAssetBytes(long bytesDone, long bytesTotal);

    void ResetAssetSync();
}
