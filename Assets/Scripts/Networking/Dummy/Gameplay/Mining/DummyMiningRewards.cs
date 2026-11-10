#nullable enable

using System;
using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Dummy;

internal static class DummyMiningRewards
{
    public static CrystalType GetMinedCrystal(CellType cell) => cell switch
    {
        CellType.Green or CellType.XGreen => CrystalType.Green,
        CellType.Blue or CellType.XBlue => CrystalType.Blue,
        CellType.Red or CellType.XRed => CrystalType.Red,
        CellType.Violet or CellType.XViolet => CrystalType.Violet,
        CellType.White => CrystalType.White,
        CellType.Cyan or CellType.XCyan => CrystalType.Cyan,
        _ => CrystalType.Unknown,
    };

    // Правило сервера: части пака (стена, угол, дверь) стыкуются друг с
    // другом как одна группа автотайла.
}
