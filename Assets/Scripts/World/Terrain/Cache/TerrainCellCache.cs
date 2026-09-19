#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;
public class TerrainCellCache
{
    private readonly TerrainRingGrid<CachedCellData> _cellCache = new();
    private int _cacheMinX = int.MinValue;
    private int _cacheMinY = int.MinValue;
    private int _cacheWidth;
    private int _cacheHeight;
    private readonly Dictionary<CellType, HashSet<long>> _cellsByType = [];
    private readonly Dictionary<long, CellType> _cellTypeByCoordinate = [];

    // IsPopulated flag lives inside CellMetadata itself — one array instead of two
    private readonly CellMetadata[] _metadataLookup = new CellMetadata[65536];

    private static CachedCellData _UnloadedCellData => new()
    {
        State = TerrainCellState.Unloaded,
        Type = CellType.Unloaded,
        AtlasIndex = -1,
    };

    public int CacheMinX => _cacheMinX;
    public int CacheMinY => _cacheMinY;
    public int CacheWidth => _cacheWidth;
    public int CacheHeight => _cacheHeight;

    public void EnsureCapacity(int width, int height)
    {
        _cacheWidth = width + 2;
        _cacheHeight = height + 2;
        if (_cellCache.Width != _cacheWidth || _cellCache.Height != _cacheHeight)
        {
            _cellCache.EnsureSize(_cacheWidth, _cacheHeight);
            _cellsByType.Clear();
            _cellTypeByCoordinate.Clear();
        }
    }

    public void ClearCaches()
    {
        // Array.Clear zeros all bytes → IsPopulated = false for every entry (bool default = false).
        // Faster than a manual loop: runtime uses SIMD memset internally.
        Array.Clear(_metadataLookup, 0, _metadataLookup.Length);
    }

    public void RefreshTextureMetadata(
        HashSet<CellType> cellTypes,
        MapManager mapManager,
        ITextureService textureService,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        foreach (CellType cellType in cellTypes)
        {
            int index = (int)cellType;
            if ((uint)index < (uint)_metadataLookup.Length)
            {
                _metadataLookup[index].IsPopulated = false;
            }
        }

        foreach (CellType cellType in cellTypes)
        {
            if (!_cellsByType.TryGetValue(cellType, out HashSet<long>? cells))
            {
                continue;
            }

            CellMetadata metadata = GetMetadata(cellType, mapManager, textureService, atlases);
            foreach (long key in cells)
            {
                int x = UnpackX(key) - _cacheMinX;
                int y = UnpackY(key) - _cacheMinY;
                if ((uint)x < (uint)_cacheWidth && (uint)y < (uint)_cacheHeight)
                {
                    _cellCache[x, y] = CreateCachedData(cellType, metadata);
                }
            }
        }
    }

    public CachedCellData GetCellData(int x, int y)
    {
        if (x < 0 || x >= _cacheWidth || y < 0 || y >= _cacheHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Terrain cell cache index ({x}, {y}) is outside {_cacheWidth}x{_cacheHeight}.");
        }

        return _cellCache[x, y];
    }

    public void PopulateFull(int minX, int minY, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null)
        {
            throw new ArgumentNullException(nameof(wtm));
        }

        if (atlases == null)
        {
            throw new ArgumentNullException(nameof(atlases));
        }

        if (mm == null || mapStorage == null || !mapStorage.IsReady)
        {
            return;
        }

        int worldWidth = mm.WorldWidth;
        int worldHeight = mm.WorldHeight;
        var layer = mapStorage.CellLayer;
        if (layer == null)
        {
            return;
        }

        _cacheMinX = minX - 1;
        _cacheMinY = minY - 1;
        _cellsByType.Clear();
        _cellTypeByCoordinate.Clear();

        for (int x = 0; x < _cacheWidth; x++)
        {
            int gridX = _cacheMinX + x;
            int lastChunkIndex = -1;
            CellType[]? currentChunk = null;

            for (int y = 0; y < _cacheHeight; y++)
            {
                int unityY = _cacheMinY + y;
                CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                if (type == CellType.Unloaded)
                {
                    SetCachedData(x, y, _UnloadedCellData, removePrevious: false);
                    continue;
                }

                var meta = GetMetadata(type, mm, wtm, atlases);
                SetCachedData(x, y, CreateCachedData(type, meta), removePrevious: false);
            }
        }

