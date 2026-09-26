#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;
// Реализует ICachedCellDataProvider сам: заливке фона нужен тип и свойства
// клетки, и брать их больше неоткуда. Раньше переходником служил
// TerrainRenderer — MonoBehaviour в роли адаптера над собственным полем.
public class TerrainCellCache : ITerrainCellDataSource
{
    private readonly TerrainRingGrid<CachedCellData> _cellCache = new();
    private int _cacheMinX = int.MinValue;
    private int _cacheMinY = int.MinValue;
    private int _cacheWidth;
    private int _cacheHeight;
    private readonly CellTypeSpatialIndex _cellsByType = new();
    private readonly List<(long Key, CellType Type)> _refreshEntries = [];
    private readonly TerrainCellMetadataCache _metadataCache = new();

    // Снятое главным потоком и ещё не применённое: куда встанет кэш и какие
    // клетки в каких прямоугольниках (в координатах кэша ПОСЛЕ сдвига).
    //
    // Разделение по потокам проходит ровно здесь. Главный поток читает
    // хранилище — это только байты типов, по байту на клетку, — и разрешает
    // метаданные немногих встретившихся типов. Раскладка в кольцо,
    // 80-байтные записи клеток и индекс типов — рабочий поток. Прежде всё это
    // шло на главном: сдвиг на квант по двум осям стоил ~7 мс кадра.
    private readonly List<RectInt> _pendingRects = [];
    private readonly HashSet<CellType> _pendingRefreshTypes = [];
    private readonly bool[] _seenTypes = new bool[256];
    private CellType[] _pendingTypes = [];
    private int _pendingTypeCount;
    private bool _hasPending;
    private bool _pendingFull;
    private int _pendingMinX;
    private int _pendingMinY;
    private int _pendingDeltaX;
    private int _pendingDeltaY;

    private static CachedCellData _UnloadedCellData => new()
    {
        State = TerrainCellState.Unloaded,
        Type = CellType.Unloaded,
        AtlasIndex = -1,
    };

    public int CacheMinX => _hasPending ? _pendingMinX : _cacheMinX;
    public int CacheMinY => _hasPending ? _pendingMinY : _cacheMinY;
    public int CacheWidth => _cacheWidth;
    public int CacheHeight => _cacheHeight;

    public void EnsureCapacity(int width, int height)
    {
        _cacheWidth = width + 2;
        _cacheHeight = height + 2;
        if (_cellCache.Width != _cacheWidth || _cellCache.Height != _cacheHeight)
        {
            _cellCache.EnsureSize(_cacheWidth, _cacheHeight);
            _cellsByType.EnsureWindow(_cacheWidth, _cacheHeight);
            _cellsByType.Clear();
        }
    }

    /// <summary>Начать проход разрешения метаданных (см. TerrainCellMetadataCache).</summary>
    public void BeginMetadataPass() => _metadataCache.BeginPass();

    public void ClearCaches()
    {
        _metadataCache.Clear();
    }

    public CachedCellInfo GetCell(int x, int y)
    {
        CachedCellData data = GetCellData(x, y);
        return new CachedCellInfo { Type = data.Type, Properties = data.Properties };
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

    // ---- Главный поток: снятие шага --------------------------------------

    /// <summary>Снять всё окно кэша с началом (minX - 1, minY - 1).</summary>
    public void CaptureFull(int minX, int minY, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null)
        {
            throw new ArgumentNullException(nameof(wtm));
        }

        if (atlases == null)
        {
            throw new ArgumentNullException(nameof(atlases));
        }

        if (mm == null || mapStorage == null || !mapStorage.IsReady || mapStorage.CellLayer is not { } layer)
        {
            return;
        }

        BeginCapture(minX - 1, minY - 1, full: true, 0, 0);
        CaptureRect(new RectInt(0, 0, _cacheWidth, _cacheHeight), layer, mm.WorldWidth, mm.WorldHeight);
        wtm.RequestTexture(CellType.Empty);
    }

    /// <summary>Снять вошедшие полосы сдвига кэша на (dx, dy).</summary>
    public void CaptureScroll(int dx, int dy, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null)
        {
            throw new ArgumentNullException(nameof(wtm));
        }

        if (atlases == null)
        {
            throw new ArgumentNullException(nameof(atlases));
        }

        if (mm == null || mapStorage == null || !mapStorage.IsReady || mapStorage.CellLayer is not { } layer)
        {
            return;
        }

        BeginCapture(CacheMinX + dx, CacheMinY + dy, full: false, dx, dy);

