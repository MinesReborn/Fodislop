#nullable enable

namespace Kern.World.Terrain
{
    /// <summary>Tracks requested terrain content separately from committed mesh revision.</summary>
    internal sealed class TerrainContentRevisionJournal
    {
        private ulong _requestedRevision = 1;

        public ulong RequestedRevision => _requestedRevision;

        public ulong RecordBoundedRegionChange() => _requestedRevision;

        public ulong RecordConfigurationChange() => ++_requestedRevision;

        public ulong RecordUnboundedGeometryChange() => ++_requestedRevision;

        public ulong RecordWorldLoad() => ++_requestedRevision;

        public ulong RecordLightingVisibleTextureChange() => ++_requestedRevision;
    }
}
