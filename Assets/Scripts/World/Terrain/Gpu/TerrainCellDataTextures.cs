#nullable enable

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

// Текстуры данных клетки террейна: по текселю на квад, по две строки на
// клетку (фон, передний план), плюс сетка смещений искажения на узлах.
//
// Адрес кольцевой: тексель клетки — её мировая координата по модулю размера
// сетки, узел — по модулю размера сетки узлов. Окно камеры покрывает ровно
// один полный круг, поэтому записанная клетка остаётся на месте, пока видна.
//
// Сборка идёт в управляемые массивы (их можно заполнять из Parallel.For).
// На GPU уходит только изменённый прямоугольник: он копируется в маленькую
// текстуру-заплатку и переносится Graphics.CopyTexture. Заплатка при копании
// почти каждый кадр, и выгрузка всей текстуры на каждой была дороже, чем
// прежняя частичная заливка вершин.
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
    public static readonly int GridOffsetsID = Shader.PropertyToID("_TerrainGridOffsets");
    public static readonly int GridSizeID = Shader.PropertyToID("_TerrainCellGridSize");
    public static readonly int OriginID = Shader.PropertyToID("_TerrainCellOrigin");
    public static readonly int ViewOffsetID = Shader.PropertyToID("_TerrainCellViewOffset");

    private sealed class Channel<T>(TextureFormat format, string name)
        where T : struct
    {
        public Texture2D? Target;
        public Texture2D? Patch;
        public T[] Data = [];

        public void Allocate(int width, int height)
        {
            Target = Create(width, height, format, name);
            Data = new T[width * height];
        }

        public void UploadAll()
        {
            NativeArray<T> pixels = Target!.GetPixelData<T>(0);
            pixels.CopyFrom(Data);
            Target.Apply(false, false);
        }

        public void UploadRect(int x, int y, int width, int height)
        {
            int textureWidth = Target!.width;
            if (Patch == null || Patch.width < width || Patch.height < height)
            {
                DestroyTexture(ref Patch);
                Patch = Create(
                    Mathf.NextPowerOfTwo(Math.Max(width, 16)),
                    Mathf.NextPowerOfTwo(Math.Max(height, 16)),
                    format,
                    name + "Patch");
            }

            NativeArray<T> pixels = Patch.GetPixelData<T>(0);
            for (int row = 0; row < height; row++)
            {
                NativeArray<T>.Copy(Data, ((y + row) * textureWidth) + x, pixels, row * Patch.width, width);
            }

            Patch.Apply(false, false);
            Graphics.CopyTexture(Patch, 0, 0, 0, 0, width, height, Target, 0, 0, x, y);
        }

        public void Destroy()
        {
            DestroyTexture(ref Target);
            DestroyTexture(ref Patch);
            Data = [];
        }
    }

    private readonly Channel<Color32> _color = new(TextureFormat.RGBA32, "TerrainCellColor");
    private readonly Channel<Color32> _meta = new(TextureFormat.RGBA32, "TerrainCellMeta");
    private readonly Channel<TerrainHalfTexel> _atlasRect = new(TextureFormat.RGBAHalf, "TerrainCellAtlasRect");
    private readonly Channel<TerrainHalfTexel> _tileSize = new(TextureFormat.RGBAHalf, "TerrainCellTileSize");
    private readonly Channel<TerrainHalfTexel> _animation = new(TextureFormat.RGBAHalf, "TerrainCellAnimation");
    private readonly Channel<Vector4> _world = new(TextureFormat.RGBAFloat, "TerrainCellWorld");
    private readonly Channel<Vector4> _glow = new(TextureFormat.RGBAFloat, "TerrainCellGlow");
    private readonly Channel<Vector4> _gridOffsets = new(TextureFormat.RGBAFloat, "TerrainGridOffsets");

    private readonly TerrainDirtyRegion _dirty = new();
    private readonly TerrainDirtyRegion _gridOffsetsDirty = new();
    private bool _offsetsDirty;

    public int MeshWidth { get; private set; }

    public int MeshHeight { get; private set; }

    public bool IsAllocated => _color.Target != null;

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
        _gridOffsets.Allocate(meshWidth + 1, meshHeight + 1);
        _offsetsDirty = true;
        _dirty.Reset(meshWidth, meshHeight);
        _gridOffsetsDirty.Reset(meshWidth + 1, meshHeight + 1);
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
    }

    // offsets — локальные узлы окна [0, W] × [0, H], minX/minY — мировой узел (0, 0).
    public void WriteGridOffsets(TerrainRingGrid<Vector3> offsets, int minX, int minY)
    {
        if (!IsAllocated)
        {
            return;
        }

        int nodesWide = MeshWidth + 1;
        int nodesHigh = MeshHeight + 1;
        int width = Math.Min(offsets.Width, nodesWide);
        int height = Math.Min(offsets.Height, nodesHigh);
        for (int x = 0; x < width; x++)
        {
            int ringX = Ring(minX + x, nodesWide);
            for (int y = 0; y < height; y++)
            {
                Vector3 offset = offsets[x, y];
                _gridOffsets.Data[(Ring(minY + y, nodesHigh) * nodesWide) + ringX] =
                    new Vector4(offset.x, offset.y, offset.z, 0f);
            }
        }

        _offsetsDirty = true;
        _gridOffsetsDirty.MarkAll();
    }

    public void WriteGridOffsetsIncremental(TerrainRingGrid<Vector3> offsets, int minX, int minY, int dx, int dy)
    {
        if (!IsAllocated)
        {
            return;
        }

        int nodesWide = MeshWidth + 1;
        int nodesHigh = MeshHeight + 1;
        int xStart = 0;
        int xLength = 0;
        int yStart = 0;
        int yLength = 0;

        if (dx > 0)
        {
            xStart = Mathf.Max(0, nodesWide - dx - 1);
            xLength = nodesWide - xStart;
        }
        else if (dx < 0)
        {
            xLength = Mathf.Min(nodesWide, -dx + 1);
        }

        if (dy > 0)
        {
            yStart = Mathf.Max(0, nodesHigh - dy - 1);
            yLength = nodesHigh - yStart;
        }
        else if (dy < 0)
        {
            yLength = Mathf.Min(nodesHigh, -dy + 1);
        }

        if (xLength > 0)
        {
            WriteGridOffsetRect(offsets, minX, minY, xStart, 0, xLength, nodesHigh);
            _gridOffsetsDirty.MarkRect(
                Ring(minX + xStart, nodesWide),
                Ring(minY, nodesHigh),
                xLength,
                nodesHigh);
        }

        if (yLength > 0 && xLength < nodesWide)
        {
            int remainingStart = dx > 0 ? 0 : xLength;
            int remainingWidth = nodesWide - xLength;
            WriteGridOffsetRect(offsets, minX, minY, remainingStart, yStart, remainingWidth, yLength);
            _gridOffsetsDirty.MarkRect(
                Ring(minX + remainingStart, nodesWide),
                Ring(minY + yStart, nodesHigh),
                remainingWidth,
                yLength);
        }

        _offsetsDirty = true;
    }

    public void Apply()
    {
        if (!IsAllocated)
        {
            return;
        }

        if (_offsetsDirty)
        {
            _offsetsDirty = false;
            long gridOffsetArea = (long)(MeshWidth + 1) * (MeshHeight + 1);
            long gridOffsetPatchWork = _gridOffsetsDirty.Area +
                (_gridOffsetsDirty.Count * PatchSetupEquivalentTexels);
            bool fullGridOffsetUpload = _gridOffsetsDirty.IsAll ||
                SystemInfo.copyTextureSupport == CopyTextureSupport.None ||
                gridOffsetPatchWork >= gridOffsetArea;
            if (fullGridOffsetUpload)
            {
                _gridOffsets.UploadAll();
            }
            else
            {
                for (int i = 0; i < _gridOffsetsDirty.Count; i++)
                {
                    RectInt rect = _gridOffsetsDirty[i];
                    _gridOffsets.UploadRect(rect.x, rect.y, rect.width, rect.height);
                }
            }

            _gridOffsetsDirty.Clear();
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
            _color.UploadAll();
            _meta.UploadAll();
            _atlasRect.UploadAll();
            _tileSize.UploadAll();
            _animation.UploadAll();
            _world.UploadAll();
            _glow.UploadAll();
        }
        else
        {
            for (int i = 0; i < _dirty.Count; i++)
            {
                RectInt rect = _dirty[i];
                _color.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _meta.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _atlasRect.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _tileSize.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _animation.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _world.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _glow.UploadRect(rect.x, rect.y, rect.width, rect.height);
            }
        }

        _dirty.Clear();
    }

    // Глобально, а не в материал: свойства вне UnityPerMaterial выключили бы
    // SRP Batcher на всём шейдере террейна.
    public void BindGlobals(float cellSize, int originX, int originY, bool distortion)
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
        Shader.SetGlobalTexture(GridOffsetsID, _gridOffsets.Target);
        Shader.SetGlobalVector(GridSizeID, new Vector4(MeshWidth, MeshHeight, cellSize, distortion ? 1f : 0f));
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
        _gridOffsets.Destroy();
        _gridOffsetsDirty.Clear();
        MeshWidth = 0;
        MeshHeight = 0;
    }

    private void WriteGridOffsetRect(
        TerrainRingGrid<Vector3> offsets,
        int minX,
        int minY,
        int startX,
        int startY,
        int width,
        int height)
    {
        int nodesWide = MeshWidth + 1;
        int nodesHigh = MeshHeight + 1;
        for (int x = startX; x < startX + width; x++)
        {
            int ringX = Ring(minX + x, nodesWide);
            for (int y = startY; y < startY + height; y++)
            {
                Vector3 offset = offsets[x, y];
                _gridOffsets.Data[(Ring(minY + y, nodesHigh) * nodesWide) + ringX] =
                    new Vector4(offset.x, offset.y, offset.z, 0f);
            }
        }
    }

    private static Texture2D Create(int width, int height, TextureFormat format, string name) =>
        new(width, height, format, mipChain: false, linear: true)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };

    private static void DestroyTexture(ref Texture2D? texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        texture = null;
    }
}
