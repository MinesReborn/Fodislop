#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Подписки террейна на источники данных, все четыре в одном месте.
/// </summary>
///
/// Источники подменяются на ходу: хранилище переинициализируется при смене
/// мира, слой клеток появляется после WorldInit, сервис текстур — после
/// загрузки атласа. Каждая подписка поэтому перепривязывается, а не ставится
/// один раз, и снимать её надо ровно с того объекта, на который её повесили.
/// Пока это жило в рендерере, оно занимало четыре поля «на что подписаны» и
/// две почти одинаковые процедуры — подписку и отписку в OnDestroy.
public sealed class TerrainSubscriptions : IDisposable
{
    private readonly Action<int, int> _onCellChanged;
    private readonly Action<int, int, int, int> _onRegionChanged;
    private readonly Action<CellType, Texture2D> _onTextureLoaded;
    private readonly Action _onDecalAtlasLoaded;
    private readonly Action _onWorldDataLoaded;
    private readonly Action<int, int, int, int> _onChunkLoaded;
    private IWorldDataStorage? _storage;
    private ITextureService? _textureService;
    private IMapDataProvider? _mapData;
    private IWorldLayer<CellType>? _cellLayer;

    public TerrainSubscriptions(
        Action<int, int> onCellChanged,
        Action<int, int, int, int> onRegionChanged,
        Action<CellType, Texture2D> onTextureLoaded,
        Action onDecalAtlasLoaded,
        Action onWorldDataLoaded,
        Action<int, int, int, int> onChunkLoaded)
    {
        _onCellChanged = onCellChanged;
        _onRegionChanged = onRegionChanged;
        _onTextureLoaded = onTextureLoaded;
        _onDecalAtlasLoaded = onDecalAtlasLoaded;
        _onWorldDataLoaded = onWorldDataLoaded;
        _onChunkLoaded = onChunkLoaded;
    }

    /// <summary>
    /// Привязать подписки к текущим источникам. Вызывается повторно: что не
    /// изменилось, то не трогается.
    /// </summary>
    public void Bind(
        IWorldDataStorage? storage,
        ITextureService? textureService,
        IMapDataProvider? mapData)
    {
        if (!ReferenceEquals(_storage, storage))
        {
            if (_storage != null)
            {
                _storage.CellChanged -= OnCellChanged;
                _storage.RegionChanged -= OnRegionChanged;
            }

            _storage = storage;
            if (_storage != null)
            {
                _storage.CellChanged += OnCellChanged;
                _storage.RegionChanged += OnRegionChanged;
            }
        }

        if (!ReferenceEquals(_textureService, textureService))
        {
            if (_textureService != null)
            {
                _textureService.OnTextureLoaded -= OnTextureLoaded;
                _textureService.OnTerrainDecalAtlasLoaded -= OnDecalAtlasLoaded;
            }

            _textureService = textureService;
            if (_textureService != null)
            {
                _textureService.OnTextureLoaded += OnTextureLoaded;
                _textureService.OnTerrainDecalAtlasLoaded += OnDecalAtlasLoaded;
            }
        }

        if (!ReferenceEquals(_mapData, mapData))
        {
            if (_mapData != null)
            {
                _mapData.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            _mapData = mapData;
            if (_mapData != null)
            {
                _mapData.OnWorldDataLoaded += OnWorldDataLoaded;
            }
        }

        BindCellLayer(storage?.CellLayer);
    }

    public void Dispose()
    {
        Bind(null, null, null);
    }

    private void BindCellLayer(IWorldLayer<CellType>? cellLayer)
    {
        if (ReferenceEquals(_cellLayer, cellLayer))
        {
            return;
        }

        if (_cellLayer != null)
        {
            _cellLayer.ChunkLoaded -= OnChunkLoaded;
        }

        _cellLayer = cellLayer;
        if (_cellLayer != null)
        {
            _cellLayer.ChunkLoaded += OnChunkLoaded;
        }
    }

    private void OnCellChanged(int serverX, int serverY) => _onCellChanged(serverX, serverY);

    private void OnRegionChanged(int serverX, int serverY, int width, int height) =>
        _onRegionChanged(serverX, serverY, width, height);

    private void OnTextureLoaded(CellType cellType, Texture2D texture) =>
        _onTextureLoaded(cellType, texture);

    private void OnDecalAtlasLoaded() => _onDecalAtlasLoaded();

    private void OnWorldDataLoaded() => _onWorldDataLoaded();

    private void OnChunkLoaded(int serverX, int serverY, int width, int height) =>
        _onChunkLoaded(serverX, serverY, width, height);
}
