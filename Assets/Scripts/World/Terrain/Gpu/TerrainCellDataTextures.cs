#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.Core.Interfaces.Diagnostics;

namespace Kern.World.Terrain;

// Текстуры данных клетки террейна: по текселю на квад, по две строки на
// клетку (фон, передний план), включая канонические четыре угла геометрии.
//
// Адрес кольцевой: тексель клетки — её мировая координата по модулю размера
// сетки. Окно камеры покрывает ровно один полный круг, поэтому записанная
// клетка остаётся на месте, пока видна.
//
// Сборка идёт в управляемые массивы (их можно заполнять из Parallel.For).
// На GPU уходит только изменённый прямоугольник: он копируется в маленькую
// текстуру-заплатку и переносится Graphics.CopyTexture. Геометрия входит в
// тот же прямоугольник, поэтому cell-data и форма клетки не расходятся.
public sealed class TerrainCellDataTextures : IDisposable
{
    // UploadRect() always applies at least a 16x16 patch. Treat that fixed
    // setup/copy cost as texel work when choosing between many patches and
    // one full upload; this is a cost governor, not a patch-count cutoff.
    private const long PatchSetupEquivalentTexels = 16L * 16L;

    public static readonly int ColorID = Shader.PropertyToID("_TerrainCellColor");
    public static readonly int MetaID = Shader.PropertyToID("_TerrainCellMeta");
    public static readonly int AtlasRectID = Shader.PropertyToID("_TerrainCellAtlasRect");
    public static readonly int TileSizeID = Shader.PropertyToID("_TerrainCellTileSize");
    public static readonly int AnimationID = Shader.PropertyToID("_TerrainCellAnimation");
    public static readonly int WorldID = Shader.PropertyToID("_TerrainCellWorld");
    public static readonly int GlowID = Shader.PropertyToID("_TerrainCellGlow");
    public static readonly int GeometryXID = Shader.PropertyToID("_TerrainCellGeometryX");
    public static readonly int GeometryYID = Shader.PropertyToID("_TerrainCellGeometryY");
    public static readonly int GridSizeID = Shader.PropertyToID("_TerrainCellGridSize");
    public static readonly int OriginID = Shader.PropertyToID("_TerrainCellOrigin");
    public static readonly int ViewOffsetID = Shader.PropertyToID("_TerrainCellViewOffset");

    private readonly TerrainCellDataChannel<Color32> _color = new(TextureFormat.RGBA32, "TerrainCellColor");
    private readonly TerrainCellDataChannel<Color32> _meta = new(TextureFormat.RGBA32, "TerrainCellMeta");
    private readonly TerrainCellDataChannel<TerrainHalfTexel> _atlasRect = new(TextureFormat.RGBAHalf, "TerrainCellAtlasRect");
    private readonly TerrainCellDataChannel<TerrainHalfTexel> _tileSize = new(TextureFormat.RGBAHalf, "TerrainCellTileSize");
    private readonly TerrainCellDataChannel<TerrainHalfTexel> _animation = new(TextureFormat.RGBAHalf, "TerrainCellAnimation");
    private readonly TerrainCellDataChannel<Vector4> _world = new(TextureFormat.RGBAFloat, "TerrainCellWorld");
    private readonly TerrainCellDataChannel<Vector4> _glow = new(TextureFormat.RGBAFloat, "TerrainCellGlow");
    private readonly TerrainCellDataChannel<TerrainHalfTexel> _geometryX = new(TextureFormat.RGBAHalf, "TerrainCellGeometryX");
    private readonly TerrainCellDataChannel<TerrainHalfTexel> _geometryY = new(TextureFormat.RGBAHalf, "TerrainCellGeometryY");

    private readonly TerrainDirtyRegion _dirty = new();
    private readonly List<RectInt> _uploadRects = [];
    private readonly List<TerrainStagedPiece> _uploadPieces = [];

    // Кадр, в котором слот промежуточных текстур набивался последний раз.
    private readonly List<int> _stagingSlotFrames = [];
    private const int MaximumStagingSlots = 8;

    public int MeshWidth { get; private set; }

