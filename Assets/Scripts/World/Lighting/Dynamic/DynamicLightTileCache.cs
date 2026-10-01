#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kern.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

// Dynamic light, kept per dynamic light until the dynamic light or what it lights changes.
//
// Source ray depth depends on pose, colour, the material field and its world origin.
// Receiver radiance also depends on the visible world-grid coverage. Camera motion
// updates those receivers while retaining unchanged source rays. Lights that moved, changed,
// appeared, or lost their tiles to a geometry change are traced in the same
// frame. There is no update rate: every change is traced when it happens.
internal sealed class DynamicLightTileCache
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TileInfo
    {
        public readonly int FieldOriginX;
        public readonly int FieldOriginY;
        public readonly int SizeX;
        public readonly int SizeY;
        public readonly int TileOffsetX;
        public readonly int TileOffsetY;
        public readonly int ReachIndex;
        public readonly int Reserved;

        public TileInfo(RectInt fieldRect, Vector2Int tileOffset, int reachIndex)
        {
            FieldOriginX = fieldRect.x;
            FieldOriginY = fieldRect.y;
            SizeX = fieldRect.width;
            SizeY = fieldRect.height;
            TileOffsetX = tileOffset.x;
            TileOffsetY = tileOffset.y;
            ReachIndex = reachIndex;
            Reserved = 0;
        }
    }

    public const int TileInfoStride = sizeof(int) * 8;

    // Tile sizes round up to this many texels so a slightly brighter dynamic light
    // does not reallocate the atlas.
    private const int TileQuantum = 64;

    private readonly Dictionary<int, int> _slotByLightID = new();
    private int[] _lightIDBySlot = Array.Empty<int>();
    private bool[] _slotInUse = Array.Empty<bool>();
    private int[] _slotSeenFrame = Array.Empty<int>();
    private bool[] _slotValid = Array.Empty<bool>();
    private bool[] _slotPolarValid = Array.Empty<bool>();
    private Vector2Int[] _slotPolarSize = Array.Empty<Vector2Int>();
    private Vector4[] _slotPosition = Array.Empty<Vector4>();
    private Vector4[] _slotColor = Array.Empty<Vector4>();
    private RectInt[] _slotRect = Array.Empty<RectInt>();
    private int _frame;
    private int _tileWidth;
    private int _tileHeight;
    private bool _singleLightDirect;

    public RenderTexture? Tiles { get; private set; }

    // Increments whenever the tile atlas is replaced: every retained tile and
    // slot is gone, so a composed result cannot be patched incrementally.
    public int LayoutGeneration { get; private set; }

    public ComputeBuffer? TileInfos { get; private set; }

    // Cached optical depth per source: column = ray angle, row = distance in
    // texels, layer = source slot * emitter point count + emitter point.
    // Float preserves depth through rock; layers retain the complete ray length.
    public RenderTexture? Polar { get; private set; }

    // Angular horizon: one radius per source slot, emitter point and polar
    // direction ([slot][point][direction], stride = directions), written by the
    // rays and replaced together with them.
    public ComputeBuffer? Horizon { get; private set; }

    public int HorizonStride { get; private set; }

    public void EnsurePolar(int angles, int radii)
    {
        RenderTextureFormat format = LightingComputeBinder.UsesScalarPolarExtinction
            ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBFloat;
        int layers = checked(Math.Max(1, Capacity) * LightingComputeBinder.DynamicEmitterPointCount);
        if (Polar != null && Polar.format == format &&
            Polar.width >= angles + 2 && Polar.height >= radii && Polar.volumeDepth >= layers &&
            Horizon != null && Horizon.count >= layers * HorizonStride)
        {
            return;
        }

        int width = Math.Max(angles + 2, Polar?.width ?? 0);
        int height = Math.Max(RoundUpToQuantum(radii), Polar?.height ?? 0);
        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            throw new InvalidOperationException(
                $"Dynamic ray texture {width}x{height} exceeds the maximum texture size {SystemInfo.maxTextureSize}.");
        }

        if (!SystemInfo.supports2DArrayTextures)
        {
            throw new InvalidOperationException("Dynamic transport requires 2D array texture support.");
        }

        if (layers > SystemInfo.maxTextureArraySlices)
        {
            throw new InvalidOperationException(
                $"Dynamic transport requires {layers} layers; maximum is {SystemInfo.maxTextureArraySlices}.");
        }
        if (!SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format))
        {
            throw new InvalidOperationException($"Dynamic optical depth requires writable {format} textures.");
        }
        MemoryAllocationGuard.Require("Dynamic optical depth array",
            LightingAllocationEstimate.TextureBytes(width, height, layers,
                format == RenderTextureFormat.RFloat ? 4 : 16) +
            (long)layers * (width - 2) * sizeof(uint));
        ReleasePolar();
        var polar = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = layers,
            enableRandomWrite = true,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "_DynamicRayDepth",
        };
        if (!polar.Create())
        {
            DestroyObject(polar);
            throw new InvalidOperationException("Failed to create the dynamic light ray texture.");
        }

        Polar = polar;
        HorizonStride = width - 2;
        Horizon = new ComputeBuffer(layers * HorizonStride, sizeof(uint), ComputeBufferType.Structured);
        Array.Clear(_slotPolarValid, 0, _slotPolarValid.Length);
    }

    private void ReleasePolar()
    {
        if (Polar != null)
        {
            Polar.Release();
            DestroyObject(Polar);
            Polar = null;
        }

        Horizon?.Release();
        Horizon = null;
        HorizonStride = 0;
    }

    public int Capacity { get; private set; }

    // Grows the atlas when a dynamic rectangle or the dynamic light count no longer fits.
    // Shrinks it back when the count drops to a quarter of capacity, so one
    // huge or busy frame does not pin VRAM forever. Growth is eager, shrink
    // is lazy (quarter threshold): hovering around the boundary must not
    // flap between reallocating and re-tracing every frame.
    // A new atlas holds no traced light, so every tile is invalidated.
    public void EnsureLayout(int requiredTileWidth, int requiredTileHeight, int lightCount, int polarAngles, int polarRadii)
    {
        // A single light retains its result in DirectTexture itself. Keep a
        // one-texel UAV binding for the shared kernel, without a duplicate
        // full-field radiance allocation or any writes to that binding.
        bool singleLightDirect = lightCount == 1;
        RenderTextureFormat tileFormat = !singleLightDirect && LightingComputeBinder.UsesScalarDynamicRadiance
            ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBHalf;
        bool shrinkToFit = Capacity > 1 && lightCount <= Capacity / 4;
        bool replaceLayout = shrinkToFit || singleLightDirect != _singleLightDirect;
        int tileWidth = singleLightDirect ? 1 : replaceLayout
            ? RoundUpToQuantum(requiredTileWidth)
            : Math.Max(_tileWidth, RoundUpToQuantum(requiredTileWidth));
        int tileHeight = singleLightDirect ? 1 : replaceLayout
            ? RoundUpToQuantum(requiredTileHeight)
            : Math.Max(_tileHeight, RoundUpToQuantum(requiredTileHeight));
        int capacity = singleLightDirect ? 1 : replaceLayout
            ? Mathf.NextPowerOfTwo(Math.Max(1, lightCount))
            : Math.Max(Capacity, Mathf.NextPowerOfTwo(Math.Max(1, lightCount)));
        if (Tiles != null &&
            Tiles.format == tileFormat &&
            tileWidth == _tileWidth &&
            tileHeight == _tileHeight &&
            capacity == Capacity)
        {
            return;
        }

        // A source owns one array layer. Packing sources beside one another
        // multiplied viewport dimensions and could exceed the device limit on
        // zoom even when every individual world-grid target was supported.
        if (tileWidth > SystemInfo.maxTextureSize || tileHeight > SystemInfo.maxTextureSize)
        {
            throw new InvalidOperationException(
                $"Dynamic light layer {tileWidth}x{tileHeight} for {capacity} dynamic lights exceeds the " +
                $"maximum texture size {SystemInfo.maxTextureSize}.");
        }
        if (!SystemInfo.supports2DArrayTextures || capacity > SystemInfo.maxTextureArraySlices)
        {
            throw new InvalidOperationException(
                $"Dynamic radiance requires {capacity} array layers; maximum is {SystemInfo.maxTextureArraySlices}.");
        }
        int polarLayers = checked(capacity * LightingComputeBinder.DynamicEmitterPointCount);
        if (polarLayers > SystemInfo.maxTextureArraySlices)
        {
            throw new InvalidOperationException(
                $"Dynamic transport requires {polarLayers} layers at this source capacity; " +
                $"maximum is {SystemInfo.maxTextureArraySlices}.");
        }
        if (!SystemInfo.SupportsRandomWriteOnRenderTextureFormat(tileFormat))
        {
            throw new InvalidOperationException($"Dynamic radiance requires writable {tileFormat} textures.");
        }
        long tileBytes = LightingAllocationEstimate.TextureBytes(tileWidth, tileHeight, capacity,
            tileFormat == RenderTextureFormat.RFloat ? 4 : 8) + (long)capacity * TileInfoStride;
        RenderTextureFormat polarFormat = LightingComputeBinder.UsesScalarPolarExtinction
            ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBFloat;
        bool replacesPolar = capacity < Capacity || Polar == null || Polar.format != polarFormat ||
            Polar.width < polarAngles + 2 || Polar.height < polarRadii || Polar.volumeDepth < polarLayers;
        long polarBytes = replacesPolar
            ? LightingAllocationEstimate.TextureBytes(Math.Max(polarAngles + 2, Polar?.width ?? 0),
                Math.Max(RoundUpToQuantum(polarRadii), Polar?.height ?? 0), polarLayers,
                polarFormat == RenderTextureFormat.RFloat ? 4 : 16)
            : 0;
        // Validate tiles and the immediately following ray allocation together.
        // Old GPU generations may still be resident; do not subtract them.
        MemoryAllocationGuard.Require("Dynamic light cache generation", checked(tileBytes + polarBytes));
        ReleaseGpuResources();
        if (capacity < Capacity)
        {
            // A quarter-capacity shrink already invalidates/remaps every
            // source slot. Release its old polar layers in the same transition
            // instead of retaining a many-source array for one parked light.
            ReleasePolar();
        }
        var tiles = new RenderTexture(tileWidth, tileHeight, 0, tileFormat, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = capacity,
            enableRandomWrite = true,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "_DynamicLightTiles",
        };
        if (!tiles.Create())
        {
            DestroyObject(tiles);
            throw new InvalidOperationException("Failed to create the dynamic light atlas.");
        }

        Tiles = tiles;
        TileInfos = new ComputeBuffer(capacity, TileInfoStride, ComputeBufferType.Structured);

        // A layout change replaces the atlas and invalidates every traced tile.
        // The old ID -> slot map is invalid as well: a shrink can remove slots
        // that were still referenced by the dictionary, and the next solve
        // would index the newly smaller arrays with those stale values.
        _slotByLightID.Clear();
        Array.Resize(ref _lightIDBySlot, capacity);
        Array.Resize(ref _slotInUse, capacity);
        Array.Resize(ref _slotSeenFrame, capacity);
        Array.Resize(ref _slotValid, capacity);
        Array.Resize(ref _slotPolarValid, capacity);
        Array.Resize(ref _slotPolarSize, capacity);
        Array.Resize(ref _slotPosition, capacity);
        Array.Resize(ref _slotColor, capacity);
        Array.Resize(ref _slotRect, capacity);
        Array.Clear(_lightIDBySlot, 0, _lightIDBySlot.Length);
        Array.Clear(_slotInUse, 0, _slotInUse.Length);
        Array.Clear(_slotSeenFrame, 0, _slotSeenFrame.Length);
        Array.Clear(_slotValid, 0, _slotValid.Length);
        Array.Clear(_slotPolarValid, 0, _slotPolarValid.Length);
        Capacity = capacity;
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;
        _singleLightDirect = singleLightDirect;
        LayoutGeneration++;
        InvalidateAll();
    }

    // Every tile must be traced again: the material field, the field layout,
    // or a debug view changed what a dynamic light's light is.
    public void InvalidateAll()
    {
        Array.Clear(_slotValid, 0, _slotValid.Length);
        // Optical depth depends on material and the field's world origin as
        // well as source pose. A parked source still needs new rays after a
        // geometry edit or reanchor; retaining them would sample the old world.
        Array.Clear(_slotPolarValid, 0, _slotPolarValid.Length);
    }

    // Keeps the slots of dynamic lights still present, frees those of dynamic lights gone, and
    // gives new dynamic lights free slots. Call once per solve, before SlotOf.
    public void AssignSlots(ReadOnlySpan<int> lightIDs)
    {
        _frame++;
        for (int i = 0; i < lightIDs.Length; i++)
        {
            if (_slotByLightID.TryGetValue(lightIDs[i], out int slot))
            {
                _slotSeenFrame[slot] = _frame;
            }
        }

        for (int slot = 0; slot < Capacity; slot++)
        {
            if (_slotInUse[slot] && _slotSeenFrame[slot] != _frame)
            {
                _slotByLightID.Remove(_lightIDBySlot[slot]);
                _slotInUse[slot] = false;
                _slotValid[slot] = false;
                _slotPolarValid[slot] = false;
            }
        }

        int freeSearch = 0;
        for (int i = 0; i < lightIDs.Length; i++)
        {
            if (_slotByLightID.ContainsKey(lightIDs[i]))
            {
                continue;
            }

            while (_slotInUse[freeSearch])
            {
                freeSearch++;
            }

            _slotInUse[freeSearch] = true;
            _slotValid[freeSearch] = false;
            _slotPolarValid[freeSearch] = false;
            _slotSeenFrame[freeSearch] = _frame;
            _lightIDBySlot[freeSearch] = lightIDs[i];
            _slotByLightID.Add(lightIDs[i], freeSearch);
        }
    }

    public int SlotOf(int lightID) => _slotByLightID[lightID];

    public bool NeedsTrace(int slot, Vector4 position, Vector4 color, RectInt fieldRect) =>
        !_slotValid[slot] ||
        _slotPosition[slot] != position ||
        _slotColor[slot] != color ||
        !_slotRect[slot].Equals(fieldRect);

    public bool NeedsPolarTrace(int slot, Vector4 position, Vector4 color) =>
        !_slotPolarValid[slot] || _slotPosition[slot] != position || _slotColor[slot] != color;

    public Vector2Int PolarSize(int slot) => _slotPolarSize[slot];

    public void MarkPolarTraced(int slot, Vector2Int size)
    {
        _slotPolarValid[slot] = true;
        _slotPolarSize[slot] = size;
    }

    public void MarkTraced(int slot, Vector4 position, Vector4 color, RectInt fieldRect)
    {
        _slotValid[slot] = true;
        _slotPosition[slot] = position;
        _slotColor[slot] = color;
        _slotRect[slot] = fieldRect;
    }

    public void Release()
    {
        ReleaseGpuResources();
        ReleasePolar();
        _slotByLightID.Clear();
        _lightIDBySlot = Array.Empty<int>();
        _slotInUse = Array.Empty<bool>();
        _slotSeenFrame = Array.Empty<int>();
        _slotValid = Array.Empty<bool>();
        _slotPolarValid = Array.Empty<bool>();
        _slotPolarSize = Array.Empty<Vector2Int>();
        _slotPosition = Array.Empty<Vector4>();
        _slotColor = Array.Empty<Vector4>();
        _slotRect = Array.Empty<RectInt>();
        Capacity = 0;
        _tileWidth = 0;
        _tileHeight = 0;
        _singleLightDirect = false;
    }

    private void ReleaseGpuResources()
    {
        if (Tiles != null)
        {
            Tiles.Release();
            DestroyObject(Tiles);
            Tiles = null;
        }

        TileInfos?.Release();
        TileInfos = null;
    }

    private static int RoundUpToQuantum(int value) =>
        Math.Max(TileQuantum, ((value + TileQuantum - 1) / TileQuantum) * TileQuantum);

    private static void DestroyObject(UnityEngine.Object target)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
