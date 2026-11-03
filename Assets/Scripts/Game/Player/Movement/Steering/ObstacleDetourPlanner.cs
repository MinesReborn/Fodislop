#nullable enable

using Kern.Core.Interfaces;
using Kern.Game.Player.Pathfinding;
using UnityEngine;

namespace Kern.Game.Player.Movement;

// Обход препятствия при ручном движении: локальный поиск минимальной
// цены (Дейкстра) в окне (2·Radius+1)² вокруг робота. Координаты — серверные.
//
// Состояние поиска — клетка плюс направление последнего шага: поворот стоит
// штраф, поэтому из равных по времени маршрутов берётся самый прямой.
// Цена шага — время хода по клетке (дорога выгоднее породы под ногами).
// Цель — уйти как можно дальше по удерживаемому направлению: клетка
// оценивается ценой пути плюс временем на оставшееся до края окна
// продвижение. Оставшиеся шаги неизвестны, поэтому каждый стоит двойного
// хода по клетке, на которой стоит робот: неглубокий карман сбоку
// проигрывает обходу, который действительно уводит вперёд.
// Прямой ход назад запрещён: обход идёт вбок и вперёд. Для диагонали
// продвижение считается по обеим осям, и отступ по одной допустим, если
// вторая продвигается сильнее.
// Наружу отдаётся только первый шаг: на следующем такте расчёт повторяется
// уже с новой клетки и по свежей карте.
public static class ObstacleDetourPlanner
{
    public const int Radius = 4;

    private const int Side = Radius * 2 + 1;
    private const int Cells = Side * Side;
    private const int StartHeading = 4;
    private const int Headings = 5;
    private const int States = Cells * Headings;
    private const int HeapCapacity = States * 4 + 1;

    private static readonly Vector2Int[] s_steps =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [System.ThreadStatic]
    private static int[]? s_cost;
    [System.ThreadStatic]
    private static sbyte[]? s_firstStep;
    [System.ThreadStatic]
    private static int[]? s_heapKey;
    [System.ThreadStatic]
    private static int[]? s_heapState;

