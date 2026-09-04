#nullable enable

using Fodinae;
using System;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyBuildHandler
{
    public static void TryBuild(IWorldLayer<CellType>? worldLayer, Func<ushort, ushort, CellType> getCell, Action<ushort, ushort, CellType> setCell, Action<ServerPacket> sendPacket, ushort x, ushort y, CellType placeType)
    {
        if (worldLayer == null)
        {
            return;
        }

        CellType current = getCell(x, y);
        if (current != CellType.Empty && current != CellType.Road)
        {
            return;
        }

        setCell(x, y, placeType);
        sendPacket(new ServerPacket(new HBPacket(new IHBPacket[] { new MapRegionPacket(x, y, 0, 0, new[] { placeType }) })));
    }

    public static void TryUpgradeBuild(IWorldLayer<CellType>? worldLayer, Func<ushort, ushort, CellType> getCell, Action<ushort, ushort, CellType> setCell, Action<ServerPacket> sendPacket, ushort x, ushort y, params (CellType From, CellType To)[] upgrades)
    {
        if (worldLayer == null)
        {
            return;
        }

        CellType current = getCell(x, y);

        for (int i = 0; i < upgrades.Length; i++)
        {
            if (current == upgrades[i].From || (current == CellType.Road && i == 0 && upgrades[i].From == CellType.Empty))
            {
                setCell(x, y, upgrades[i].To);
                sendPacket(new ServerPacket(new HBPacket(new IHBPacket[] { new MapRegionPacket(x, y, 0, 0, new[] { upgrades[i].To }) })));
                return;
            }
        }
    }
}
