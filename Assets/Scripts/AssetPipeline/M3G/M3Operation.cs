#nullable enable

namespace Kern;

/// <summary>
/// Operations of the M3G image format. Values match the operation slots in
/// the file header (bytes 4..13) and the reference encoder in MinesServer.M3G.
/// </summary>
public enum M3Operation : byte
{
    None = 0,
    Delta = 1,
    Ngramm = 2,
    RLE = 3,
    PackInterleaved = 4,
    PackPlanar = 5,
}
