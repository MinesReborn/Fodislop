#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using MinesServer.Data;

using Kern.Diagnostics.Frames;

using Kern.Diagnostics.World;

namespace Kern.World.Terrain;

// Данные клетки террейна на GPU: StructuredBuffer клеток и таблица типов.
// Что лежит в битах — TerrainCellData.
//
// Адрес кольцевой: клетка лежит по своей мировой координате по модулю
// размера кольца. Кольцо на клетку шире окна с каждой стороны
// (TerrainCellData.RingMargin): краевым клеткам нужны соседи и их узлы.
// Записанная клетка остаётся на месте, пока видна.
//
// Сборка идёт в управляемые массивы (их можно заполнять из Parallel.For).
// На GPU уходят только изменённые строки прямоугольников: строка кольца в
// буфере непрерывна, и каждая уходит одним SetData. Когда отрезков больше
// MaximumUploadSegments, дешевле одна выгрузка всего буфера.
public sealed class TerrainCellBuffers : IDisposable
{
    private const int MaximumUploadSegments = 64;

    public static readonly int CellsId = Shader.PropertyToID("_TerrainCells");
    public static readonly int TypesId = Shader.PropertyToID("_TerrainTypes");
    public static readonly int TileDescriptorsId = Shader.PropertyToID("_TerrainTileDescriptors");
    public static readonly int GridSizeId = Shader.PropertyToID("_TerrainCellGridSize");
    public static readonly int OriginId = Shader.PropertyToID("_TerrainCellOrigin");
    public static readonly int ViewOffsetId = Shader.PropertyToID("_TerrainCellViewOffset");
    public static readonly int DecalAtlasInfoId = Shader.PropertyToID("_TerrainDecalAtlasInfo");
    public static readonly int DecalEffectsId = Shader.PropertyToID("_TerrainDecalEffects");
    public static readonly int UnderlayTypeId = Shader.PropertyToID("_TerrainUnderlayType");
    public static readonly int DistortionEnabledId = Shader.PropertyToID("_TerrainDistortionEnabled");
    public static readonly int DistortionId = Shader.PropertyToID("_TerrainDistortion");

    private static readonly Vector4[] s_distortion = TerrainCellData.PackDistortion();
    private const int CellsPerWord = (int)TerrainCellFormat.CellsPerWord;
    private const int CellBits = (int)TerrainCellFormat.CellBits;
    private static readonly int s_typeStride = System.Runtime.InteropServices.Marshal.SizeOf<TerrainTypeRow>();

    private readonly TerrainTextureUploadCounters _uploadCounters = new();
    private readonly TerrainDirtyRegion _dirty = new();
    private readonly List<(int Start, int Count)> _segments = new();
    // До прихода метаданных строка несёт всё из cells.json, но без текстуры
    // и тайлгруппы; строка Unloaded так и остаётся.
    private readonly TerrainTypeRow[] _types = PackAuthoredTypes();
    // Клетки по байту; на GPU — по четыре в uint. Слова собираются из клеток
    // только при выгрузке: так заливка из Parallel.For не пишет в общее слово.
    private TerrainCell[] _cells = Array.Empty<TerrainCell>();
    private uint[] _words = Array.Empty<uint>();
    private GraphicsBuffer? _cellBuffer;
    private GraphicsBuffer? _typeBuffer;
    private GraphicsBuffer? _tileDescriptorBuffer;
    private bool _typesDirty;

    public int MeshWidth { get; private set; }

    public int MeshHeight { get; private set; }

    public int RingWidth { get; private set; }

    public int RingHeight { get; private set; }

    public bool IsAllocated => _cellBuffer != null;

    public ITerrainTextureUploadTelemetry UploadTelemetry => _uploadCounters;

    /// <summary>Чем была последняя выгрузка: сколько прямоугольников и клеток.</summary>
    ///
    /// Ноль прямоугольников при ненулевом числе клеток означает выгрузку целиком.
    public int LastUploadRectCount { get; private set; }

    public long LastUploadTexels { get; private set; }

    /// <summary>Сколько вызовов SetData ушло на последнюю выгрузку клеток.</summary>
    public int LastUploadStrips { get; private set; }