    public int MeshHeight { get; private set; }

    public bool IsAllocated => _color.Target != null;

    /// <summary>Чем была последняя выгрузка: сколько прямоугольников и текселей.</summary>
    ///
    /// Ноль прямоугольников при ненулевых текселях означает выгрузку целиком.
    /// Цена выгрузки — самая крупная незакрытая статья в модели стоимости
    /// пересборки, и без этих двух чисел из лога нельзя отличить «выгрузили
    /// полосу» от «выгрузили девять текстур целиком».
    public int LastUploadRectCount { get; private set; }

    public long LastUploadTexels { get; private set; }

    /// <summary>На сколько полосок разошлась последняя выгрузка.</summary>
    ///
    /// Прямоугольник любой формы грузится полосками постоянного размера,
    /// поэтому число полосок — это вся зависимость выгрузки от формы. Растёт
    /// линейно с высотой изменённой области и ни от чего больше не зависит.
    public int LastUploadStrips { get; private set; }

    /// <summary>Сколько из выгрузки ушло в набивку и загрузку промежуточных текстур.</summary>
    public float LastStageMs { get; private set; }

    /// <summary>Из набивки: копирование строк в промежуточную текстуру.</summary>
    public float LastStageCopyMs { get; private set; }

    /// <summary>Из набивки: загрузка промежуточной текстуры на GPU.</summary>
    public float LastStageApplyMs { get; private set; }

    private static void ResetStageCounters()
    {
        TerrainCellDataChannel<Color32>.CopyTicks = 0;
        TerrainCellDataChannel<Color32>.ApplyTicks = 0;
        TerrainCellDataChannel<TerrainHalfTexel>.CopyTicks = 0;
        TerrainCellDataChannel<TerrainHalfTexel>.ApplyTicks = 0;
        TerrainCellDataChannel<Vector4>.CopyTicks = 0;
        TerrainCellDataChannel<Vector4>.ApplyTicks = 0;
    }

