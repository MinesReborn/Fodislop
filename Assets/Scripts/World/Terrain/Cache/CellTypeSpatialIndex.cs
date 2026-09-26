#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>Мировая координата клетки одним числом.</summary>
///
/// Ключ словаря, а не координата для счёта: складывать и сравнивать его
/// нельзя, он только адресует. Отрицательные координаты штатны — мир шире
/// окна в обе стороны, поэтому y кладётся без знака, а распаковывается обратно
/// в int.
public static class TerrainCoordinateKey
{
    public static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

    public static int UnpackX(long key) => (int)(key >> 32);

    public static int UnpackY(long key) => (int)key;
}

/// <summary>
/// Какие клетки окна имеют данный тип — и какой тип у данной клетки.
/// </summary>
///
/// Нужен ровно для одного вопроса: приехала текстура типа N, какие клетки
/// перечитать. Без обратного индекса на это отвечает только проход по всему
/// окну, а текстуры приезжают пачками по мере исследования мира.
///
/// ХРАНЕНИЕ — ПЛОТНЫЙ МАССИВ ПО КОЛЬЦЕВОМУ АДРЕСУ, И ЭТО ГЛАВНОЕ В ЭТОМ ТИПЕ.
///
/// Мировая координата берётся по модулю размера окна, и каждая клетка окна
/// занимает ровно один слот — тот же приём, что у текселей и у массива
/// атласов. Сдвиг окна не стоит ничего: уехавшая клетка и приехавшая делят
/// слот, и запись новой снимает прежнюю сама, без отдельного прохода.
///
/// В слоте лежит и ключ, а не только тип. Ключ отвечает на вопрос «эта ли
/// клетка здесь сейчас»: без него снятие уже переехавшей клетки выбросило бы
/// законного жильца слота.
///
/// ПОЧЕМУ НЕТ ПРЯМОГО ИНДЕКСА «ТИП → КЛЕТКИ». Он был — словарём множеств — и
/// стоил четырёх хеш-операций на КАЖДУЮ записанную клетку: снять со старого
/// типа, добавить к новому. Записей — тысячи в кадре, на каждой полосе сдвига
/// и на каждой заплатке. А спрашивают прямой индекс только когда приезжает
/// текстура: несколько раз за загрузку мира.
///
/// Поэтому запись — две записи в массив и ни одной хеш-операции, а вопрос
/// «какие клетки этих типов» отвечается одним проходом по окну. Проход по
/// 30 тысячам слотов дешевле, чем 30 тысяч хеш-операций, и платится он тогда,
/// когда приехала текстура, а не каждый кадр.
public sealed class CellTypeSpatialIndex
{
    // Ключ, которого не бывает у клетки: Pack кладёт x в старшие 32 бита, а
    // int.MinValue как x означал бы координату за краем любого мира.
    private const long EmptySlot = long.MinValue;

    private long[] _slotKeys = [];
    private CellType[] _slotTypes = [];
    private int _width;
    private int _height;

    /// <summary>
    /// Задать размер окна. Меняется редко (окно перестраивается под освещение)
    /// и сбрасывает индекс: кольцевые адреса считаны по прежнему размеру.
    /// </summary>
    public void EnsureWindow(int width, int height)
    {
        if (_width == width && _height == height && _slotKeys.Length != 0)
        {
            return;
        }

        _width = width;
        _height = height;
        _slotKeys = new long[checked(width * height)];
        _slotTypes = new CellType[_slotKeys.Length];
        Clear();
    }

    /// <summary>
    /// Собрать клетки окна, чей тип входит в <paramref name="types"/>, одним
    /// проходом по слотам. Порядок — по слотам, то есть один и тот же для
    /// одного и того же состояния окна.
    /// </summary>
    internal void CollectEntries(
        in TerrainCellTypeSet types,
        List<(long Key, CellType Type)> into)
    {
        if (types.IsEmpty)
        {
            return;
        }

        for (int slot = 0; slot < _slotKeys.Length; slot++)
        {
            long key = _slotKeys[slot];
            if (key == EmptySlot)
            {
                continue;
            }

            CellType type = _slotTypes[slot];
            if (types.Contains(type))
            {
                into.Add((key, type));
            }
        }
    }

    public void CollectEntries(
        HashSet<CellType> types,
        List<(long Key, CellType Type)> into)
    {
        TerrainCellTypeSet snapshot = TerrainCellTypeSet.Capture(types);
        CollectEntries(snapshot, into);
    }

    public void Clear()
    {
        Array.Fill(_slotKeys, EmptySlot);
        Array.Clear(_slotTypes, 0, _slotTypes.Length);
    }

    /// <summary>
    /// Запомнить тип клетки. <see cref="CellType.Unloaded"/> не индексируется:
    /// незагруженная клетка не принадлежит никакому типу.
    /// </summary>
    public void Set(long key, CellType type)
    {
        int slot = SlotOf(key);
        if (type == CellType.Unloaded)
        {
            _slotKeys[slot] = EmptySlot;
            _slotTypes[slot] = CellType.Unloaded;
            return;
        }

        _slotKeys[slot] = key;
        _slotTypes[slot] = type;
    }

    public void Remove(long key)
    {
        int slot = SlotOf(key);
        if (_slotKeys[slot] != key)
        {
            return;
        }

        _slotKeys[slot] = EmptySlot;
        _slotTypes[slot] = CellType.Unloaded;
    }

    public void RemoveRect(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                Remove(TerrainCoordinateKey.Pack(x, y));
            }
        }
    }


    // Размер окна обязан быть задан до первой записи. Тихо ничего не делать
    // здесь нельзя: индекс молча перестал бы отвечать на «приехала текстура —
    // какие клетки перечитать», и окно осталось бы с прежними текстурами без
    // единого признака поломки.
    private int SlotOf(long key)
    {
        if (_slotKeys.Length == 0)
        {
            throw new InvalidOperationException(
                "CellTypeSpatialIndex: не задан размер окна, вызовите EnsureWindow.");
        }

        return (Ring(TerrainCoordinateKey.UnpackX(key), _width) * _height) +
            Ring(TerrainCoordinateKey.UnpackY(key), _height);
    }

    private static int Ring(int value, int size)
    {
        int remainder = value % size;
        return remainder < 0 ? remainder + size : remainder;
    }
}
