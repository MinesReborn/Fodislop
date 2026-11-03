#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using UnityEngine;

using Kern.Game.Player.Movement;

namespace Kern.Game.Player.Pathfinding;

// A* по четырём направлениям. Вес входа — movement cooldown клетки от сервера.
public static class ClickPathfinder
{
    private const int MaxExpandedNodes = 24000;
    private const int MaxPathLength = 2048;

    [System.ThreadStatic]
    private static Dictionary<int, int>? s_cachedCameFrom;
    [System.ThreadStatic]
    private static Dictionary<int, int>? s_cachedCostSoFar;
    [System.ThreadStatic]
    private static MinHeap? s_cachedOpen;

    // Возвращает клетки маршрута в серверных координатах БЕЗ стартовой клетки:
    // от первого шага до цели включительно. null - путь не найден.
    public static List<Vector2Int>? FindPath(
        IWorldDataStorage storage,
        IMapDataProvider mapDataProvider,
        Vector2Int start,
        Vector2Int target)
    {
        int width = mapDataProvider.WorldWidth;
        int height = mapDataProvider.WorldHeight;
        if (width <= 0 || height <= 0 ||
            !PlayerMovementValidator.IsWithinWorldBounds(start, width, height) ||
            !PlayerMovementValidator.IsWithinWorldBounds(target, width, height))
        {
            return null;
        }

        if (start == target)
        {
            return null;
        }

        int minStepMs = CellTraversalCost.MinStepMs(mapDataProvider);

        int startIndex = start.y * width + start.x;
        int targetIndex = target.y * width + target.x;

        var cameFrom = s_cachedCameFrom ??= new Dictionary<int, int>(1 << 10);
        var costSoFar = s_cachedCostSoFar ??= new Dictionary<int, int>(1 << 10);
        var open = s_cachedOpen ??= new MinHeap();

        cameFrom.Clear();
        costSoFar.Clear();
        open.Clear();

        cameFrom[startIndex] = -1;
        costSoFar[startIndex] = 0;
        open.Push(Heuristic(start, target, minStepMs), 0, startIndex);

        int expanded = 0;
        bool found = false;

        // Ближайшая к цели достигнутая клетка: если сама цель непроходима
        // (закрыта), маршрут строится до неё.
        int bestNode = startIndex;
        int bestH = int.MaxValue;

        while (open.Count > 0)
        {
            open.Pop(out int f, out int g, out int current);

            // Ленивое удаление: в куче могут лежать устаревшие записи.
            if (g > costSoFar[current])
            {
                continue;
            }

            var currentPosition = new Vector2Int(current % width, current / width);
            int currentH = Heuristic(currentPosition, target, minStepMs);
            if (currentH < bestH)
            {
                bestH = currentH;
                bestNode = current;
            }

            if (current == targetIndex)
            {
                found = true;
                break;
            }

            if (++expanded > MaxExpandedNodes)
            {
                break;
            }

            int cx = current % width;
            int cy = current / width;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0) == (dy == 0))
                    {
                        continue; // только 4 направления
                    }

                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int neighbor = ny * width + nx;
                    int cellCost = CellTraversalCost.EnterCostMs(storage, mapDataProvider, nx, ny);
                    if (cellCost < 0)
                    {
                        continue;
                    }

                    int newCost = g + cellCost;
                    if (costSoFar.TryGetValue(neighbor, out int oldCost) && newCost >= oldCost)
                    {
                        continue;
                    }

                    costSoFar[neighbor] = newCost;
                    cameFrom[neighbor] = current;

                    var neighborPos = new Vector2Int(nx, ny);
                    open.Push(newCost + Heuristic(neighborPos, target, minStepMs), newCost, neighbor);
                }
            }
        }

        // Нашли цель - идём до неё; не нашли - до ближайшей
        // достигнутой клетки. Если и старт - ближайшая, пути нет.
        int finalNode = found ? targetIndex : bestNode;
        if (finalNode == startIndex)
        {
            return null;
        }

        var path = new List<Vector2Int>();
        int node = finalNode;
        while (node != startIndex && cameFrom.TryGetValue(node, out int previous))
        {
            path.Add(new Vector2Int(node % width, node / width));
            if (path.Count > MaxPathLength)
            {
                return null;
            }

            node = previous;
        }

        path.Reverse();
        return path.Count > 0 ? path : null;
    }

    private static int Heuristic(Vector2Int from, Vector2Int to, int minStepMs) =>
        (Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y)) * minStepMs;

    // Компактная бинарная куча по f (затем g) - в netstandard 2.1 нет PriorityQueue.
    private sealed class MinHeap
    {
        private readonly List<(int F, int G, int Index)> _items = new();

        public int Count => _items.Count;
        public void Clear() => _items.Clear();

        public void Push(int f, int g, int index)
        {
            _items.Add((f, g, index));
            SiftUp(_items.Count - 1);
        }

        public void Pop(out int f, out int g, out int index)
        {
            (f, g, index) = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            if (last > 0)
            {
                SiftDown(0);
            }
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (Compare(_items[i], _items[parent]) >= 0)
                {
                    break;
                }

                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int left = i * 2 + 1;
                int right = left + 1;
                int smallest = i;
                if (left < _items.Count && Compare(_items[left], _items[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < _items.Count && Compare(_items[right], _items[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == i)
                {
                    break;
                }

                (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                i = smallest;
            }
        }

        private static int Compare((int F, int G, int Index) a, (int F, int G, int Index) b)
        {
            int byF = a.F.CompareTo(b.F);
            return byF != 0 ? byF : a.G.CompareTo(b.G);
        }
    }
}