    private static float TicksToMs(long ticks) =>
        (float)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    private static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);

    public static int Ring(int value, int size)
    {
        int remainder = value % size;
        return remainder < 0 ? remainder + size : remainder;
    }

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (IsAllocated && MeshWidth == meshWidth && MeshHeight == meshHeight)
        {
            return;
        }

        Dispose();
        MeshWidth = meshWidth;
        MeshHeight = meshHeight;
        int height = meshHeight * TerrainCellDataPacker.LayersPerCell;
        _color.Allocate(meshWidth, height);
        _meta.Allocate(meshWidth, height);
        _atlasRect.Allocate(meshWidth, height);
        _tileSize.Allocate(meshWidth, height);
        _animation.Allocate(meshWidth, height);
        _world.Allocate(meshWidth, height);
        _glow.Allocate(meshWidth, height);
        _geometryX.Allocate(meshWidth, height);
        _geometryY.Allocate(meshWidth, height);
        // Every channel allocated staging slot zero above. Register it now so
        // the first patch reuses that slot instead of reporting a new one.
        _stagingSlotFrames.Add(int.MinValue);
        _dirty.Reset(meshWidth, meshHeight);
    }

    // Полная сборка пишет клетки из нескольких потоков: прямоугольник по
    // ним не копится, выгружается всё.
    public void MarkAllDirty() => _dirty.MarkAll();

    // Прямоугольник клеток в кольцевых координатах; на шве кольца он
    // разрезается, чтобы не растягиваться на всю текстуру.
    public void MarkCells(int ringX, int ringY, int width, int height) =>
        _dirty.MarkCells(ringX, ringY, width, height);

    public void SetCell(int ringX, int ringY, int layer, TerrainCellTexels texels)
    {
        int row = (ringY * TerrainCellDataPacker.LayersPerCell) + layer;
        int index = (row * MeshWidth) + ringX;
        _color.Data[index] = texels.Color;
        _meta.Data[index] = texels.Meta;
        _atlasRect.Data[index] = texels.AtlasRect;
        _tileSize.Data[index] = texels.TileSize;
        _animation.Data[index] = texels.Animation;
        _world.Data[index] = texels.World;
        _glow.Data[index] = texels.Glow;
        _geometryX.Data[index] = texels.GeometryX;
        _geometryY.Data[index] = texels.GeometryY;
    }

    // Снимок текселя из управляемых массивов. Это ровно то, что уходит на
    // GPU в Apply(): ни одного преобразования между этим чтением и загрузкой
    // нет. Нужен тестам сборки как сравнимый результат.
    internal TerrainCellTexels GetCell(int ringX, int ringY, int layer)
    {
        int row = (ringY * TerrainCellDataPacker.LayersPerCell) + layer;
        int index = (row * MeshWidth) + ringX;
        return new TerrainCellTexels(
            _color.Data[index],
            _meta.Data[index],
            _atlasRect.Data[index],
            _tileSize.Data[index],
            _animation.Data[index],
            _world.Data[index],
            _glow.Data[index],
            _geometryX.Data[index],
            _geometryY.Data[index]);
    }

    public void Apply()
    {
        if (!IsAllocated)
        {
            return;
        }

        if (_dirty.IsEmpty)
        {
            return;
        }

        int textureHeight = MeshHeight * TerrainCellDataPacker.LayersPerCell;
        long patchWork = _dirty.Area + (_dirty.Count * PatchSetupEquivalentTexels);

        // Area includes both changed texels and the fixed setup cost of each
        // patch command, so a full upload is selected when it is cheaper.
        bool full = _dirty.IsAll ||
            patchWork >= (long)MeshWidth * textureHeight ||
            SystemInfo.copyTextureSupport == CopyTextureSupport.None;
        if (full)
        {
            LastUploadRectCount = 0;
            LastUploadTexels = (long)MeshWidth * textureHeight;
            FrameEventLog.Record($"террейн: полная выгрузка {LastUploadTexels} текселей");
            LastUploadStrips = 0;
            LastStageMs = 0f;
            LastStageCopyMs = 0f;
            LastStageApplyMs = 0f;
            _color.UploadAll();
            _meta.UploadAll();
            _atlasRect.UploadAll();
            _tileSize.UploadAll();
            _animation.UploadAll();
            _world.UploadAll();
            _glow.UploadAll();
            _geometryX.UploadAll();
            _geometryY.UploadAll();
        }
        else
        {
            LastUploadRectCount = _dirty.Count;
            LastUploadTexels = _dirty.Area;
            ResetStageCounters();
            long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _uploadRects.Clear();
            for (int i = 0; i < _dirty.Count; i++)
            {
                _uploadRects.Add(_dirty[i]);
            }

            int batches = TerrainStagingPacker.Pack(
                _uploadRects,
                _color.StagingWidth,
                _color.StagingHeight,
                _uploadPieces);
            LastUploadStrips = _uploadPieces.Count;
            int start = 0;
            for (int batch = 0; batch < batches; batch++)
            {
                int end = start;
                while (end < _uploadPieces.Count && _uploadPieces[end].Batch == batch)
                {
                    end++;
                }

                int slot = AcquireStagingSlot();
                _color.Stage(slot, _uploadPieces, start, end);
                _meta.Stage(slot, _uploadPieces, start, end);
                _atlasRect.Stage(slot, _uploadPieces, start, end);
                _tileSize.Stage(slot, _uploadPieces, start, end);
                _animation.Stage(slot, _uploadPieces, start, end);
                _world.Stage(slot, _uploadPieces, start, end);
                _glow.Stage(slot, _uploadPieces, start, end);
                _geometryX.Stage(slot, _uploadPieces, start, end);
                _geometryY.Stage(slot, _uploadPieces, start, end);

                _color.CopyStaged(slot, _uploadPieces, start, end);
                _meta.CopyStaged(slot, _uploadPieces, start, end);
                _atlasRect.CopyStaged(slot, _uploadPieces, start, end);
                _tileSize.CopyStaged(slot, _uploadPieces, start, end);
                _animation.CopyStaged(slot, _uploadPieces, start, end);
                _world.CopyStaged(slot, _uploadPieces, start, end);
                _glow.CopyStaged(slot, _uploadPieces, start, end);
                _geometryX.CopyStaged(slot, _uploadPieces, start, end);
                _geometryY.CopyStaged(slot, _uploadPieces, start, end);
                start = end;
            }

            LastStageMs = ElapsedMs(stageStart);
            LastStageCopyMs = TicksToMs(
                TerrainCellDataChannel<Color32>.CopyTicks + TerrainCellDataChannel<TerrainHalfTexel>.CopyTicks +
                TerrainCellDataChannel<Vector4>.CopyTicks);
            LastStageApplyMs = TicksToMs(
                TerrainCellDataChannel<Color32>.ApplyTicks + TerrainCellDataChannel<TerrainHalfTexel>.ApplyTicks +
                TerrainCellDataChannel<Vector4>.ApplyTicks);
        }

        _dirty.Clear();
    }

    // Слот, чья промежуточная текстура не набивалась ни в этом кадре, ни в
    // прошлом: её прошлую загрузку render thread уже прочитал, и доступ к
    // пиксельному буферу не ждёт. Пул растёт только под настоящий объём
    // выгрузки; за потолком берётся самый давний слот — медленнее, но верно.
    private int AcquireStagingSlot()
    {
        int frame = Time.frameCount;
        int oldest = 0;
        for (int slot = 0; slot < _stagingSlotFrames.Count; slot++)
        {
            if (_stagingSlotFrames[slot] < frame - 1)
            {
                _stagingSlotFrames[slot] = frame;
                return slot;
            }

            if (_stagingSlotFrames[slot] < _stagingSlotFrames[oldest])
            {
                oldest = slot;
            }
        }

        int chosen = _stagingSlotFrames.Count < MaximumStagingSlots ? _stagingSlotFrames.Count : oldest;
        if (chosen == _stagingSlotFrames.Count)
        {
            _stagingSlotFrames.Add(frame);
            FrameEventLog.Record($"террейн: staging-слот {chosen} создан");
            _color.EnsureStagingSlot(chosen);
            _meta.EnsureStagingSlot(chosen);
            _atlasRect.EnsureStagingSlot(chosen);
            _tileSize.EnsureStagingSlot(chosen);
            _animation.EnsureStagingSlot(chosen);
            _world.EnsureStagingSlot(chosen);
            _glow.EnsureStagingSlot(chosen);
            _geometryX.EnsureStagingSlot(chosen);
            _geometryY.EnsureStagingSlot(chosen);
        }
        else
        {
            _stagingSlotFrames[chosen] = frame;
        }

        return chosen;
    }

    // Глобально, а не в материал: свойства вне UnityPerMaterial выключили бы
    // SRP Batcher на всём шейдере террейна.
    public void BindGlobals(float cellSize, int originX, int originY)
    {
        if (!IsAllocated)
        {
            return;
        }

        Shader.SetGlobalTexture(ColorID, _color.Target);
        Shader.SetGlobalTexture(MetaID, _meta.Target);
        Shader.SetGlobalTexture(AtlasRectID, _atlasRect.Target);
        Shader.SetGlobalTexture(TileSizeID, _tileSize.Target);
        Shader.SetGlobalTexture(AnimationID, _animation.Target);
        Shader.SetGlobalTexture(WorldID, _world.Target);
        Shader.SetGlobalTexture(GlowID, _glow.Target);
        Shader.SetGlobalTexture(GeometryXID, _geometryX.Target);
        Shader.SetGlobalTexture(GeometryYID, _geometryY.Target);
        Shader.SetGlobalVector(GridSizeID, new Vector4(MeshWidth, MeshHeight, cellSize, 0f));
        Shader.SetGlobalVector(OriginID, new Vector4(originX, originY, 0f, 0f));
    }

    public void Dispose()
    {
        _color.Destroy();
        _meta.Destroy();
        _atlasRect.Destroy();
        _tileSize.Destroy();
        _animation.Destroy();
        _world.Destroy();
        _glow.Destroy();
        _geometryX.Destroy();
        _geometryY.Destroy();
        _stagingSlotFrames.Clear();
        MeshWidth = 0;
        MeshHeight = 0;
    }

}
