#nullable enable

using Kern.Core.Interfaces;

namespace Kern.Core.Lifecycle;

public sealed class WorldLoadProgress : IWorldLoadProgress
{
    public WorldLoadPhase CurrentPhase { get; private set; }

    public AssetSyncProgress AssetSync { get; private set; }

    public void Report(WorldLoadPhase phase)
    {
        CurrentPhase = phase;
    }

    public void ReportAssetCheck(int filesChecked, int filesTotal)
    {
        AssetSync = AssetSync with { FilesChecked = filesChecked, FilesTotal = filesTotal };
    }

    public void ReportAssetBytes(long bytesDone, long bytesTotal)
    {
        AssetSync = AssetSync with { BytesDone = bytesDone, BytesTotal = bytesTotal };
    }

    public void ResetAssetSync()
    {
        AssetSync = default;
    }
}