    /// <summary>Сколько заняла последняя выгрузка клеток и таблицы типов.</summary>
    public float LastStageMs { get; private set; }

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
        RingWidth = meshWidth + (2 * TerrainCellData.RingMargin);
        RingHeight = meshHeight + (2 * TerrainCellData.RingMargin);
        int count = checked(RingWidth * RingHeight);
        _cells = new TerrainCell[count];
        _words = new uint[(count + CellsPerWord - 1) / CellsPerWord];
        _cellBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _words.Length, sizeof(uint))
        {
            name = "TerrainCells",
        };
        _typeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _types.Length, s_typeStride)
        {
            name = "TerrainTypes",
        };
        _tileDescriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TerrainCellData.TileDescriptorWords, sizeof(uint))
        {
            name = "TerrainTileDescriptors",
        };
        _tileDescriptorBuffer.SetData(TerrainCellData.PackTileDescriptors());
        _typesDirty = true;
        _uploadCounters.BeginGeneration();
        _dirty.Reset(RingWidth, RingHeight);
    }

    // Полная сборка пишет клетки из нескольких потоков: прямоугольник по
    // ним не копится, выгружается всё.
    public void MarkAllDirty() => _dirty.MarkAll();

    // Прямоугольник клеток в мировых координатах; на шве кольца он
    // разрезается, чтобы не растягиваться на весь буфер.
    public void MarkCells(int gridX, int unityY, int width, int height) =>
        _dirty.MarkCells(Ring(gridX, RingWidth), Ring(unityY, RingHeight), width, height);

    public void SetCell(int gridX, int unityY, TerrainCell cell) =>
        _cells[Index(gridX, unityY)] = cell;

    // Ровно то, что уходит на GPU в Apply(): между этим чтением и выгрузкой
    // нет ни одного преобразования. Нужно тестам сборки как сравнимый результат.
    internal TerrainCell GetCell(int gridX, int unityY) => _cells[Index(gridX, unityY)];

    private int Index(int gridX, int unityY) =>
        (Ring(unityY, RingHeight) * RingWidth) + Ring(gridX, RingWidth);

    internal TerrainTypeRow GetTypeRow(CellType type) => _types[(byte)type];

    private static TerrainTypeRow[] PackAuthoredTypes()
    {
        var rows = new TerrainTypeRow[TerrainCellData.TypeCount];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = TerrainCellData.PackType(
                TerrainCellPacker.ResolveTypeFields((CellType)index, default, Array.Empty<IAtlasDescriptor>()));
        }

        return rows;
    }

    // Строка меняется, только когда у типа поменялся вид (приехала текстура,
    // пришёл конфиг); таблица тогда уходит целиком — 4 КБ.
    public void SetType(CellType type, TerrainTypeRow row)
    {
        ref TerrainTypeRow slot = ref _types[(byte)type];
        if (slot != row)
        {
            slot = row;
            _typesDirty = true;
        }
    }

    public void Apply()
    {
        if (!IsAllocated)
        {
            return;
        }

        long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_typesDirty)
        {
            _typeBuffer!.SetData(_types);
            _uploadCounters.RecordApply(_types.Length, 1, s_typeStride, Time.frameCount);
            _typesDirty = false;
        }

        if (_dirty.IsEmpty)
        {
            LastStageMs = ElapsedMs(stageStart);
            return;
        }

        _segments.Clear();
        bool full = _dirty.IsAll;
        for (int i = 0; i < _dirty.Count && !full; i++)
        {
            RectInt rect = _dirty[i];
            for (int row = rect.yMin; row < rect.yMax; row++)
            {
                AddSegment((row * RingWidth) + rect.xMin, rect.width);
            }

            full = _segments.Count > MaximumUploadSegments;
        }

        if (full)
        {
            LastUploadRectCount = 0;
            LastUploadTexels = _cells.Length;
            LastUploadStrips = 1;
            FrameEventLog.Record($"террейн: полная выгрузка {LastUploadTexels} клеток");
            PackWords(0, _words.Length);
            _cellBuffer!.SetData(_words);
            _uploadCounters.RecordApply(_words.Length, 1, sizeof(uint), Time.frameCount);
        }
        else
        {
            LastUploadRectCount = _dirty.Count;
            LastUploadTexels = _dirty.Area;
            LastUploadStrips = _segments.Count;
            for (int i = 0; i < _segments.Count; i++)
            {
                (int start, int count) = _segments[i];
                // Отрезок клеток → слова, задевающие его края.
                int firstWord = start / CellsPerWord;
                int wordCount = ((start + count + CellsPerWord - 1) / CellsPerWord) - firstWord;
                PackWords(firstWord, wordCount);
                _cellBuffer!.SetData(_words, firstWord, firstWord, wordCount);
                _uploadCounters.RecordApply(wordCount, 1, sizeof(uint), Time.frameCount);
            }
        }

        LastStageMs = ElapsedMs(stageStart);
        _dirty.Clear();
    }

    // Слово i — клетки 4i..4i + 3, младшая — в младшем байте.
    private void PackWords(int firstWord, int wordCount)
    {
        for (int word = firstWord; word < firstWord + wordCount; word++)
        {
            int first = word * CellsPerWord;
            uint packed = 0u;
            for (int i = 0; i < CellsPerWord && first + i < _cells.Length; i++)
            {
                packed |= (uint)_cells[first + i].Bits << (i * CellBits);
            }

            _words[word] = packed;
        }
    }

    // Соседние строки прямоугольника во всю ширину лежат подряд и сливаются
    // в один отрезок.
    private void AddSegment(int start, int count)
    {
        if (_segments.Count > 0)
        {
            (int lastStart, int lastCount) = _segments[^1];
            if (lastStart + lastCount == start)
            {
                _segments[^1] = (lastStart, lastCount + count);
                return;
            }
        }

        _segments.Add((start, count));
    }

    // Глобально, а не в материал: свойства вне UnityPerMaterial выключили бы
    // SRP Batcher на всём шейдере террейна. Размер мира нужен шейдеру, чтобы
    // получить серверную строку клетки из её адреса и не смещать узлы на
    // краю мира; флаг и константы искажения — чтобы считать узлы и рёбра
    // как TerrainVertexDistortionCalculator; правила декалей — чтобы ставить
    // их тем же хэшем, что TerrainDecalCatalog.Place.
    public void BindGlobals(float cellSize, int originX, int originY, int worldWidth, int worldHeight, bool distortionEnabled)
    {
        if (!IsAllocated)
        {
            return;
        }

        Shader.SetGlobalBuffer(CellsId, _cellBuffer);
        Shader.SetGlobalBuffer(TypesId, _typeBuffer);
        Shader.SetGlobalBuffer(TileDescriptorsId, _tileDescriptorBuffer);
        Shader.SetGlobalVector(GridSizeId, new Vector4(RingWidth, RingHeight, cellSize, 0f));
        Shader.SetGlobalVector(OriginId, new Vector4(originX, originY, worldHeight, worldWidth));
        Shader.SetGlobalInteger(DistortionEnabledId, distortionEnabled ? 1 : 0);
        Shader.SetGlobalInteger(UnderlayTypeId, (int)BlockRegistry.UnderlayType);
        Shader.SetGlobalVectorArray(DistortionId, s_distortion);
        Shader.SetGlobalVectorArray(DecalAtlasInfoId, TerrainDecalCatalog.GetAtlasInfo());
        Shader.SetGlobalVectorArray(DecalEffectsId, TerrainDecalCatalog.GetEffects());
    }

    public void Dispose()
    {
        _cellBuffer?.Dispose();
        _cellBuffer = null;
        _typeBuffer?.Dispose();
        _typeBuffer = null;
        _tileDescriptorBuffer?.Dispose();
        _tileDescriptorBuffer = null;
        _cells = Array.Empty<TerrainCell>();
        _words = Array.Empty<uint>();
        _uploadCounters.EndGeneration();
        MeshWidth = 0;
        MeshHeight = 0;
        RingWidth = 0;
        RingHeight = 0;
    }

    private static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);
}
