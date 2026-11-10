#nullable enable

namespace Kern.Core.Interfaces;

// Atlas data is inert in the pure CPU benchmark; FillQuad only needs the
// authored atlas dimensions when a cell has no texture rectangle, and the
// type table needs per-atlas opacity.
public interface IAtlasDescriptor
{
    int Size { get; }

    bool IsFullyOpaque(MinesServer.Data.CellType cellType);
}
