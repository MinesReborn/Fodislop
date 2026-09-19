#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainCellBuilder : IDisposable
{
    private sealed class Scratch
    {
        public readonly TerrainVertex[] Vertices = new TerrainVertex[8];
        public readonly bool[] Door = new bool[1];
    }

    private readonly TerrainCellDataTextures _textures = new();
    private readonly Scratch _mainScratch = new();
    private readonly TerrainRingGrid<int> _foregroundAtlases = new();
    private readonly TerrainRingGrid<bool> _doorFlags = new();
    private readonly HashSet<int> _doorQuads = [];
    private int[] _doorQuadScratch = [];
    private readonly TerrainCellTextureIndex _textureIndex = new();
    private bool _trackDoorQuads;
    private bool _trackTextureIndex;
    private int _width;
    private int _height;
    private float _cellSize;
    private bool _doorsTouched;

    public TerrainCellDataTextures Textures => _textures;

    // Последняя сборка задела двери: накладку надо пересобрать.
    public bool DoorsTouched => _doorsTouched;

    public bool HasDoors => _doorQuads.Count > 0;

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellSize = cellSize;
        if (_width == meshWidth && _height == meshHeight && _textures.IsAllocated)
        {
            return;
        }

        _width = meshWidth;
        _height = meshHeight;
        _foregroundAtlases.EnsureSize(meshWidth, meshHeight);
        _doorFlags.EnsureSize(meshWidth, meshHeight);
        _doorQuads.Clear();
        _textureIndex.Clear();
        _textures.EnsureCapacity(meshWidth, meshHeight);
    }

    public void BuildFull(TerrainCellSources sources, int minX, int minY)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        _doorsTouched = true;
        _doorQuads.Clear();
        _trackDoorQuads = false;
        _trackTextureIndex = false;
        _textures.MarkAllDirty();
        Parallel.For(
            0,
            _width,
            static () => new Scratch(),
            (x, _, scratch) =>
            {
                for (int y = 0; y < _height; y++)
                {
                    FillCell(x, y, minX, minY, sources, scratch);
                }

                return scratch;
            },
            static _ => { });
        _trackDoorQuads = true;
        RebuildDoorQuadIndex();
        _trackTextureIndex = true;
        _textureIndex.Rebuild(sources, minX, minY, _width, _height);
    }

    public void ScrollAndBuildBand(TerrainCellSources sources, int minX, int minY, int dx, int dy)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        // Сдвиг во всё окно не оставляет ничего годного, и полосы выродились
        // бы в ту же полную сборку.
        if (Math.Abs(dx) >= _width || Math.Abs(dy) >= _height)
        {
            BuildFull(sources, minX, minY);
            return;
        }

        // Тексели по кольцевому адресу не двигаются. Двигаются только
        // локальные массивы дверей, и накладка встаёт на новые координаты.
        // Если состав дверей не изменился, renderer может компенсировать
        // сдвиг родителя без повторной выгрузки всех дверных квадов.
        _doorsTouched = false;
        int previousDoorCount = _doorQuads.Count;
        if (dx != 0 || dy != 0)
        {
            _foregroundAtlases.Scroll(dx, dy);
            _doorFlags.Scroll(dx, dy);
            ScrollDoorQuads(dx, dy);
            _doorsTouched = _doorQuads.Count != previousDoorCount;
            _textureIndex.RemoveScrolledOutCells(minX, minY, dx, dy, _width, _height);
        }

        TerrainMeshScroller.GetBandExtents(_width, dx, out int bandXStart, out int bandXLength);
        TerrainMeshScroller.GetBandExtents(_height, dy, out int bandYStart, out int bandYLength);

        if (bandXLength > 0)
        {
            FillRect(bandXStart, bandXStart + bandXLength, 0, _height, minX, minY, sources);
        }

        if (bandYLength > 0 && bandXLength < _width)
        {
            // Полоса по y берёт только ширину, не покрытую полосой по x:
            // угол иначе был бы собран дважды.
            int remainingStart = dx > 0 ? 0 : bandXLength;
            int remainingEnd = dx > 0 ? bandXStart : _width;
            if (remainingStart < remainingEnd)
            {
                FillRect(remainingStart, remainingEnd, bandYStart, bandYStart + bandYLength, minX, minY, sources);
            }
        }
    }

    public void BuildRegion(
        TerrainCellSources sources,
        int minX,
        int minY,
        int startX,
        int startY,
        int countX,
        int countY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        FillRect(
            Mathf.Clamp(startX, 0, _width),
            Mathf.Clamp(startX + countX, 0, _width),
            Mathf.Clamp(startY, 0, _height),
            Mathf.Clamp(startY + countY, 0, _height),
            minX,
            minY,
            sources);
    }

    public void BuildTextureCells(HashSet<CellType> cellTypes, TerrainCellSources sources, int minX, int minY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        _textureIndex.CollectRefreshQuads(cellTypes, minX, minY, _width, _height);
        List<int> refreshQuads = _textureIndex.TextureRefreshQuads;
        bool trackTextureIndex = _trackTextureIndex;
        _trackTextureIndex = false;
        try
        {
            for (int index = 0; index < refreshQuads.Count; index++)
            {
                int quad = refreshQuads[index];
                int x = quad / _height;
                int y = quad % _height;
                FillCell(x, y, minX, minY, sources, _mainScratch);
            }
        }
        finally
        {
            _trackTextureIndex = trackTextureIndex;
        }

        MarkTextureRefreshRuns(refreshQuads, minX, minY);
    }

    private void MarkTextureRefreshRuns(List<int> refreshQuads, int minX, int minY)
    {
        int index = 0;
        while (index < refreshQuads.Count)
        {
            int firstQuad = refreshQuads[index];
            int x = firstQuad / _height;
            int firstY = firstQuad % _height;
            int lastY = firstY;
            index++;

            while (index < refreshQuads.Count)
            {
                int nextQuad = refreshQuads[index];
                if (nextQuad / _height != x || nextQuad % _height != lastY + 1)
                {
                    break;
                }

                lastY++;
                index++;
            }

            _textures.MarkCells(
                TerrainCellDataTextures.Ring(minX + x, _width),
                TerrainCellDataTextures.Ring(minY + firstY, _height),
                1,
                lastY - firstY + 1);
        }
    }


    // Вершины накладки дверей: клеток с дверью мало, поэтому их квады
    // собираются заново по требованию, а не хранятся для всей сетки.
    public void BuildDoorOverlay(
        TerrainCellSources sources,
        int minX,
        int minY,
        List<TerrainVertex> vertices,
        List<int>[] indicesPerAtlas)
    {
        vertices.Clear();
        foreach (List<int> indices in indicesPerAtlas)
        {
            indices.Clear();
        }

        foreach (int quad in _doorQuads)
        {
            int x = quad / _height;
            int y = quad % _height;
            int atlas = _foregroundAtlases[x, y];
            if (atlas < 0 || atlas >= indicesPerAtlas.Length)
            {
                continue;
            }

            TerrainQuadBuilder.FillQuadData(
                _mainScratch.Vertices, _mainScratch.Door, _cellSize,
                x, y, minX + x, minY + y, sources.CellCache, sources.Precalc, sources.FloodFill,
                sources.WorldWidth, sources.WorldHeight, false, 4, sources.Atlases, sources.UseColorLod,
                sources.MapManager, sources.TextureService);

            int baseVertex = vertices.Count;
            for (int corner = 4; corner < 8; corner++)
            {
                vertices.Add(_mainScratch.Vertices[corner]);
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

    // Выгрузка на GPU и адрес окна для шейдера. При scroll переписываются
    // только новые узлы; полный upload остаётся для первого build и resize.
    public void Commit(
        TerrainPrecalculator precalc,
        int minX,
        int minY,
        bool incrementalGridOffsets = false,
        int dx = 0,
        int dy = 0)
    {
        if (precalc.EnableDistortion)
        {
            if (incrementalGridOffsets)
            {
                _textures.WriteGridOffsetsIncremental(precalc.GridVertexOffsets, minX, minY, dx, dy);
            }
            else
            {
                _textures.WriteGridOffsets(precalc.GridVertexOffsets, minX, minY);
            }
        }

        _textures.Apply();
        _textures.BindGlobals(_cellSize, minX, minY, precalc.EnableDistortion);
    }

    public void Dispose()
    {
        _textures.Dispose();
        _width = 0;
        _height = 0;
    }

    // Квад переднего плана даёт альфу 1 на каждом пикселе клетки, только если
    // у него есть текстура, она непрозрачна вся, цвет без прозрачности, нет
    // скругления контура и шейдер не выводит квад прозрачным.
    private static bool ForegroundCoversCell(int x, int y, int foreground, Scratch scratch, TerrainCellSources sources)
    {
        if (foreground < 0 || foreground >= sources.Atlases.Count)
        {
            return false;
        }

        ref TerrainVertex vertex = ref scratch.Vertices[4];
        const int RoundableFlag = 2;
        bool hasTexture = vertex.UV1z != 0 && Mathf.HalfToFloat(vertex.UV1z) > 0.0001f;
        bool roundable = (Mathf.RoundToInt(vertex.UV6.z) & RoundableFlag) != 0;
        if (!hasTexture || roundable || vertex.Color.a < 255 || vertex.UV3.w > 1.5f)
        {
            return false;
        }

        CellType foregroundType = sources.CellCache.GetCellData(x + 1, y + 1).Type;
        return sources.Atlases[foreground].IsFullyOpaque(foregroundType);
    }

    private bool CanBuild(TerrainCellSources sources) =>
        _textures.IsAllocated && sources.Atlases != null && sources.Atlases.Count > 0;

    private void FillRect(int startX, int endX, int startY, int endY, int minX, int minY, TerrainCellSources sources)
    {
        if (endX <= startX || endY <= startY)
        {
            return;
        }

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                FillCell(x, y, minX, minY, sources, _mainScratch);
            }
        }

        _textures.MarkCells(
            TerrainCellDataTextures.Ring(minX + startX, _width),
            TerrainCellDataTextures.Ring(minY + startY, _height),
            endX - startX,
            endY - startY);
    }

    private void FillCell(int x, int y, int minX, int minY, TerrainCellSources sources, Scratch scratch)
    {
        int gridX = minX + x;
        int unityY = minY + y;
        int quad = (x * _height) + y;
        scratch.Door[0] = false;

        int background = TerrainQuadBuilder.FillQuadData(
            scratch.Vertices, scratch.Door, _cellSize,
            x, y, gridX, unityY, sources.CellCache, sources.Precalc, sources.FloodFill,
            sources.WorldWidth, sources.WorldHeight, true, 0, sources.Atlases, sources.UseColorLod,
            sources.MapManager, sources.TextureService);
        int foreground = TerrainQuadBuilder.FillQuadData(
            scratch.Vertices, scratch.Door, _cellSize,
            x, y, gridX, unityY, sources.CellCache, sources.Precalc, sources.FloodFill,
            sources.WorldWidth, sources.WorldHeight, false, 4, sources.Atlases, sources.UseColorLod,
            sources.MapManager, sources.TextureService);

        bool door = scratch.Door[0];
        if (door || _doorFlags[x, y])
        {
            _doorsTouched = true;
        }

        _foregroundAtlases[x, y] = foreground;
        _doorFlags[x, y] = door;
        if (_trackTextureIndex)
        {
            _textureIndex.UpdateCell(
                gridX,
                unityY,
                sources.FloodFill.Buffer[x, y],
                sources.CellCache.GetCellData(x + 1, y + 1).Type);
        }
        if (_trackDoorQuads && door)
        {
            _doorQuads.Add(quad);
        }
        else if (_trackDoorQuads)
        {
            _doorQuads.Remove(quad);
        }

        int ringX = TerrainCellDataTextures.Ring(gridX, _width);
        int ringY = TerrainCellDataTextures.Ring(unityY, _height);
        TerrainCellTexels backgroundTexels = TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(0, 4), background);
        if (background >= 0 && ForegroundCoversCell(x, y, foreground, scratch, sources))
        {
            // Сплошной передний план закрывает фон целиком: смешивание
            // полностью непрозрачного пикселя не оставляет от фона ни цвета,
            // ни света, ни тени. Квад фона отбрасывается в вершинном шейдере.
            Color32 meta = backgroundTexels.Meta;
            backgroundTexels = backgroundTexels with { Meta = new Color32(meta.r, meta.g, 1, meta.a) };
        }

        _textures.SetCell(ringX, ringY, TerrainCellDataPacker.BackgroundLayer, backgroundTexels);
        _textures.SetCell(
            ringX, ringY, TerrainCellDataPacker.ForegroundLayer,
            TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(4, 4), foreground));
    }

    private void ScrollDoorQuads(int dx, int dy)
    {
        if (_doorQuads.Count == 0)
        {
            return;
        }

        if (_doorQuadScratch.Length < _doorQuads.Count)
        {
            _doorQuadScratch = new int[_doorQuads.Count];
        }

        _doorQuads.CopyTo(_doorQuadScratch);
        int previousCount = _doorQuads.Count;
        _doorQuads.Clear();

        for (int index = 0; index < previousCount; index++)
        {
            int quad = _doorQuadScratch[index];
            int x = (quad / _height) - dx;
            int y = (quad % _height) - dy;
            if ((uint)x < (uint)_width && (uint)y < (uint)_height)
            {
                _doorQuads.Add((x * _height) + y);
            }
        }
    }

    private void RebuildDoorQuadIndex()
    {
        _doorQuads.Clear();
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (_doorFlags[x, y])
                {
                    _doorQuads.Add((x * _height) + y);
                }
            }
        }
    }
}