        wtm.RequestTexture(CellType.Empty);
    }

    public void UpdateRegion(int gridMinX, int unityMinY, int width, int height, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null || atlases == null || mm == null || mapStorage == null || !mapStorage.IsReady)
        {
            return;
        }

        int worldWidth = mm.WorldWidth;
        int worldHeight = mm.WorldHeight;
        var layer = mapStorage.CellLayer;
        if (layer == null)
        {
            return;
        }

        int startX = Mathf.Clamp(gridMinX - _cacheMinX, 0, _cacheWidth);
        int endX = Mathf.Clamp(gridMinX + width - _cacheMinX, 0, _cacheWidth);
        int startY = Mathf.Clamp(unityMinY - _cacheMinY, 0, _cacheHeight);
        int endY = Mathf.Clamp(unityMinY + height - _cacheMinY, 0, _cacheHeight);

        for (int x = startX; x < endX; x++)
        {
            int gridX = _cacheMinX + x;
            int lastChunkIndex = -1;
            CellType[]? currentChunk = null;

            for (int y = startY; y < endY; y++)
            {
                int unityY = _cacheMinY + y;
                CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                if (type == CellType.Unloaded)
                {
                    SetCachedData(x, y, _UnloadedCellData);
                    continue;
                }

                var meta = GetMetadata(type, mm, wtm, atlases);
                SetCachedData(x, y, CreateCachedData(type, meta));
            }
        }
    }

    public void ScrollAndFill(int dx, int dy, IWorldDataStorage mapStorage, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null)
        {
            throw new ArgumentNullException(nameof(wtm));
        }

        if (atlases == null)
        {
            throw new ArgumentNullException(nameof(atlases));
        }

        if (mm == null || mapStorage == null || !mapStorage.IsReady)
        {
            return;
        }

        int worldWidth = mm.WorldWidth;
        int worldHeight = mm.WorldHeight;
        var layer = mapStorage.CellLayer;
        if (layer == null)
        {
            return;
        }

        RemoveScrolledOutCells(dx, dy);
        _cacheMinX += dx;
        _cacheMinY += dy;

        _cellCache.Scroll(dx, dy);

        int lastChunkIndex = -1;
        CellType[]? currentChunk = null;

        void FillCell(int cx, int cy, ref int chunkIdx, ref CellType[]? chunk)
        {
            int gridX = _cacheMinX + cx;
            int unityY = _cacheMinY + cy;

            CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref chunkIdx, ref chunk);

            if (type == CellType.Unloaded)
            {
                SetCachedData(cx, cy, _UnloadedCellData);
                return;
            }

            var meta = GetMetadata(type, mm, wtm, atlases);
            SetCachedData(cx, cy, CreateCachedData(type, meta));
        }

        if (dx > 0)
        {
            for (int x = _cacheWidth - dx; x < _cacheWidth; x++)
            {
                for (int y = 0; y < _cacheHeight; y++)
                {
                    FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                }
            }
        }
        else if (dx < 0)
        {
            for (int x = 0; x < -dx; x++)
            {
                for (int y = 0; y < _cacheHeight; y++)
                {
                    FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                }
            }
        }

        if (dy > 0)
        {
            for (int y = _cacheHeight - dy; y < _cacheHeight; y++)
            {
                for (int x = 0; x < _cacheWidth; x++)
                {
                    FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                }
            }
        }
        else if (dy < 0)
        {
            for (int y = 0; y < -dy; y++)
            {
                for (int x = 0; x < _cacheWidth; x++)
                {
                    FillCell(x, y, ref lastChunkIndex, ref currentChunk);
                }
            }
        }

        wtm.RequestTexture(CellType.Empty);
    }

    private CellType GetCellType(int gridX, int unityY, int worldWidth, int worldHeight, IWorldLayer<CellType> layer, ref int lastChunkIndex, ref CellType[]? currentChunk)
    {
        if (unityY >= worldHeight)
        {
            return CellType.Unloaded;
        }

        if (gridX < 0 || gridX >= worldWidth || unityY < 0)
        {
            // The infinite redrock shell is rendered by SurfaceRenderer's
            // boundary shader. It is not terrain data and must never be
            // converted into a server CellType: doing so asks the texture
            // cache for RedRock metadata/animation outside the world and
            // can fail when the server has not configured that cell type.
            return CellType.Unloaded;
        }

        int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);
        if (!layer.GetChunkIndexAndLocal(gridX, serverY, out int chunkIndex, out int localIndex))
        {
            return CellType.Unloaded;
        }

        if (chunkIndex != lastChunkIndex)
        {
            ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: true);
            currentChunk = result.Status == ChunkReadStatus.Available
                ? result.Data
                : null;
            lastChunkIndex = chunkIndex;
        }

        return currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
    }

    public CellMetadata GetMetadata(CellType type, MapManager mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        int idx = (int)type;
        if ((uint)idx < (uint)_metadataLookup.Length && _metadataLookup[idx].IsPopulated)
        {
            return _metadataLookup[idx];
        }

        var config = mm.GetCellConfig(type);

        int atlasIndex = -1;
        for (int i = 0; i < atlases.Count; i++)
        {
            if (atlases[i].ContainsCell(type))
            {
                atlasIndex = i;
                break;
            }
        }

        Vector4 atlasRect = wtm.GetCellFrameRect(type);
        int frameCount = wtm.GetAnimationFrameCount(type);
        int frameSize = wtm.GetFrameSize(type);

        var meta = new CellMetadata
        {
            Properties = config.Properties,
            ReliefGroup = config.ReliefGroup,
            Distortion = config.Distortion,
            HasTileGroup = mm.TryGetTileGroup(type, out int gid),
            TileGroupID = gid,
            MinimapColor = (Color32)mm.GetCellMinimapColor(type),
            Animation = config.Animation,
            AnimationSpeed = wtm.GetAnimationSpeedForCell(type),
            AtlasRect = atlasRect,
            AtlasIndex = atlasIndex,
            UVTileSize = atlasIndex >= 0 && atlasIndex < atlases.Count
                ? (float)RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size
                : 0f,
            AnimationFrameCount = frameCount,
            FrameHeightTiles = (float)frameSize / RenderingConstants.CELL_SIZE,
            IsTextureReady = atlasIndex >= 0 && atlasRect.z > 0f,
            IsPopulated = true,
        };

        // The metadata is always fully populated (IsPopulated = true) once built here.
        // Only the fast _metadataLookup cache entry is skipped while the atlas texture is
        // not yet ready, so callers fall through to RequestTexture instead of caching an
        // unready rect. The per-cell IsTextureReady flag (read by HasMissingTextures) is
        // what actually gates drawing of not-yet-loaded cells.
        if (meta.IsTextureReady && (uint)idx < (uint)_metadataLookup.Length)
        {
            _metadataLookup[idx] = meta;
        }

        if (!meta.IsTextureReady)
        {
            wtm.RequestTexture(type);
        }

        return meta;
    }

    public CachedCellData CreateCachedData(CellType type, CellMetadata meta)
    {
        return new CachedCellData
        {
            State = TerrainCellState.Loaded,
            Type = type,
            Properties = meta.Properties,
            ReliefGroup = meta.ReliefGroup,
            Distortion = meta.Distortion,
            HasTileGroup = meta.HasTileGroup,
            TileGroupID = meta.TileGroupID,
            MinimapColor = meta.MinimapColor, // Color32 = Color32, no conversion
            Animation = meta.Animation,
            AnimationSpeed = meta.AnimationSpeed,
            AtlasRect = meta.AtlasRect,
            AtlasIndex = meta.AtlasIndex,
            UVTileSize = meta.UVTileSize,
            AnimationFrameCount = meta.AnimationFrameCount,
            FrameHeightTiles = meta.FrameHeightTiles,
            IsTextureReady = meta.IsTextureReady,
        };
    }

    private void SetCachedData(int x, int y, CachedCellData data, bool removePrevious = true)
    {
        long key = PackCoordinate(_cacheMinX + x, _cacheMinY + y);
        if (removePrevious)
        {
            RemoveCellIndexKey(key);
        }
        if (data.Type == CellType.Unloaded)
        {
            _cellCache[x, y] = data;
            return;
        }

        if (!_cellsByType.TryGetValue(data.Type, out HashSet<long>? cells))
        {
            cells = [];
            _cellsByType.Add(data.Type, cells);
        }

        cells.Add(key);
        _cellTypeByCoordinate[key] = data.Type;
        _cellCache[x, y] = data;
    }

    private void RemoveScrolledOutCells(int dx, int dy)
    {
        int oldMinX = _cacheMinX;
        int oldMinY = _cacheMinY;
        if (dx > 0)
        {
            RemoveCellIndexRect(oldMinX, oldMinX + dx, oldMinY, oldMinY + _cacheHeight);
        }
        else if (dx < 0)
        {
            RemoveCellIndexRect(oldMinX + _cacheWidth + dx, oldMinX + _cacheWidth, oldMinY, oldMinY + _cacheHeight);
        }

        if (dy > 0)
        {
            RemoveCellIndexRect(oldMinX, oldMinX + _cacheWidth, oldMinY, oldMinY + dy);
        }
        else if (dy < 0)
        {
            RemoveCellIndexRect(oldMinX, oldMinX + _cacheWidth, oldMinY + _cacheHeight + dy, oldMinY + _cacheHeight);
        }
    }

    private void RemoveCellIndexRect(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                RemoveCellIndexKey(PackCoordinate(x, y));
            }
        }
    }

    private void RemoveCellIndexKey(long key)
    {
        if (!_cellTypeByCoordinate.Remove(key, out CellType previousType) ||
            !_cellsByType.TryGetValue(previousType, out HashSet<long>? cells))
        {
            return;
        }

        cells.Remove(key);
        if (cells.Count == 0)
        {
            _cellsByType.Remove(previousType);
        }
    }

    private static long PackCoordinate(int x, int y) => ((long)x << 32) | (uint)y;

    private static int UnpackX(long key) => (int)(key >> 32);

    private static int UnpackY(long key) => (int)key;

}
