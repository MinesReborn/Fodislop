#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.Player.Logic;

// Поиск маршрута для клик-движения (ЛКМ): A* по 4 направлениям.
// Проходимая клетка стоит StepCost, сплошная ломаемая - DigCost (её придётся
// бурить Bz-пакетом по дороге, это заметно дольше шага).
// Непроходимы насовсем (маршрут их обходит, не бурит):
//  - незагруженные клетки (шаг туда всё равно не провалидировать);
//  - неломаемые буром породы: сплошные клетки без is_diggable в cells.json
//    сервера - Черноскал, Красноскал, Гипноскал, Череп, Суперрадуга,
//    Федеральный блок и пр. Флаг Breakable тут врёт: сервер ставит его и для
//    is_destructible (бомба/падение), хотя буром такие клетки не взять;
//  - паки и строительные блоки: BuildingWall/BuildingCorner,
//    GreenBlock/YellowBlock/RedBlock, MilitaryBlock(Frame),
//    QuadBlock, Support - зеркала WorldCellChecks.isPackBlock/isBuildingBlock.
//    BuildingRoad тут НЕ блокер: это дорога внутри пака, по ней роботы
//    ездят (в cells.json она isEmpty/passable, WASD на неё пускает) - клик
//    по ней строит обычный маршрут по проходимости из данных клетки.
//    BuildingDoor - отдельное правило: клик ПРЯМО по двери ведёт робота
//    на неё (осознанный вход в пак), в маршрутах НАЧАТЫХ вне пака дверь
//    обходится: проход через неё открывает окно пака, и случайное движение
//    мышкой не должно его дёргать. Если робот уже стоит на паковой клетке
//    (дверь или дорога пака) - это выход из пака: двери на маршруте
//    разрешены, иначе из пака мышкой не выйти.
// Клик по непроходимой клетке пака/стройблока ведёт робота к ближайшей
// достижимой клетке рядом с целью (подойти к паку вплотную).
public static class ClickPathfinder
{
    private const int StepCost = 1;
    private const int DigCost = 24;
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

        // Клетка цели: >0 - обычный маршрут до неё (ломаемая сплошная тоже -
        // бурим до неё); <0 и это пак/стройблок - ведём к ближайшей достижимой
        // клетке рядом (подойти к паку); <0 у неломаемой породы или
        // незагруженной клетки - маршрута нет. Дверь пака разрешена только
        // как явная цель клика (allowBuildingDoor: true).
        CellType targetCell = storage.GetCell(target.x, target.y);
        int targetCost = CellCost(targetCell, mapDataProvider, allowBuildingDoor: true);
        // Клик по двери: вход в пак осознанный - промежуточные двери на пути
        // к ней тоже разрешены (двери бывают двухклеточными).
        bool targetIsDoor = targetCell == CellType.BuildingDoor;
        // Старт на паковой клетке (дверь или дорога пака): робот уже в паке -
        // это ВЫХОД, а не вход. Двери на маршруте разрешены, иначе из пака
        // мышкой не выйти (стоя на двери, наружу ведёт только соседняя дверь).
        CellType startCell = storage.GetCell(start.x, start.y);
        bool startInPack = startCell is CellType.BuildingRoad or CellType.BuildingDoor;
        if (targetCost < 0 && !IsPackBlock(targetCell) && !IsBuildingBlock(targetCell))
        {
            return null;
        }

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
        open.Push(Heuristic(start, target), 0, startIndex);

        int expanded = 0;
        bool found = false;

        // Ближайшая к цели достигнутая клетка: если сама цель непроходима
        // (клик по паку), маршрут строится до неё.
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
            int currentH = Heuristic(currentPosition, target);
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

