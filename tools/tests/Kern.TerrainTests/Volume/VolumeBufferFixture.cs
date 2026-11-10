using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Diagnostics.Frames
{
    public static class FrameEventLog
    {
        public static void Record(string message) { }
    }
}

namespace Kern.World.Terrain
{
    // In-memory published facts; projection/contour/topology are production code.
    public sealed class TerrainCellBuffers
    {
        private readonly Dictionary<(int X, int Y), TerrainCell> _cells = new();
        private readonly Dictionary<byte, TerrainTypeRow> _types = new();
        internal TerrainCell GetCell(int x, int y) => _cells.GetValueOrDefault((x, y), new TerrainCell(2));
        internal TerrainTypeRow GetTypeRow(CellType type) => _types.GetValueOrDefault((byte)type,
            new TerrainTypeRow(0, 0, TerrainCellFormat.TypeBackground, 0));
        public void Set(int x, int y, byte type) => _cells[(x, y)] = new TerrainCell(type);
        public void Type(byte type, uint look) => _types[type] = new TerrainTypeRow(0, 0, look, 0);
    }
}