    // forwardA / forwardB — удерживаемые оси в серверных координатах;
    // forwardB = zero для прямого хода. preferredFirstStep — первый шаг
    // прошлого расчёта (серверная дельта или zero). firstStep — серверная
    // дельта первого шага обхода.
    public static bool TryPlanFirstStep(
        Vector2Int start,
        Vector2Int forwardA,
        Vector2Int forwardB,
        Vector2Int preferredFirstStep,
        IWorldDataStorage storage,
        IMapDataProvider mapDataProvider,
        out Vector2Int firstStep)
    {
        firstStep = Vector2Int.zero;
        int width = mapDataProvider.WorldWidth;
        int height = mapDataProvider.WorldHeight;
        if (forwardA == Vector2Int.zero || width <= 0 || height <= 0)
        {
            return false;
        }

        bool diagonal = forwardB != Vector2Int.zero;
        int minStepMs = CellTraversalCost.MinStepMs(mapDataProvider);
        int turnPenalty = Mathf.Max(1, minStepMs / 4);
        int maxProgress = diagonal ? Radius * 2 : Radius;
        int hereMs = start.x >= 0 && start.x < width && start.y >= 0 && start.y < height
            ? CellTraversalCost.EnterCostMs(storage, mapDataProvider, start.x, start.y)
            : -1;
        int unknownStepMs = 2 * Mathf.Max(minStepMs, hereMs);

        int[] cost = s_cost ??= new int[States];
        sbyte[] first = s_firstStep ??= new sbyte[States];
        int[] heapKey = s_heapKey ??= new int[HeapCapacity];
        int[] heapState = s_heapState ??= new int[HeapCapacity];
        System.Array.Fill(cost, int.MaxValue);

        int startState = LocalIndex(Radius, Radius) * Headings + StartHeading;
        cost[startState] = 0;
        first[startState] = -1;
        int heapCount = 0;
        Push(heapKey, heapState, ref heapCount, 0, startState);

        while (heapCount > 0)
        {
            Pop(heapKey, heapState, ref heapCount, out int currentCost, out int state);
            if (currentCost > cost[state])
            {
                continue;
            }

            int cell = state / Headings;
            int heading = state % Headings;
            int lx = cell % Side;
            int ly = cell / Side;

            for (int k = 0; k < s_steps.Length; k++)
            {
                Vector2Int step = s_steps[k];
                if (!diagonal && step == -forwardA)
                {
                    continue;
                }

                int nlx = lx + step.x;
                int nly = ly + step.y;
                if (nlx < 0 || nlx >= Side || nly < 0 || nly >= Side)
                {
                    continue;
                }

                int wx = start.x + nlx - Radius;
                int wy = start.y + nly - Radius;
                if (wx < 0 || wx >= width || wy < 0 || wy >= height)
                {
                    continue;
                }

                int enter = CellTraversalCost.EnterCostMs(storage, mapDataProvider, wx, wy);
                if (enter < 0)
                {
                    continue;
                }

                int nextCost = currentCost + enter +
                    (heading != StartHeading && heading != k ? turnPenalty : 0);
                int nextState = LocalIndex(nlx, nly) * Headings + k;
                sbyte nextFirst = heading == StartHeading ? (sbyte)k : first[state];
                if (nextCost < cost[nextState] ||
                    (nextCost == cost[nextState] && IsPreferred(nextFirst, first[nextState], preferredFirstStep)))
                {
                    cost[nextState] = nextCost;
                    first[nextState] = nextFirst;
                    Push(heapKey, heapState, ref heapCount, nextCost, nextState);
                }
            }
        }

        int bestScore = int.MaxValue;
        sbyte bestFirst = -1;
        for (int state = 0; state < States; state++)
        {
            if (cost[state] == int.MaxValue || state == startState)
            {
                continue;
            }

            int cell = state / Headings;
            var offset = new Vector2Int(cell % Side - Radius, cell / Side - Radius);
            int progress = Dot(offset, forwardA) + Dot(offset, forwardB);
            if (progress < 1)
            {
                continue;
            }

            int score = cost[state] + (maxProgress - progress) * unknownStepMs;
            if (score < bestScore ||
                (score == bestScore && IsPreferred(first[state], bestFirst, preferredFirstStep)))
            {
                bestScore = score;
                bestFirst = first[state];
            }
        }

        if (bestFirst < 0)
        {
            return false;
        }

        firstStep = s_steps[bestFirst];
        return true;
    }

    private static int LocalIndex(int x, int y) => y * Side + x;

    private static int Dot(Vector2Int a, Vector2Int b) => a.x * b.x + a.y * b.y;

    private static bool IsPreferred(sbyte candidate, sbyte current, Vector2Int preferred) =>
        preferred != Vector2Int.zero &&
        candidate >= 0 && s_steps[candidate] == preferred &&
        (current < 0 || s_steps[current] != preferred);

    private static void Push(int[] keys, int[] states, ref int count, int key, int state)
    {
        int i = count++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (keys[parent] <= key)
            {
                break;
            }

            keys[i] = keys[parent];
            states[i] = states[parent];
            i = parent;
        }

        keys[i] = key;
        states[i] = state;
    }

    private static void Pop(int[] keys, int[] states, ref int count, out int key, out int state)
    {
        key = keys[0];
        state = states[0];
        count--;
        if (count == 0)
        {
            return;
        }

        int lastKey = keys[count];
        int lastState = states[count];
        int i = 0;
        while (true)
        {
            int child = i * 2 + 1;
            if (child >= count)
            {
                break;
            }

            if (child + 1 < count && keys[child + 1] < keys[child])
            {
                child++;
            }

            if (keys[child] >= lastKey)
            {
                break;
            }

            keys[i] = keys[child];
            states[i] = states[child];
            i = child;
        }

        keys[i] = lastKey;
        states[i] = lastState;
    }
}
