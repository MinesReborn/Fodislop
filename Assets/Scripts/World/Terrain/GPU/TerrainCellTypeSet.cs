#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>
/// Immutable set of byte-backed CellType values for transfer to the worker.
/// A bitset avoids exposing the journal's mutable HashSet or allocating a
/// second set for each build request.
/// </summary>
internal readonly struct TerrainCellTypeSet
{
    private readonly ulong _bits0;
    private readonly ulong _bits1;
    private readonly ulong _bits2;
    private readonly ulong _bits3;

    private TerrainCellTypeSet(ulong bits0, ulong bits1, ulong bits2, ulong bits3)
    {
        _bits0 = bits0;
        _bits1 = bits1;
        _bits2 = bits2;
        _bits3 = bits3;
    }

    public bool IsEmpty => (_bits0 | _bits1 | _bits2 | _bits3) == 0;

    public bool Contains(CellType type)
    {
        int value = (byte)type;
        ulong word = (value >> 6) switch
        {
            0 => _bits0,
            1 => _bits1,
            2 => _bits2,
            _ => _bits3,
        };
        return (word & (1UL << (value & 63))) != 0;
    }

    public static TerrainCellTypeSet Capture(HashSet<CellType> types)
    {
        ulong bits0 = 0;
        ulong bits1 = 0;
        ulong bits2 = 0;
        ulong bits3 = 0;
        foreach (CellType type in types)
        {
            int value = (byte)type;
            ulong bit = 1UL << (value & 63);
            switch (value >> 6)
            {
                case 0: bits0 |= bit; break;
                case 1: bits1 |= bit; break;
                case 2: bits2 |= bit; break;
                default: bits3 |= bit; break;
            }
        }

        return new TerrainCellTypeSet(bits0, bits1, bits2, bits3);
    }
}
