#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Networking.Buildings;
using Kern.World;
using MinesServer.Data;
using UnityEngine;
using VContainer.Unity;

namespace Kern.Game.Managers;

// Призрак устанавливаемого пака — поведение легаси-клиента: пока в инвентаре
// выбран пак-предмет, клетки футпринта пака подсвечиваются лаймовым в точке,
// куда сервер его поставит. Якорь повторяет серверный Inventory.TryPlacePack:
// playerPos + направление * дистанция (Up/Clans=3, Science=6, остальное=2),
// сам футпринт берётся из BuildingTemplates (CellsToPlace).
// Превью — чистый сервис (docs/architecture/SCENE_STANDARD.md §1): объект
// сцены не имеет, клетки создаются под RuntimeOwner.VFX.
public sealed class PackPlacementPreview(
    ILocalPlayerState localPlayerState,
    IInventoryState inventory,
    IMapDataProvider mapDataProvider,
    ISceneObjectFactory sceneObjects,
    WorldEntityBatchRenderer entityBatchRenderer) : ITickable, IDisposable
{
    private const string TAG = "[PackPlacementPreview]";

    // Над крышами зданий (BUILDING_ROOF_SORTING_ORDER = 600), под иконками
    // клана (610): подсветка читается поверх застройки, не перекрывая иконки.
    private const int PreviewSortingOrder = 605;

    // Клетка мира — ровно 1 юнит: CoordinateUtils ставит центры соседних
    // клеток на расстоянии 1, спрайты зданий создаются с PPU = CELL_SIZE
    // (32px -> 1 юнит). Не CELL_SIZE / PIXELS_PER_UNIT — тот коэффициент
    // даёт 2 юнита и раздувает превью вдвое.
    private const float CellUnits = 1f;

    // Небольшой зазор между клетками — подсветка читается как сетка футпринта.
    private const float CellInset = 0.1f;

    // Лаймовый призрак легаси-клиента, заметно прозрачный: фон читается
    // сквозь подсветку. Порог отсечки батча — 0.003, запас большой.
    private static readonly Color s_tint = new(0.35f, 1f, 0.15f, 0.2f);

    private readonly List<WorldEntityBatchRenderer.SpriteHandle> _cells = [];
    private readonly List<(int Dx, int Dy)> _footprint = [];

    private Sprite? _cellSprite;
    private ItemType? _previewedItem;

    public void Tick()
    {
        ItemType? selected = inventory.SelectedItem;
        ILocalPlayer? player = localPlayerState.Current;

        if (selected is null || player is null ||
            !player.HasServerPosition || !player.IsGameplayVisible ||
            mapDataProvider.WorldHeight <= 0 ||
            !BuildingTemplates.TryGetByItem(selected.Value, out PackBuilding? template) ||
            template is null)
        {
            Hide();
            return;
        }

        if (selected != _previewedItem)
        {
            _previewedItem = selected;
            CacheFootprint(template);
        }

        // Якорь установки — как на сервере: DirectionalCoords(entity, dir, dist).
        int distance = BuildingTemplates.GetAnchorDistance(template.Type);
        (int dirDx, int dirDy) = DirectionVector(player.LastDirection);
        int anchorX = player.Position.x + dirDx * distance;
        int anchorY = player.Position.y + dirDy * distance;

        EnsureCapacity(_footprint.Count);

        ushort worldWidth = mapDataProvider.WorldWidth;
        ushort worldHeight = mapDataProvider.WorldHeight;
        int used = 0;
        foreach ((int dx, int dy) in _footprint)
        {
            int x = anchorX + dx;
            int y = anchorY + dy;
            // Клетки за краем мира не рисуем — сервер такую установку отклонит.
            if (x < 0 || y < 0 || x >= worldWidth || y >= worldHeight)
            {
                continue;
            }

            WorldEntityBatchRenderer.SpriteHandle cell = _cells[used++];
            cell.Transform.position = CoordinateUtils.ServerToUnityPos(x, y, worldHeight);
            cell.SetColor(s_tint);
            cell.SetEnabled(true);
        }

        for (int i = used; i < _cells.Count; i++)
        {
            _cells[i].SetEnabled(false);
        }
    }

    public void Dispose()
    {
        foreach (WorldEntityBatchRenderer.SpriteHandle cell in _cells)
        {
            entityBatchRenderer.UnregisterSprite(cell);
            if (cell.Transform != null)
            {
                UnityEngine.Object.Destroy(cell.Transform.gameObject);
            }
        }

        _cells.Clear();
        if (_cellSprite != null)
        {
            UnityEngine.Object.Destroy(_cellSprite);
            _cellSprite = null;
        }

        Debug.Log($"{TAG} Disposed.");
    }

    private void Hide()
    {
        foreach (WorldEntityBatchRenderer.SpriteHandle cell in _cells)
        {
            cell.SetEnabled(false);
        }

        // При следующем показе футпринт пересчитывается под новый выбранный пак.
        _previewedItem = null;
    }

    private void CacheFootprint(PackBuilding template)
    {
        _footprint.Clear();
        foreach (((int x, int y) pos, CellType cell) in template.CellsToPlace())
        {
            // Легаси-клиент подсвечивал только стены: двери, угловые и
            // дороги оставались без подсветки (сверено со старым клиентом
            // на крафтере).
            if (cell is not CellType.BuildingWall)
            {
                continue;
            }

            _footprint.Add((pos.x, pos.y));
        }
    }

    private void EnsureCapacity(int count)
    {
        while (_cells.Count < count)
        {
            GameObject go = sceneObjects.Create(
                $"PackPreviewCell_{_cells.Count}", RuntimeOwner.VFX);
            go.transform.localScale = new Vector3(
                CellUnits - CellInset, CellUnits - CellInset, 1f);

            WorldEntityBatchRenderer.SpriteHandle handle =
                entityBatchRenderer.RegisterSprite(go.transform, PreviewSortingOrder);
            // Спрайт ставится методом рендерера, а не хендла: только он
            // регистрирует текстуру в мировом атласе (EnsureTextureInAtlas).
            // SetSprite на самом хендле атлас пропускает, и первая же
            // перестройка батча падает "Texture ... was not registered".
            entityBatchRenderer.SetSprite(handle, GetCellSprite());
            handle.SetEnabled(false);
            _cells.Add(handle);
        }
    }

    private Sprite GetCellSprite()
    {
        if (_cellSprite != null)
        {
            return _cellSprite;
        }

        // Белая квадратная текстура 4x4 при PPU 4 — ровно 1 юнит; масштаб
        // клетки задаётся на трансформе (CellUnits - CellInset). Формат —
        // RGBA32 в sRGB (linear: false): атлас проверяет graphicsFormat и
        // требует тот же, что у себя (R8G8B8A8_SRGB).
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            4,
            4,
            "PackPreviewCell",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        var pixels = new Color32[4 * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(255, 255, 255, 255);
        }

        texture.SetPixels32(pixels);
        // Без makeNoLongerReadable: атлас копирует текстуру GPU-копией
        // (Graphics.CopyTexture), читаемость исходника не нужна, но и не
        // мешает — текстура крошечная (4x4).
        texture.Apply(updateMipmaps: false);

        _cellSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 4f, 4f),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 4f);
        return _cellSprite;
    }

    // Совпадает с серверным Direction.NormalizedVector (Mines3Enums.cs):
    // сетка сервера растёт вниз (Y+ = юг); клиентская конвертация координат
    // (CoordinateUtils) учитывает переворот отдельно.
    private static (int Dx, int Dy) DirectionVector(Direction direction) => direction switch
    {
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        Direction.Up => (0, -1),
        Direction.Right => (1, 0),
        _ => (0, 0),
    };
}
