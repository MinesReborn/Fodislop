#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Terrain;

internal static class TerrainCellMaskRules
{
    public static byte CalculateSolidBoundaryMask(
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right,
        CachedCellData topLeft,
        CachedCellData topRight,
        CachedCellData bottomLeft,
        CachedCellData bottomRight)
    {
        byte solidMask = 0;
        if ((top.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 1;
        }

        if ((left.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 2;
        }

        if ((bottom.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 4;
        }

        if ((right.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 8;
        }

        if ((topLeft.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 16;
        }

        if ((topRight.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 32;
        }

        if ((bottomLeft.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 64;
        }

        if ((bottomRight.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 128;
        }

        return solidMask;
    }
}
