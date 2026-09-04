#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    public class TerrainMeshBuilder
    {
        private TerrainVertex[] _vertexBuffer = Array.Empty<TerrainVertex>();
        private float _cellSize;
        public TerrainVertex[] VertexBuffer => _vertexBuffer;

        private int[] _bgAtlasIndices = Array.Empty<int>();
        private int[] _fgAtlasIndices = Array.Empty<int>();
        private bool[] _foregroundOverlayFlags = Array.Empty<bool>();

        /// <summary>
        /// Whether the last <see cref="BuildRegion"/> changed which submesh any
        /// quad belongs to, and so requires the index lists to be re-uploaded.
        /// </summary>
        /// <remarks>
        /// Almost always false. A quad's submesh is its texture atlas, and a
        /// streamed chunk or a mined cell changes the cell's appearance far
        /// more often than it moves that cell onto a different atlas. Rebuilding
        /// the lists regardless meant every incremental patch, however small,
        /// cleared every submesh list and re-appended twelve ints for every quad
        /// in the viewport - the whole grid's worth of List&lt;int&gt;.Add on the
        /// main thread, to usually reproduce the identical lists.
        /// </remarks>
        public bool IndicesChanged { get; private set; }

        public bool OverlayIndicesChanged { get; private set; }

        /// <summary>
        /// The span of <see cref="VertexBuffer"/> the last
        /// <see cref="BuildRegion"/> actually wrote, as a vertex offset and
        /// count. Zero count means it wrote nothing.
        /// </summary>
        /// <remarks>
        /// Quads are indexed x-major (<c>x * meshHeight + y</c>), so a
        /// rectangle occupies one contiguous run per column and this span is
        /// the smallest range covering all of them - tight when the dirty rect
        /// is narrow in x, which is the common case for a walking player.
        /// </remarks>
        public int DirtyVertexStart { get; private set; }

        public int DirtyVertexCount { get; private set; }

        public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
        {
            _cellSize = cellSize;
            int quadCount = meshWidth * meshHeight * 2;
            int vertCount = quadCount * 4;

            if (_vertexBuffer == null || _vertexBuffer.Length != vertCount)
            {
                _vertexBuffer = new TerrainVertex[vertCount];
            }

            int singleLayerQuads = meshWidth * meshHeight;
            if (_bgAtlasIndices.Length != singleLayerQuads)
            {
                _bgAtlasIndices = new int[singleLayerQuads];
                _fgAtlasIndices = new int[singleLayerQuads];
                _foregroundOverlayFlags = new bool[singleLayerQuads];
            }
        }

        public void BuildFull(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int minX, int minY, int meshWidth, int meshHeight, int worldWidth, int worldHeight,
            IReadOnlyList<IAtlasDescriptor> atlases, List<int>[] subMeshIndices, bool useColorLod,
            MapManager mapManager, ITextureService textureManager)
        {
            if (atlases == null || atlases.Count == 0 || subMeshIndices == null || subMeshIndices.Length == 0)
            {
                return;
            }

            EnsureCapacity(meshWidth, meshHeight, _cellSize);

            // A full build rewrites everything, so the incremental bookkeeping
            // reports exactly that to anyone who reads it after this call.
            IndicesChanged = true;
            OverlayIndicesChanged = true;
            DirtyVertexStart = 0;
            DirtyVertexCount = _vertexBuffer.Length;

            System.Threading.Tasks.Parallel.For(0, meshWidth, x =>
            {
                int gridX = minX + x;
                for (int y = 0; y < meshHeight; y++)
                {
                    int unityY = minY + y;
                    int quadIdx = (x * meshHeight) + y;
                    int baseIdx = quadIdx * 8;
                    _bgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                        _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                        x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, true, baseIdx, atlases, useColorLod,
                        mapManager, textureManager);
                    _fgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                        _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                        x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod,
                        mapManager, textureManager);
                }
            });

            RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
        }

        public void BuildRegion(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int minX, int minY, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY, int worldWidth, int worldHeight,
            IReadOnlyList<IAtlasDescriptor> atlases, List<int>[] subMeshIndices, bool useColorLod,
            MapManager mapManager, ITextureService textureManager)
        {
            if (atlases == null || atlases.Count == 0 || subMeshIndices == null || subMeshIndices.Length == 0)
            {
                return;
            }

            int endX = Mathf.Clamp(startX + countX, 0, meshWidth);
            int endY = Mathf.Clamp(startY + countY, 0, meshHeight);
            int clampedStartX = Mathf.Clamp(startX, 0, meshWidth);
            int clampedStartY = Mathf.Clamp(startY, 0, meshHeight);

            if (endX <= clampedStartX || endY <= clampedStartY)
            {
                IndicesChanged = false;
                OverlayIndicesChanged = false;
                DirtyVertexStart = 0;
                DirtyVertexCount = 0;
                return;
            }

            int firstQuad = (clampedStartX * meshHeight) + clampedStartY;
            int lastQuad = ((endX - 1) * meshHeight) + (endY - 1);
            DirtyVertexStart = firstQuad * 8;
            DirtyVertexCount = ((lastQuad + 1) * 8) - DirtyVertexStart;

            bool atlasAssignmentChanged = false;
            bool overlayAssignmentChanged = false;
            for (int x = clampedStartX; x < endX; x++)
            {
                int gridX = minX + x;
                for (int y = clampedStartY; y < endY; y++)
                {
                    int unityY = minY + y;
                    int quadIdx = (x * meshHeight) + y;
                    int baseIdx = quadIdx * 8;
                    int previousBackgroundAtlas = _bgAtlasIndices[quadIdx];
                    int previousForegroundAtlas = _fgAtlasIndices[quadIdx];
                    bool previousOverlay = _foregroundOverlayFlags[quadIdx];
                    _bgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                        _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                        x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, true, baseIdx, atlases, useColorLod,
                        mapManager, textureManager);
                    _fgAtlasIndices[quadIdx] = TerrainQuadBuilder.FillQuadData(
                        _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                        x, y, gridX, unityY, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod,
                        mapManager, textureManager);
                    if (_bgAtlasIndices[quadIdx] != previousBackgroundAtlas ||
                        _fgAtlasIndices[quadIdx] != previousForegroundAtlas)
                    {
                        atlasAssignmentChanged = true;
                    }

                    overlayAssignmentChanged |=
                        previousOverlay != _foregroundOverlayFlags[quadIdx];
                }
            }

            IndicesChanged = atlasAssignmentChanged;
            OverlayIndicesChanged = overlayAssignmentChanged || atlasAssignmentChanged;
            if (!atlasAssignmentChanged)
            {
                // The vertices moved onto different textures within the same
                // atlases, so every triangle still belongs to the submesh it
                // already belonged to. Rebuilding the lists would reproduce
                // them byte for byte.
                return;
            }

            RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
        }

        public void BuildTextureCells(
            HashSet<CellType> cellTypes,
            TerrainCellCache cellCache,
            TerrainPrecalculator precalc,
            BackgroundFloodFill bgFloodFill,
            int minX,
            int minY,
            int meshWidth,
            int meshHeight,
            int worldWidth,
            int worldHeight,
            IReadOnlyList<IAtlasDescriptor> atlases,
            List<int>[] subMeshIndices,
            bool useColorLod,
            MapManager mapManager,
            ITextureService textureManager)
        {
            bool atlasAssignmentChanged = false;
            int firstDirtyQuad = int.MaxValue;
            int lastDirtyQuad = -1;
            for (int x = 0; x < meshWidth; x++)
            {
                int gridX = minX + x;
                for (int y = 0; y < meshHeight; y++)
                {
                    CellType foregroundType = cellCache.GetCellData(x + 1, y + 1).Type;
                    CellType backgroundType = bgFloodFill.Buffer[x, y];
                    if (!cellTypes.Contains(foregroundType) && !cellTypes.Contains(backgroundType))
                    {
                        continue;
                    }

                    int quadIndex = (x * meshHeight) + y;
                    int baseIndex = quadIndex * 8;
                    int previousBackgroundAtlas = _bgAtlasIndices[quadIndex];
                    int previousForegroundAtlas = _fgAtlasIndices[quadIndex];
                    _bgAtlasIndices[quadIndex] = TerrainQuadBuilder.FillQuadData(
                        _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                        x, y, gridX, minY + y, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, true, baseIndex, atlases, useColorLod,
                        mapManager, textureManager);
                    _fgAtlasIndices[quadIndex] = TerrainQuadBuilder.FillQuadData(
                        _vertexBuffer, _foregroundOverlayFlags, _cellSize,
                        x, y, gridX, minY + y, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, false, baseIndex + 4, atlases, useColorLod,
                        mapManager, textureManager);
                    atlasAssignmentChanged |=
                        _bgAtlasIndices[quadIndex] != previousBackgroundAtlas ||
                        _fgAtlasIndices[quadIndex] != previousForegroundAtlas;
                    firstDirtyQuad = Mathf.Min(firstDirtyQuad, quadIndex);
                    lastDirtyQuad = Mathf.Max(lastDirtyQuad, quadIndex);
                }
            }

            IndicesChanged = atlasAssignmentChanged;
            OverlayIndicesChanged = atlasAssignmentChanged;
            DirtyVertexStart = lastDirtyQuad >= 0 ? firstDirtyQuad * 8 : 0;
            DirtyVertexCount = lastDirtyQuad >= 0 ? ((lastDirtyQuad + 1) * 8) - DirtyVertexStart : 0;
            if (!atlasAssignmentChanged)
            {
                return;
            }

            RebuildSubMeshIndices(meshWidth, meshHeight, subMeshIndices);
        }

        private static void AddQuadIndices(List<int> indices, int baseIndex)
        {
            indices.Add(baseIndex);
            indices.Add(baseIndex + 3);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex);
        }

        private void RebuildSubMeshIndices(
            int meshWidth,
            int meshHeight,
            List<int>[] subMeshIndices)
        {
            for (int i = 0; i < subMeshIndices.Length; i++)
            {
                subMeshIndices[i].Clear();
            }

            int totalQuads = meshWidth * meshHeight;

            for (int i = 0; i < totalQuads; i++)
            {
                int backgroundAtlas = _bgAtlasIndices[i];

                if (backgroundAtlas >= 0 && backgroundAtlas < subMeshIndices.Length)
                {
                    AddQuadIndices(subMeshIndices[backgroundAtlas], i * 8);
                }

                int foregroundAtlas = _fgAtlasIndices[i];

                if (foregroundAtlas >= 0 && foregroundAtlas < subMeshIndices.Length)
                {
                    AddQuadIndices(subMeshIndices[foregroundAtlas], (i * 8) + 4);
                }
            }
        }

        public void RebuildOverlaySubMeshIndices(
            int meshWidth,
            int meshHeight,
            List<int>[] overlaySubMeshIndices)
        {
            foreach (List<int> indices in overlaySubMeshIndices)
            {
                indices.Clear();
            }

            int totalQuads = meshWidth * meshHeight;

            for (int i = 0; i < totalQuads; i++)
            {
                int foregroundAtlas = _fgAtlasIndices[i];

                if (!_foregroundOverlayFlags[i] ||
                    foregroundAtlas < 0 ||
                    foregroundAtlas >= overlaySubMeshIndices.Length)
                {
                    continue;
                }

                AddQuadIndices(overlaySubMeshIndices[foregroundAtlas], (i * 8) + 4);
            }
        }
    }
}
