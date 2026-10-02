#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Где в окне стоят двери и по каким вершинам собирается накладка.
/// </summary>
///
/// Дверей в окне единицы, поэтому их квады собираются по требованию, а не
/// хранятся для всей сетки. Здесь три кольцевых поля — атлас переднего плана,
/// флаг двери и отпечаток квада — и множество номеров дверных квадов. Клетка
/// сообщает о себе одним вызовом <see cref="RecordCell"/>, сдвиг — одним
/// <see cref="Scroll"/>, а накладка строится из множества, а не из сетки.
public sealed class TerrainDoorOverlayIndex
{
    private readonly TerrainRingGrid<int> _atlases = new();
    private readonly TerrainRingGrid<bool> _flags = new();
    private readonly TerrainRingGrid<int> _fingerprints = new();
    private readonly HashSet<int> _quads = [];
    private readonly object _quadLock = new();
    private int[] _quadScratch = [];
    private int _width;
    private int _height;
    private bool _trackQuads;

    /// <summary>В окне есть хотя бы одна дверь — накладку есть что строить.</summary>
    public bool HasDoors => _quads.Count > 0;

    public void EnsureSize(int width, int height)
    {
        _width = width;
        _height = height;
        _atlases.EnsureSize(width, height);
        _flags.EnsureSize(width, height);
        _fingerprints.EnsureSize(width, height);
        _quads.Clear();
    }

    /// <summary>
    /// Полная сборка: пока она идёт, множество квадов не ведётся — его
    /// собирают одним проходом в конце.
    /// </summary>
    public void BeginFullBuild()
    {
        _quads.Clear();
        _trackQuads = false;
    }

    public void CompleteFullBuild()
    {
        _trackQuads = true;
        RebuildQuadIndex();
    }

    /// <summary>
    /// Кольцевой сдвиг: атласы, флаги и отпечатки двигаются вместе с окном, а
    /// накладка встаёт на новые координаты. Возвращается «состав дверей
    /// изменился»: если нет, renderer компенсирует сдвиг родителя без повторной
    /// выгрузки всех дверных квадов.
    /// </summary>
    public bool Scroll(int dx, int dy)
    {
        int previousDoorCount = _quads.Count;
        _atlases.Scroll(dx, dy);
        _flags.Scroll(dx, dy);
        _fingerprints.Scroll(dx, dy);
        ScrollQuads(dx, dy);
        return _quads.Count != previousDoorCount;
    }

    /// <summary>
    /// Кольцевой сдвиг не чистит вошедшую полосу: в её слотах лежат флаги
    /// уехавших клеток, и «дверь была» там врёт. Полоса обнуляется до заливки,
    /// и каждая её клетка приходит в <see cref="RecordCell"/> как новая.
    /// </summary>
    ///
    /// Обнуляется РОВНО вошедшее, без каймы соседства: клетки каймы остались
    /// на месте вместе со своими дверями, и стереть им флаг значило бы
    /// разойтись с множеством в другую сторону.
    public void ClearBand(RectInt band)
    {
        for (int x = band.xMin; x < band.xMax; x++)
        {
            for (int y = band.yMin; y < band.yMax; y++)
            {
                _flags[x, y] = false;
                _fingerprints[x, y] = 0;
            }
        }
    }

    /// <summary>
    /// Записать клетку и вернуть признак «двери задеты».
    /// </summary>
    ///
    /// Возвращается признак, а не пишется в общее поле: полная сборка зовёт
    /// это из Parallel.For, и такая запись была гонкой.
    ///
    /// «Дверь задета» — это появление, исчезновение или СМЕНА ГЕОМЕТРИИ уже
    /// стоявшей двери: накладка строится по вершинам. Просто пройти мимо двери
    /// и перезалить её клетку поводом не является, а раньше являлось — и каждый
    /// такой кадр пересобирал всю накладку заново.
    public bool RecordCell(int x, int y, int foreground, bool door, Span<TerrainVertex> foregroundVertices)
    {
        int quad = (x * _height) + y;
        bool wasDoor = _flags[x, y];
        int fingerprint = door ? DoorQuadFingerprint(foregroundVertices) : 0;
        bool doorsChanged = door != wasDoor ||
            (door && (fingerprint != _fingerprints[x, y] || foreground != _atlases[x, y]));

        _atlases[x, y] = foreground;
        _flags[x, y] = door;
        _fingerprints[x, y] = fingerprint;

        // Дверей в окне единицы, а заплатка перечитывает тысячи клеток.
        // Безусловный Remove на каждой не-двери был хешированием впустую.
        if (_trackQuads && door != wasDoor)
        {
            lock (_quadLock)
            {
                if (door)
                {
                    _quads.Add(quad);
                }
                else
                {
                    _quads.Remove(quad);
                }
            }
        }

        return doorsChanged;
    }

