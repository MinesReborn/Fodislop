#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MapViewportRenderer : IDisposable
{
    private const int ChunkDim = 16;
    private const int ChunkUints = 64; // 16 * 16 bytes = 256 bytes = 64 uints

    private readonly Color32[] _cellColorTable = new Color32[256];
    private readonly Vector4[] _paletteArray = new Vector4[256];

    private ComputeShader? _computeShader;
    private int _kernelHandle = -1;

    private ComputeBuffer? _paletteBuffer;
    private ComputeBuffer? _chunkDataBuffer;
    private ComputeBuffer? _chunkLookupBuffer;

    private uint[]? _chunkDataArray;
    private int[]? _chunkLookupArray;
    private Texture2D? _dummyMipTexture;

    private int _lastMinChunkX = int.MinValue;
    private int _lastMaxChunkX = int.MinValue;
    private int _lastMinChunkY = int.MinValue;
    private int _lastMaxChunkY = int.MinValue;
    private int _lastSamplerRevision = -1;
    private int _lastLoadedChunkCount;

    public Color32[] CellColorTable => _cellColorTable;

    public MapViewportRenderer()
    {
        for (int i = 0; i < 256; i++)
        {
            CellType type = (CellType)i;
            Color32 c = MapBlockColors.GetColor32(type);
            _cellColorTable[i] = c;
            Color cLinear = ((Color)c).linear;
            _paletteArray[i] = new Vector4(cLinear.r, cLinear.g, cLinear.b, c.a / 255f);
        }
    }

    public void InitColorTable(MapManager manager)
    {
        if (manager == null)
        {
            throw new InvalidOperationException("[MapViewportRenderer] Cannot build color table: map manager is not initialized");
        }

        Color32[] colors = MapProjection.BuildCellColorTable(manager);
        Array.Copy(colors, _cellColorTable, colors.Length);

        for (int i = 0; i < 256; i++)
        {
            Color c = (Color)_cellColorTable[i];
            Color cLinear = c.linear;
            _paletteArray[i] = new Vector4(cLinear.r, cLinear.g, cLinear.b, c.a);
        }

        _paletteBuffer?.SetData(_paletteArray);
    }

    public bool Render(
        RenderTexture? mapTexture,
        MapManager manager,
        MapCellSampler cellSampler,
        WorldMapMipCache? mipCache,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY)
    {
        if (manager == null)
        {
            return false;
        }

        return Render(
            mapTexture,
            manager.WorldWidth,
            manager.WorldHeight,
            cellSampler,
            mipCache,
            texWidth,
            texHeight,
            cellsPerPixel,
            viewCenterX,
            viewCenterY);
    }

    public bool Render(
        RenderTexture? mapTexture,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler,
        WorldMapMipCache? mipCache,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY)
    {
        if (mapTexture == null)
        {
            return false;
        }

        int worldW = worldWidth;
        int worldH = worldHeight;
        float cp = cellsPerPixel;
        float cx = viewCenterX;
        float cy = viewCenterY;
        int texW = texWidth;
        int texH = texHeight;

        if (texW <= 0 || texH <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(texWidth), "Map texture dimensions must be positive.");
        }

        if (cp <= 0f || float.IsNaN(cp) || float.IsInfinity(cp))
        {
            throw new ArgumentOutOfRangeException(nameof(cellsPerPixel), "Map scale must be finite and positive.");
        }

        EnsureComputeResources();

        float startWorldX = cx + (0.5f - texW * 0.5f) * cp;
        float startWorldY = cy + (texH * 0.5f - 0.5f) * cp;
        bool useMip = mipCache != null && cp >= mipCache.ChunkSize;

        int minChunkX = 0;
        int minChunkY = 0;
        int gridWidth = 1;
        int gridHeight = 1;
        int chunkSlot = 0;

        if (!useMip)
        {
            float minWorldX = startWorldX;
            float maxWorldX = startWorldX + (texW - 1) * cp;
            float minWorldY = cy + (0.5f - texH * 0.5f) * cp;
            float maxWorldY = cy + (texH * 0.5f - 0.5f) * cp;

            int maxChunkCoordX = Mathf.Max(0, (worldW + ChunkDim - 1) / ChunkDim - 1);
            int maxChunkCoordY = Mathf.Max(0, (worldH + ChunkDim - 1) / ChunkDim - 1);

            minChunkX = Mathf.Clamp(Mathf.FloorToInt(minWorldX / ChunkDim), 0, maxChunkCoordX);
            int maxChunkX = Mathf.Clamp(Mathf.FloorToInt(maxWorldX / ChunkDim), 0, maxChunkCoordX);
            minChunkY = Mathf.Clamp(Mathf.FloorToInt(minWorldY / ChunkDim), 0, maxChunkCoordY);
            int maxChunkY = Mathf.Clamp(Mathf.FloorToInt(maxWorldY / ChunkDim), 0, maxChunkCoordY);

            gridWidth = Mathf.Max(1, maxChunkX - minChunkX + 1);
            gridHeight = Mathf.Max(1, maxChunkY - minChunkY + 1);
            int gridArea = gridWidth * gridHeight;

            int heightChunks = cellSampler.HeightChunks;
            EnsureBuffers(gridArea, 4096);

            bool gridUnchanged = minChunkX == _lastMinChunkX &&
                                 maxChunkX == _lastMaxChunkX &&
                                 minChunkY == _lastMinChunkY &&
                                 maxChunkY == _lastMaxChunkY &&
                                 cellSampler.Revision == _lastSamplerRevision &&
                                 _chunkLookupBuffer != null &&
                                 _chunkLookupBuffer.count >= gridArea;

            if (gridUnchanged)
            {
                chunkSlot = _lastLoadedChunkCount;
            }
            else
            {
                _lastMinChunkX = minChunkX;
                _lastMaxChunkX = maxChunkX;
                _lastMinChunkY = minChunkY;
                _lastMaxChunkY = maxChunkY;
                _lastSamplerRevision = cellSampler.Revision;

                if (gridArea <= 4096)
                {
                    for (int gy = 0; gy < gridHeight; gy++)
                    {
                        int chunkY = minChunkY + gy;
                        int rowOffset = gy * gridWidth;
                        for (int gx = 0; gx < gridWidth; gx++)
                        {
                            int chunkX = minChunkX + gx;
                            if (cellSampler.TryGetChunk(chunkX, chunkY, out CellType[]? chunk) && chunk != null && chunkSlot < 4096)
                            {
                                _chunkLookupArray![rowOffset + gx] = chunkSlot;
                                PackChunk(chunk, _chunkDataArray!, chunkSlot);
                                chunkSlot++;
                            }
                            else
                            {
                                _chunkLookupArray![rowOffset + gx] = -1;
                            }
                        }
                    }
                }
                else
                {
                    Array.Fill(_chunkLookupArray!, -1, 0, gridArea);

                    if (cellSampler.Layer != null && heightChunks > 0)
                    {
                        foreach (int chunkIndex in cellSampler.Layer.GetLoadedChunkIndices())
                        {
                            int chunkX = chunkIndex / heightChunks;
                            int chunkY = chunkIndex % heightChunks;

                            if (chunkX >= minChunkX && chunkX <= maxChunkX &&
                                chunkY >= minChunkY && chunkY <= maxChunkY)
                            {
                                int gx = chunkX - minChunkX;
                                int gy = chunkY - minChunkY;
                                if (cellSampler.TryGetChunk(chunkX, chunkY, out CellType[]? chunk) && chunk != null && chunkSlot < 4096)
                                {
                                    _chunkLookupArray![gy * gridWidth + gx] = chunkSlot;
                                    PackChunk(chunk, _chunkDataArray!, chunkSlot);
                                    chunkSlot++;
                                }
                            }
                        }
                    }
                }

                _lastLoadedChunkCount = chunkSlot;
                _chunkLookupBuffer!.SetData(_chunkLookupArray, 0, 0, gridArea);
                if (chunkSlot > 0)
                {
                    _chunkDataBuffer!.SetData(_chunkDataArray, 0, 0, chunkSlot * ChunkUints);
                }
            }
        }
        else
        {
            EnsureBuffers(1, 1);
        }

        Texture mipTextureToBind = useMip ? mipCache!.GetOrCreateMipTexture() : GetDummyMipTexture();

        _computeShader!.SetVector("_StartWorld", new Vector4(startWorldX, startWorldY, 0f, 0f));
        _computeShader.SetVector("_TexSize", new Vector4(texW, texH, 0f, 0f));
        _computeShader.SetFloat("_CellsPerPixel", cp);
        _computeShader.SetInt("_WorldWidth", worldW);
        _computeShader.SetInt("_WorldHeight", worldH);
        _computeShader.SetInt("_UseMip", useMip ? 1 : 0);
        _computeShader.SetInt("_GridMinChunkX", minChunkX);
        _computeShader.SetInt("_GridMinChunkY", minChunkY);
        _computeShader.SetInt("_GridWidth", gridWidth);
        _computeShader.SetInt("_GridHeight", gridHeight);

        _computeShader.SetTexture(_kernelHandle, "_Result", mapTexture);
        _computeShader.SetTexture(_kernelHandle, "_MipTexture", mipTextureToBind);
        _computeShader.SetBuffer(_kernelHandle, "_Palette", _paletteBuffer);
        _computeShader.SetBuffer(_kernelHandle, "_ChunkData", _chunkDataBuffer);
        _computeShader.SetBuffer(_kernelHandle, "_ChunkLookup", _chunkLookupBuffer);

        int groupsX = Mathf.CeilToInt(texW / 8f);
        int groupsY = Mathf.CeilToInt(texH / 8f);
        _computeShader.Dispatch(_kernelHandle, groupsX, groupsY, 1);
        return chunkSlot > 0 || useMip;
    }

    public void Dispose()
    {
        _paletteBuffer?.Release();
        _paletteBuffer = null;

        _chunkDataBuffer?.Release();
        _chunkDataBuffer = null;

        _chunkLookupBuffer?.Release();
        _chunkLookupBuffer = null;

        if (_dummyMipTexture != null)
        {
            UnityEngine.Object.Destroy(_dummyMipTexture);
            _dummyMipTexture = null;
        }
    }

    private void EnsureComputeResources()
    {
        if (_computeShader == null)
        {
            _computeShader = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldMapCompute) ??
                throw new InvalidOperationException($"[MapViewportRenderer] Resources/{ProjectRuntimeContracts.ResourcePaths.WorldMapCompute}.compute is missing.");
            _kernelHandle = _computeShader.FindKernel("CSWorldMapRender");
        }

        if (_paletteBuffer == null)
        {
            _paletteBuffer = new ComputeBuffer(256, sizeof(float) * 4);
            _paletteBuffer.SetData(_paletteArray);
        }
    }

    private void EnsureBuffers(int requiredLookupSize, int requiredChunkSlots)
    {
        if (_chunkLookupBuffer == null || _chunkLookupBuffer.count < requiredLookupSize)
        {
            _chunkLookupBuffer?.Release();
            _chunkLookupBuffer = new ComputeBuffer(requiredLookupSize, sizeof(int));
            _chunkLookupArray = new int[requiredLookupSize];
        }

        int requiredChunkUints = requiredChunkSlots * ChunkUints;
        if (_chunkDataBuffer == null || _chunkDataBuffer.count < requiredChunkUints)
        {
            _chunkDataBuffer?.Release();
            _chunkDataBuffer = new ComputeBuffer(requiredChunkUints, sizeof(uint));
            _chunkDataArray = new uint[requiredChunkUints];
        }
    }

    private Texture2D GetDummyMipTexture()
    {
        if (_dummyMipTexture == null)
        {
            _dummyMipTexture = RuntimeTextureFactory.CreateRGBA32NoMip(
                1,
                1,
                "WorldMapDummyMipTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _dummyMipTexture.SetPixel(0, 0, Color.black);
            _dummyMipTexture.Apply(false, false);
        }

        return _dummyMipTexture;
    }

    private static void PackChunk(CellType[] chunk, uint[] dest, int chunkSlot)
    {
        int baseOffset = chunkSlot * ChunkUints;
        for (int i = 0; i < ChunkUints; i++)
        {
            int cellIdx = i * 4;
            dest[baseOffset + i] =
                (uint)(byte)chunk[cellIdx] |
                ((uint)(byte)chunk[cellIdx + 1] << 8) |
                ((uint)(byte)chunk[cellIdx + 2] << 16) |
                ((uint)(byte)chunk[cellIdx + 3] << 24);
        }
    }
}