                    // Соседние клетки: дверь пака проходима ТОЛЬКО когда она
                    // сама - цель маршрута (прямой клик по двери), когда цель
                    // тоже дверь (двухклеточные двери) или когда робот уже
                    // стоит на паковой клетке (выход из пака). Иначе дверь -
                    // обход: проход через неё открывает окно пака, случайные
                    // маршруты не должны дёргать его при движении мышкой.
                    int neighbor = ny * width + nx;
                    bool neighborIsTarget = neighbor == targetIndex;
                    bool allowDoor = neighborIsTarget || targetIsDoor || startInPack;
                    int cellCost = CellCost(storage, mapDataProvider, nx, ny, allowBuildingDoor: allowDoor);
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
                    open.Push(newCost + Heuristic(neighborPos, target), newCost, neighbor);
                }
            }
        }

        // Нашли цель - идём до неё; не нашли (клик по паку) - до ближайшей
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

    // Стоимость входа в клетку: -1 непроходимо насовсем (обход), StepCost -
    // можно идти, DigCost - сплошная ломаемая (бурим по дороге).
    // allowBuildingDoor: дверь пака проходима только когда она сама - цель
    // клика; в любых других маршрутах она обходится (иначе мышинное движение
    // по КД проваливалось бы в пак и открывало его окно).
    private static int CellCost(IWorldDataStorage storage, IMapDataProvider mapDataProvider, int x, int y, bool allowBuildingDoor)
    {
        return CellCost(storage.GetCell(x, y), mapDataProvider, allowBuildingDoor);
    }

    private static int CellCost(CellType cell, IMapDataProvider mapDataProvider, bool allowBuildingDoor)
    {
        if (cell == CellType.Unloaded)
        {
            return -1;
        }

        // Паки и строительные блоки: бурением не убираются - только обход.
        if (IsPackBlock(cell) || IsBuildingBlock(cell))
        {
            return -1;
        }

        // Дверь пака: только как явная цель клика, иначе - обход.
        if (cell == CellType.BuildingDoor && !allowBuildingDoor)
        {
            return -1;
        }

        // Федеральный блок: в игре это проходимая клетка, хотя в cells.json
        // у него нет isEmpty - без этой ветки путь через него не строится.
        if (cell == CellType.FedBlock)
        {
            return StepCost;
        }

        if (PlayerMovementValidator.IsPassable(cell, mapDataProvider.GetCellConfig(cell)))
        {
            return StepCost;
        }

        // Буром берутся только is_diggable-клетки. Черноскал/Красноскал и пр.
        // помечены сервером как Breakable (is_destructible - бомбой), но буром
        // не копаются - явный список из cells.json.
        if (IsUndiggable(cell))
        {
            return -1;
        }

        if (!mapDataProvider.GetCellConfig(cell).Properties.HasFlag(CellConfigProperties.Breakable))
        {
            return -1;
        }

        return DigCost;
    }

    // Сплошные клетки без is_diggable в cells.json сервера: бур их не берёт.
    // Черноскал раньше назывался NiggerRock (легаси-имя),
    // в актуальном протоколе переименован в BlackRock (114).
    private static bool IsUndiggable(CellType cell) =>
        cell is CellType.BlackRock          // Черноскал (114)
            or CellType.LivingBlackRock     // Чёрная скала живородящая (115)
            or CellType.RedRock             // Красноскал (117)
            or CellType.HypnoRock           // Гипноскал (119)
            or CellType.Skull               // Череп (88)
            or CellType.SuperRainbow;       // Суперрадуга (87)

    // Пак-здания (зеркало WorldCellChecks.isPackBlock на сервере).
    // BuildingRoad и BuildingDoor - не блокеры, а проходимые клетки пака
    // (дорога и дверь; в cells.json у обеих isEmpty/passable): их
    // проходимость решает PlayerMovementValidator.IsPassable - на них можно
    // и кликать, и заходить по WASD, в том числе внутрь пака через дверь.
    private static bool IsPackBlock(CellType cell) =>
        cell is CellType.BuildingWall or CellType.BuildingCorner;

    // Строительные блоки (зеркало WorldCellChecks.isBuildingBlock на сервере).
    private static bool IsBuildingBlock(CellType cell) =>
        cell is CellType.GreenBlock or CellType.YellowBlock or CellType.RedBlock
            or CellType.MilitaryBlockFrame or CellType.MilitaryBlock
            or CellType.Support or CellType.QuadBlock;

    private static int Heuristic(Vector2Int from, Vector2Int to)
    {
        // Манхэттен * StepCost: допустимая эвристика (бурение только дороже).
        return (Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y)) * StepCost;
    }

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