    /// <summary>Собрать вершины и индексы накладки по всем дверям окна.</summary>
    ///
    /// Порядок обхода множества зависит от истории вставок и удалений, а она у
    /// двух клиентов на одной клетке разная: кто как сюда шёл. Из множества
    /// выходила бы накладка с теми же квадами в другом порядке вершин. Дверей
    /// в окне десятки, сортировка ничего не стоит, а геометрия становится
    /// функцией от состояния окна, а не от пути к нему.
    public void BuildOverlay(
        TerrainCellSources sources,
        int minX,
        int minY,
        float cellSize,
        Span<TerrainVertex> foregroundScratch,
        List<TerrainVertex> vertices,
        List<int>[] indicesPerAtlas)
    {
        vertices.Clear();
        foreach (List<int> indices in indicesPerAtlas)
        {
            indices.Clear();
        }

        int doorCount = _quads.Count;
        if (_quadScratch.Length < doorCount)
        {
            _quadScratch = new int[doorCount];
        }

        _quads.CopyTo(_quadScratch);
        Array.Sort(_quadScratch, 0, doorCount);

        for (int index = 0; index < doorCount; index++)
        {
            int quad = _quadScratch[index];
            int x = quad / _height;
            int y = quad % _height;
            int atlas = _atlases[x, y];
            if (atlas < 0 || atlas >= indicesPerAtlas.Length)
            {
                continue;
            }

            TerrainQuadBuilder.FillQuad(
                sources,
                new TerrainQuadSite(x, y, minX + x, minY + y, cellSize),
                TerrainQuadLayer.Foreground,
                foregroundScratch);

            int baseVertex = vertices.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                vertices.Add(foregroundScratch[corner]);
            }

            List<int> target = indicesPerAtlas[atlas];
            target.Add(baseVertex);
            target.Add(baseVertex + 3);
            target.Add(baseVertex + 2);
            target.Add(baseVertex + 2);
            target.Add(baseVertex + 1);
            target.Add(baseVertex);
        }
    }

    // Отпечаток, а не сравнение вершин: хранить копию четырёх вершин на
    // клетку — это 336 байт там, где хватает четырёх. Совпадение отпечатка при
    // разной геометрии означало бы не пересобранную накладку, поэтому в него
    // входит всё, что накладка рисует: положение углов, цвет и упакованные
    // данные слоя.
    private static int DoorQuadFingerprint(Span<TerrainVertex> foreground)
    {
        var hash = new System.HashCode();
        for (int corner = 0; corner < 4; corner++)
        {
            // Поля, а не свойства: свойства у вершины только на запись, они
            // пакуют float в half. Хеш идёт по тому, что реально уедет на GPU.
            ref TerrainVertex vertex = ref foreground[corner];
            hash.Add(vertex.Position);
            hash.Add(vertex.Color);
            hash.Add(vertex.UV0x);
            hash.Add(vertex.UV0y);
            hash.Add(vertex.UV1x);
            hash.Add(vertex.UV1y);
            hash.Add(vertex.UV1z);
            hash.Add(vertex.UV1w);
            hash.Add(vertex.UV2x);
            hash.Add(vertex.UV2y);
            hash.Add(vertex.UV2z);
            hash.Add(vertex.UV2w);
            hash.Add(vertex.UV3);
            hash.Add(vertex.UV4x);
            hash.Add(vertex.UV4y);
            hash.Add(vertex.UV4z);
            hash.Add(vertex.UV4w);
            hash.Add(vertex.UV5x);
            hash.Add(vertex.UV5y);
            hash.Add(vertex.UV5z);
            hash.Add(vertex.UV5w);
            hash.Add(vertex.UV6);
        }

        // Ноль означает «двери здесь нет»; настоящий отпечаток не имеет права с
        // ним совпасть, иначе появление двери с таким хешем осталось бы
        // незамеченным.
        int value = hash.ToHashCode();
        return value == 0 ? 1 : value;
    }

    private void ScrollQuads(int dx, int dy)
    {
        if (_quads.Count == 0)
        {
            return;
        }

        if (_quadScratch.Length < _quads.Count)
        {
            _quadScratch = new int[_quads.Count];
        }

        _quads.CopyTo(_quadScratch);
        int previousCount = _quads.Count;
        _quads.Clear();

        for (int index = 0; index < previousCount; index++)
        {
            int quad = _quadScratch[index];
            int x = (quad / _height) - dx;
            int y = (quad % _height) - dy;
            if ((uint)x < (uint)_width && (uint)y < (uint)_height)
            {
                _quads.Add((x * _height) + y);
            }
        }
    }

    private void RebuildQuadIndex()
    {
        _quads.Clear();
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (_flags[x, y])
                {
                    _quads.Add((x * _height) + y);
                }
            }
        }
    }
}
