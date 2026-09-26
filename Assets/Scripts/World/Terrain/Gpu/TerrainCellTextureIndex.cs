#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>
/// Какие клетки окна надо перечитать, когда приехала текстура типа.
/// </summary>
///
/// Слоя два, и у каждого свой тип в одной и той же клетке: фон берёт тип из
/// карты заливки, передний план — из карты мира. Текстура приезжает для типа,
/// поэтому оба индекса спрашиваются отдельно, а результат склеивается: квад
/// перечитывается один раз, даже если оба его слоя одного типа.
internal sealed class TerrainCellTextureIndex
{
    private readonly CellTypeSpatialIndex _background = new();
    private readonly CellTypeSpatialIndex _foreground = new();
    private readonly List<int> _textureRefreshQuads = [];
    private readonly HashSet<long> _textureRefreshMarks = [];
    private readonly List<(long Key, CellType Type)> _textureRefreshEntries = [];
    private int _textureRefreshWindowX;
    private int _textureRefreshWindowY;

    public List<int> TextureRefreshQuads => _textureRefreshQuads;

    /// <summary>
    /// Задать размер окна обоим слоям. Индексы адресуют клетку кольцом по
    /// этому размеру, поэтому его смена сбрасывает их.
    /// </summary>
    public void EnsureWindow(int width, int height)
    {
        _background.EnsureWindow(width, height);
        _foreground.EnsureWindow(width, height);
    }

    public void Clear()
    {
        _background.Clear();
        _foreground.Clear();
        _textureRefreshQuads.Clear();
        _textureRefreshMarks.Clear();
        _textureRefreshEntries.Clear();
    }

    // Types must already match the rendered layers, including Road under
    // passable building cells and Empty under exposed ground.
    public void UpdateCell(int gridX, int unityY, CellType backgroundType, CellType foregroundType)
    {
        long key = TerrainCoordinateKey.Pack(gridX, unityY);
        _background.Set(key, backgroundType);
        _foreground.Set(key, foregroundType);
    }

    /// <summary>
    /// Собрать квады окна, задетые перечисленными типами. Список отсортирован:
    /// сборка идёт по нему подряд и метит соседние клетки одним прямоугольником.
    /// </summary>
    public void CollectRefreshQuads(
        in TerrainCellTypeSet cellTypes,
        int minX,
        int minY,
        int width,
        int height)
    {
        _textureRefreshQuads.Clear();
        _textureRefreshMarks.Clear();
        _textureRefreshEntries.Clear();
        _textureRefreshWindowX = minX;
        _textureRefreshWindowY = minY;

        // Один проход на слой вместо прохода на каждый приехавший тип: типов
        // в пачке бывают десятки, а слотов в окне всегда столько же.
        _background.CollectEntries(cellTypes, _textureRefreshEntries);
        _foreground.CollectEntries(cellTypes, _textureRefreshEntries);
        for (int index = 0; index < _textureRefreshEntries.Count; index++)
        {
            AddTextureRefreshQuad(_textureRefreshEntries[index].Key, width, height);
        }

        _textureRefreshQuads.Sort();
    }

    public void CollectRefreshQuads(
        HashSet<CellType> cellTypes,
        int minX,
        int minY,
        int width,
        int height)
    {
        TerrainCellTypeSet snapshot = TerrainCellTypeSet.Capture(cellTypes);
        CollectRefreshQuads(snapshot, minX, minY, width, height);
    }


    private void AddTextureRefreshQuad(long key, int width, int height)
    {
        int worldX = TerrainCoordinateKey.UnpackX(key);
        int worldY = TerrainCoordinateKey.UnpackY(key);
        if ((uint)(worldX - _textureRefreshWindowX) >= (uint)width ||
            (uint)(worldY - _textureRefreshWindowY) >= (uint)height)
        {
            return;
        }

        if (!_textureRefreshMarks.Add(key))
        {
            return;
        }

        _textureRefreshQuads.Add(
            ((worldX - _textureRefreshWindowX) * height) + (worldY - _textureRefreshWindowY));
    }
}