        // Кайма нулевая: клетка кэша читается сама по себе, соседей здесь
        // никто не смотрит. Полоса по y берёт только ту ширину, которую не
        // накрыла полоса по x, — иначе угол заполнялся бы дважды.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(_cacheWidth, _cacheHeight, dx, dy);
        CaptureRect(bands.ColumnBand, layer, mm.WorldWidth, mm.WorldHeight);
        CaptureRect(bands.RowBand, layer, mm.WorldWidth, mm.WorldHeight);
        wtm.RequestTexture(CellType.Empty);
    }

    /// <summary>Снять изменённые клетки мира (координаты Unity) в границах кэша.</summary>
    public void CaptureRegion(int gridMinX, int unityMinY, int width, int height, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null || atlases == null || mm == null || mapStorage == null || !mapStorage.IsReady ||
            mapStorage.CellLayer is not { } layer)
        {
            return;
        }

        if (!_hasPending)
        {
            BeginCapture(_cacheMinX, _cacheMinY, full: false, 0, 0);
        }

        int startX = Mathf.Clamp(gridMinX - _pendingMinX, 0, _cacheWidth);
        int endX = Mathf.Clamp(gridMinX + width - _pendingMinX, 0, _cacheWidth);
        int startY = Mathf.Clamp(unityMinY - _pendingMinY, 0, _cacheHeight);
        int endY = Mathf.Clamp(unityMinY + height - _pendingMinY, 0, _cacheHeight);
        CaptureRect(new RectInt(startX, startY, endX - startX, endY - startY), layer, mm.WorldWidth, mm.WorldHeight);
    }

    /// <summary>
    /// Типы, у которых приехала текстура: метаданные перерешаются сейчас, а
    /// клетки этих типов в кэше переписываются при применении шага.
    /// </summary>
    ///
    /// Тип перерешается, даже если его клеток в окне нет: он мог остаться
    /// фоном заливки, а сборка метаданные фона только читает.
    public void CaptureTextureRefresh(
        HashSet<CellType> cellTypes,
        IMapDataProvider mapManager,
        ITextureService textureService,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (cellTypes.Count == 0)
        {
            return;
        }

        if (!_hasPending)
        {
            BeginCapture(_cacheMinX, _cacheMinY, full: false, 0, 0);
        }

        _metadataCache.Invalidate(cellTypes);
        _metadataCache.BeginPass();
        foreach (CellType cellType in cellTypes)
        {
            if (cellType != CellType.Unloaded)
            {
                _metadataCache.GetMetadata(cellType, mapManager, textureService, atlases);
            }

            _pendingRefreshTypes.Add(cellType);
        }
    }

    /// <summary>
    /// Разрешить метаданные всех снятых типов и подстановок фона (Road под
    /// зданием, Empty под пустой клеткой). Последнее, что главный поток
    /// делает перед передачей кэша рабочему.
    /// </summary>
    public void ResolveCapturedTypes(
        IMapDataProvider mapData,
        ITextureService textureService,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        _metadataCache.BeginPass();
        _metadataCache.GetMetadata(CellType.Empty, mapData, textureService, atlases);
        _metadataCache.GetMetadata(CellType.Road, mapData, textureService, atlases);
        for (int type = 0; type < _seenTypes.Length; type++)
        {
            if (!_seenTypes[type])
            {
                continue;
            }

            _seenTypes[type] = false;
            if ((CellType)type != CellType.Unloaded)
            {
                _metadataCache.GetMetadata((CellType)type, mapData, textureService, atlases);
            }
        }
    }

    // ---- Рабочий поток: применение шага -----------------------------------

    /// <summary>
    /// Разложить снятое в кольцо: сдвиг, клетки прямоугольников, перечитанные
    /// типы. Читает метаданные, разрешённые главным потоком до старта, и
    /// ничего не разрешает сам.
    /// </summary>
    public void ApplyPendingCapture()
    {
        if (!_hasPending)
        {
            return;
        }

        if (_pendingFull)
        {
            _cellsByType.Clear();
        }
        else if (_pendingDeltaX != 0 || _pendingDeltaY != 0)
        {
            // Снимать уехавшие клетки с индекса типов отдельным проходом не
            // нужно: индекс адресует клетку кольцом по размеру окна, и слот
            // уехавшей клетки — это ровно слот той, что встала на её место.
            _cellCache.Scroll(_pendingDeltaX, _pendingDeltaY);
        }

        _cacheMinX = _pendingMinX;
        _cacheMinY = _pendingMinY;

        int cursor = 0;
        for (int index = 0; index < _pendingRects.Count; index++)
        {
            RectInt rect = _pendingRects[index];
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    CellType type = _pendingTypes[cursor++];
                    SetCachedData(x, y, CreateFromCapturedType(type));
                }
            }
        }

        if (_pendingRefreshTypes.Count > 0)
        {
            // Один проход по индексу вместо прохода по окну на каждый тип.
            _refreshEntries.Clear();
            _cellsByType.CollectEntries(_pendingRefreshTypes, _refreshEntries);
            for (int index = 0; index < _refreshEntries.Count; index++)
            {
                (long key, CellType cellType) = _refreshEntries[index];
                int x = TerrainCoordinateKey.UnpackX(key) - _cacheMinX;
                int y = TerrainCoordinateKey.UnpackY(key) - _cacheMinY;
                if ((uint)x < (uint)_cacheWidth && (uint)y < (uint)_cacheHeight)
                {
                    _cellCache[x, y] = CreateFromCapturedType(cellType);
                }
            }
        }

        ClearPending();
    }

    // ---- Синхронные обёртки: снять и тут же применить ---------------------
    //
    // Тот же путь, что у фоновой сборки, в одном потоке. Ими пользуются тесты
    // и бенчмарки; игра снимает и применяет на разных потоках.

    public void PopulateFull(int minX, int minY, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        CaptureFull(minX, minY, mapStorage, mm, wtm, atlases);
        ResolveCapturedTypes(mm, wtm, atlases);
        ApplyPendingCapture();
    }

    public void ScrollAndFill(int dx, int dy, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        CaptureScroll(dx, dy, mapStorage, mm, wtm, atlases);
        ResolveCapturedTypes(mm, wtm, atlases);
        ApplyPendingCapture();
    }

    public void RefreshTextureMetadata(
        HashSet<CellType> cellTypes,
        IMapDataProvider mapManager,
        ITextureService textureService,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        CaptureTextureRefresh(cellTypes, mapManager, textureService, atlases);
        ApplyPendingCapture();
    }

    // Разрешение типа: главный поток. Пишет в кэш и дозаказывает текстуру.
    public CellMetadata GetMetadata(CellType type, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases) =>
        _metadataCache.GetMetadata(type, mm, wtm, atlases);

    // Чтение уже разрешённого типа: этим и только этим пользуется сборка
    // клетки, в том числе из рабочих потоков.
    public ITerrainMetadataLookup MetadataLookup => _metadataCache;

    public bool TryGet(CellType type, out CellMetadata metadata) =>
        _metadataCache.TryGet(type, out metadata);

    public CachedCellData CreateCachedData(CellType type, CellMetadata meta) =>
        _metadataCache.CreateCachedData(type, meta);

    private void BeginCapture(int minX, int minY, bool full, int dx, int dy)
    {
        _hasPending = true;
        _pendingFull |= full;
        _pendingMinX = minX;
        _pendingMinY = minY;
        _pendingDeltaX = dx;
        _pendingDeltaY = dy;
    }

    private void CaptureRect(RectInt rect, IWorldLayer<CellType> layer, int worldWidth, int worldHeight)
    {
        if (rect.width <= 0 || rect.height <= 0)
        {
            return;
        }

        int needed = _pendingTypeCount + (rect.width * rect.height);
        if (_pendingTypes.Length < needed)
        {
            Array.Resize(ref _pendingTypes, Math.Max(needed, _pendingTypes.Length * 2));
        }

        _pendingRects.Add(rect);
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            int gridX = _pendingMinX + x;
            int lastChunkIndex = -1;
            CellType[]? currentChunk = null;
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                CellType type = GetCellType(
                    gridX, _pendingMinY + y, worldWidth, worldHeight, layer,
                    ref lastChunkIndex, ref currentChunk);
                _pendingTypes[_pendingTypeCount++] = type;
                _seenTypes[(byte)type] = true;
            }
        }
    }

    private void ClearPending()
    {
        _pendingRects.Clear();
        _pendingRefreshTypes.Clear();
        _pendingTypeCount = 0;
        _hasPending = false;
        _pendingFull = false;
        _pendingDeltaX = 0;
        _pendingDeltaY = 0;
    }

    // Метаданные разрешены главным потоком до старта: промах — дефект
    // подготовки, а не повод рисовать подделку.
    private CachedCellData CreateFromCapturedType(CellType type)
    {
        if (type == CellType.Unloaded)
        {
            return _UnloadedCellData;
        }

        if (!_metadataCache.TryGet(type, out CellMetadata metadata))
        {
            throw new InvalidOperationException(
                $"Terrain metadata for cell type '{type}' was not resolved before the cache was applied.");
        }

        return _metadataCache.CreateCachedData(type, metadata);
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

    private void SetCachedData(int x, int y, CachedCellData data)
    {
        // Снимать клетку с прежнего типа отдельным флагом больше не нужно:
        // индекс адресует слот кольцом и сам видит, кто в слоте был.
        _cellsByType.Set(
            TerrainCoordinateKey.Pack(_cacheMinX + x, _cacheMinY + y),
            data.Type);
        _cellCache[x, y] = data;
    }
}
