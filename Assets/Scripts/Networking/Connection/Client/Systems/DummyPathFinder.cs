#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyPathFinder
{
    private readonly Action<ServerPacket> _onReceived;
    private readonly Func<CellType, CellConfigurationPacket?> _getCellConfig;
    private static readonly (int dx, int dy)[] s_dirs = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };

    public DummyPathFinder(
        Action<ServerPacket> onReceived,
        Func<CellType, CellConfigurationPacket?> getCellConfig)
    {
        _onReceived = onReceived;
        _getCellConfig = getCellConfig;
    }

    public List<(ushort X, ushort Y)> FindPath(ushort startX, ushort startY, ushort targetX, ushort targetY, Func<ushort, ushort, CellType> getCell)
    {
        const int MaximumCellsChecked = 20000;

        var visited = _pooledVisited;
        var cameFrom = _pooledCameFrom;
        var queue = _pooledQueue;
        var path = _pooledPath;
        visited.Clear();
        cameFrom.Clear();
        queue.Clear();
        path.Clear();

        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));
        int cellsChecked = 0;
        bool found = false;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            cellsChecked++;
            if (cellsChecked > MaximumCellsChecked)
            {
                break;
            }

            if (cur.X == targetX && cur.Y == targetY)
            {
                found = true;
                break;
            }

            for (int d = 0; d < s_dirs.Length; d++)
            {
                var (dx, dy) = s_dirs[d];
                int nx = cur.X + dx;
                int ny = cur.Y + dy;
                if (nx < 0 || ny < 0 || nx > ushort.MaxValue || ny > ushort.MaxValue)
                {
                    continue;
                }

                var next = ((ushort)nx, (ushort)ny);
                if (visited.Contains(next))
                {
                    continue;
                }

                CellType cellType = getCell((ushort)nx, (ushort)ny);

                CellConfigurationPacket? cellConfig = _getCellConfig(cellType);
                bool isPassable = cellType == CellType.Empty || (cellConfig.HasValue && ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Passable));
                if (!isPassable)
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = cur;
                queue.Enqueue(next);
            }
        }

        if (!found)
        {
            return new List<(ushort, ushort)>();
        }

        var current = (targetX, targetY);
        while (current != (startX, startY))
        {
            path.Add(current);
            current = cameFrom[current];
        }

        path.Reverse();
        return new List<(ushort, ushort)>(path);
    }

    private readonly HashSet<(ushort, ushort)> _pooledVisited = new();
    private readonly Dictionary<(ushort, ushort), (ushort, ushort)> _pooledCameFrom = new();
    private readonly Queue<(ushort X, ushort Y)> _pooledQueue = new();
    private readonly List<(ushort, ushort)> _pooledPath = new();
}
