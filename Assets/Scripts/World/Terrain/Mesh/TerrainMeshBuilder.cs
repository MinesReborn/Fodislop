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
                    _bgAtlasIndices[quadIdx] = FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, baseIdx, atlases, useColorLod, mapManager, textureManager);
                    _fgAtlasIndices[quadIdx] = FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod, mapManager, textureManager);
                }
            });

            for (int i = 0; i < subMeshIndices.Length; i++)
            {
                subMeshIndices[i].Clear();
            }

            // Ultra-fast flat index collection without dictionary or cache lookups
            int totalQuads = meshWidth * meshHeight;
            for (int i = 0; i < totalQuads; i++)
            {
                int bgAtlas = _bgAtlasIndices[i];
                if (bgAtlas >= 0 && bgAtlas < subMeshIndices.Length)
                {
                    var bgList = subMeshIndices[bgAtlas];
                    int baseIdx = i * 8;
                    bgList.Add(baseIdx + 0);
                    bgList.Add(baseIdx + 3);
                    bgList.Add(baseIdx + 2);
                    bgList.Add(baseIdx + 2);
                    bgList.Add(baseIdx + 1);
                    bgList.Add(baseIdx + 0);
                }

                int fgAtlas = _fgAtlasIndices[i];
                if (fgAtlas >= 0 && fgAtlas < subMeshIndices.Length)
                {
                    var fgList = subMeshIndices[fgAtlas];
                    int fgIdx = (i * 8) + 4;
                    fgList.Add(fgIdx + 0);
                    fgList.Add(fgIdx + 3);
                    fgList.Add(fgIdx + 2);
                    fgList.Add(fgIdx + 2);
                    fgList.Add(fgIdx + 1);
                    fgList.Add(fgIdx + 0);
                }
            }
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
                    _bgAtlasIndices[quadIdx] = FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, baseIdx, atlases, useColorLod, mapManager, textureManager);
                    _fgAtlasIndices[quadIdx] = FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod, mapManager, textureManager);
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

            for (int i = 0; i < subMeshIndices.Length; i++)
            {
                subMeshIndices[i].Clear();
            }

            int totalQuads = meshWidth * meshHeight;
            for (int i = 0; i < totalQuads; i++)
            {
                int bgAtlas = _bgAtlasIndices[i];
                if (bgAtlas >= 0 && bgAtlas < subMeshIndices.Length)
                {
                    var bgList = subMeshIndices[bgAtlas];
                    int baseIdx = i * 8;
                    bgList.Add(baseIdx + 0);
                    bgList.Add(baseIdx + 3);
                    bgList.Add(baseIdx + 2);
                    bgList.Add(baseIdx + 2);
                    bgList.Add(baseIdx + 1);
                    bgList.Add(baseIdx + 0);
                }

                int fgAtlas = _fgAtlasIndices[i];
                if (fgAtlas >= 0 && fgAtlas < subMeshIndices.Length)
                {
                    var fgList = subMeshIndices[fgAtlas];
                    int fgIdx = (i * 8) + 4;
                    fgList.Add(fgIdx + 0);
                    fgList.Add(fgIdx + 3);
                    fgList.Add(fgIdx + 2);
                    fgList.Add(fgIdx + 2);
                    fgList.Add(fgIdx + 1);
                    fgList.Add(fgIdx + 0);
                }
            }
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
                    _bgAtlasIndices[quadIndex] = FillQuadData(
                        x, y, gridX, minY + y, cellCache, precalc, bgFloodFill,
                        worldWidth, worldHeight, true, baseIndex, atlases, useColorLod,
                        mapManager, textureManager);
                    _fgAtlasIndices[quadIndex] = FillQuadData(
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
                    List<int> indices = subMeshIndices[backgroundAtlas];
                    int baseIndex = i * 8;
                    indices.Add(baseIndex);
                    indices.Add(baseIndex + 3);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex);
                }

                int foregroundAtlas = _fgAtlasIndices[i];
                if (foregroundAtlas >= 0 && foregroundAtlas < subMeshIndices.Length)
                {
                    List<int> indices = subMeshIndices[foregroundAtlas];
                    int baseIndex = (i * 8) + 4;
                    indices.Add(baseIndex);
                    indices.Add(baseIndex + 3);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex);
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

                List<int> indices = overlaySubMeshIndices[foregroundAtlas];
                int baseIndex = (i * 8) + 4;
                indices.Add(baseIndex);
                indices.Add(baseIndex + 3);
                indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex);
            }
        }

        private static bool IsBuildingBlock(CellType type)
        {
            return type is CellType.BuildingWall or
                CellType.BuildingDoor or
                CellType.BuildingCorner;
        }

        private int FillQuadData(int x, int y, int gridX, int unityY, TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int worldWidth, int worldHeight, bool isBackground, int vIdx, IReadOnlyList<IAtlasDescriptor> atlases, bool useColorLod,
            MapManager mapManager, ITextureService textureManager)
        {
            if (unityY < 0 || unityY >= worldHeight || gridX < 0 || gridX >= worldWidth)
            {
                return -1;
            }

            int cx = x + 1;
            int cy = y + 1;
            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

            CachedCellData ccd = cellCache.GetCellData(cx, cy);
            CellType cellFgType = ccd.Type;
            if (!isBackground)
            {
                _foregroundOverlayFlags[vIdx / 8] = cellFgType == CellType.BuildingDoor;
            }

            if (ccd.State != TerrainCellState.Loaded)
            {
                return -1;
            }

            CellType cellType = isBackground ? bgFloodFill.Buffer[x, y] : cellFgType;
            if (isBackground && IsBuildingBlock(cellFgType))
            {
                cellType = CellType.Road;
            }

            bool isSameCell = !isBackground || cellType == cellFgType;
            if (isBackground && (cellType == cellFgType || cellType == CellType.Unloaded))
            {
                return -1;
            }

            Vector4 atlasRect;
            float uvTileSize;
            CellAnimationType animType;
            float animSpeed;
            int animFrames;
            float frameHeight;
            bool hasTileGroup;
            CellConfigProperties props;
            Color32 minimapColor;
            int atlasIndex;

            if (isSameCell)
            {
                atlasRect = ccd.AtlasRect;
                uvTileSize = ccd.UVTileSize;
                animType = ccd.Animation;
                animSpeed = ccd.AnimationSpeed;
                animFrames = ccd.AnimationFrameCount;
                frameHeight = ccd.FrameHeightTiles;
                hasTileGroup = ccd.HasTileGroup;
                props = ccd.Properties;
                minimapColor = ccd.MinimapColor;
                atlasIndex = ccd.AtlasIndex;
            }
            else
            {
                var meta = cellCache.GetMetadata(cellType, mapManager, textureManager, atlases);
                atlasRect = meta.AtlasRect;
                uvTileSize = meta.UVTileSize;
                animType = meta.Animation;
                animSpeed = meta.AnimationSpeed;
                animFrames = meta.AnimationFrameCount;
                frameHeight = meta.FrameHeightTiles;
                hasTileGroup = meta.HasTileGroup;
                props = meta.Properties;
                minimapColor = meta.MinimapColor;
                atlasIndex = meta.AtlasIndex;
            }

            if (atlasIndex < 0 || atlasIndex >= atlases.Count)
            {
                atlasIndex = 0;
            }

            bool hasTexture = atlasRect.z > 0f && atlasRect.w > 0f && uvTileSize > 0f;
            if (!hasTexture)
            {
                atlasRect = Vector4.zero;
                uvTileSize = atlases.Count > 0 ? (1f / atlases[0].Size) : 0f;
                animType = CellAnimationType.None;
                animSpeed = 0f;
                animFrames = 1;
                frameHeight = 1f;
            }

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = x * _cellSize;
            float ly = y * _cellSize;

            Vector3 off00 = precalc.GridVertexOffsets[x, y];
            Vector3 off10 = precalc.GridVertexOffsets[x + 1, y];
            Vector3 off01 = precalc.GridVertexOffsets[x, y + 1];
            Vector3 off11 = precalc.GridVertexOffsets[x + 1, y + 1];

            bool isAnchored = off00 != Vector3.zero || off10 != Vector3.zero || off01 != Vector3.zero || off11 != Vector3.zero;
            float anchorFlag = isAnchored ? 1f : 0f;
            Vector2 anchor0 = isAnchored ? new Vector2(off00.x, off00.y) : new Vector2(0f, 0f);
            Vector2 anchor1 = isAnchored ? new Vector2(1f + off10.x, off10.y) : new Vector2(1f, 0f);
            Vector2 anchor2 = isAnchored ? new Vector2(1f + off11.x, 1f + off11.y) : new Vector2(1f, 1f);
            Vector2 anchor3 = isAnchored ? new Vector2(off01.x, 1f + off01.y) : new Vector2(0f, 1f);

            _vertexBuffer[vIdx + 0].Position = new Vector3(lx, ly, zOffset) + off00;
            _vertexBuffer[vIdx + 1].Position = new Vector3(lx + _cellSize, ly, zOffset) + off10;
            _vertexBuffer[vIdx + 2].Position = new Vector3(lx + _cellSize, ly + _cellSize, zOffset) + off11;
            _vertexBuffer[vIdx + 3].Position = new Vector3(lx, ly + _cellSize, zOffset) + off01;

            Vector2 uv0 = new Vector2(0, 0);
            Vector2 uv1 = new Vector2(1, 0);
            Vector2 uv2 = new Vector2(1, 1);
            Vector2 uv3 = new Vector2(0, 1);

            int descriptor = isSameCell ? precalc.CellTilingDescriptors[x, y] : 0;
            int cornerSideMask = precalc.CellCornerVariants[x, y];
            bool useNeighborVariants =
                !isBackground &&
                cellFgType == CellType.BuildingWall &&
                cornerSideMask != 0;
            float packedW = hasTileGroup || useNeighborVariants ? 1f : 0f;
            if (useNeighborVariants)
            {
                bool hasLeft = (cornerSideMask & 1) != 0;
                bool hasRight = (cornerSideMask & 2) != 0;
                bool hasTop = (cornerSideMask & 4) != 0;
                bool hasBottom = (cornerSideMask & 8) != 0;
                int cornerCount =
                    (hasLeft ? 1 : 0) +
                    (hasRight ? 1 : 0) +
                    (hasTop ? 1 : 0) +
                    (hasBottom ? 1 : 0);
                int column = RenderingConstants.BUILDING_WALL_VARIANT_BASE_TILE +
                    Math.Min(cornerCount, 2);
                byte transforms = (byte)(descriptor & 0xE0);
                if ((cornerCount == 1 && hasRight) ||
                    (cornerCount == 1 && hasBottom))
                {
                    transforms ^= 0x40;
                }

                if (cornerCount >= 2 && !hasLeft && !hasRight)
                {
                    transforms ^= 0x80;
                }

                descriptor = transforms | (column & 0x1F);
            }

            if ((hasTileGroup || useNeighborVariants) && descriptor != 0)
            {
                if ((descriptor & 0x40) != 0)
                {
                    (uv0.x, uv1.x) = (uv1.x, uv0.x);
                    (uv3.x, uv2.x) = (uv2.x, uv3.x);
                }

                if ((descriptor & 0x20) != 0)
                {
                    (uv0.y, uv3.y) = (uv3.y, uv0.y);
                    (uv1.y, uv2.y) = (uv2.y, uv1.y);
                }

                if ((descriptor & 0x80) != 0)
                {
                    Vector2 t = uv0;
                    uv0 = uv1;
                    uv1 = uv2;
                    uv2 = uv3;
                    uv3 = t;
                }
            }

            _vertexBuffer[vIdx + 0].UV0 = uv0;
            _vertexBuffer[vIdx + 1].UV0 = uv1;
            _vertexBuffer[vIdx + 2].UV0 = uv2;
            _vertexBuffer[vIdx + 3].UV0 = uv3;

            bool useFallback = useColorLod || atlasRect.z < 0.0001f;
            Color color = useFallback ? (Color)minimapColor : Color.white;
            if (atlasRect.z < 0.0001f)
            {
                color.a = 1f;
            }

            float animOffset = 0f;
            if (!useFallback && animType == CellAnimationType.Blinking)
            {
                uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
                seed = (seed ^ (seed >> 13)) * 1274126177;
                seed = seed ^ (seed >> 16);
                animOffset = (seed % 6283) / 1000f;
            }

            // Любой непустой блок переднего плана — физическая масса: свет обязан
            // поглощаться всеми блоками одинаково, без зависимости от уникальных
            // свойств DropsShadow/Passable (иначе у блоков без DropsShadow
            // occupancy = 0 и свет проходит насквозь).
            bool isPhysicalMass =
                !isBackground &&
                cellFgType != CellType.Empty;
            Vector4 animDataVec = new(
                (float)animType,
                animSpeed,
                animOffset,
                0f); // reserved (was isPhysicalMass — dead; encoded in UV6.y bit 6)
            Vector4 tileSizeVec = new Vector4(uvTileSize, uvTileSize, (float)animFrames, frameHeight);
            Vector4 worldPosVec = new Vector4(gridX, serverY, descriptor & 0x1F, packedW);

            // reliefValue removed: was written to UV5.y but never read by any shader pass.

            _vertexBuffer[vIdx].Color = color;
            _vertexBuffer[vIdx].UV1 = atlasRect;
            _vertexBuffer[vIdx].UV2 = tileSizeVec;
            _vertexBuffer[vIdx].UV3 = worldPosVec;
            _vertexBuffer[vIdx].UV4 = animDataVec;
            bool isGlowing = (props & CellConfigProperties.Glowing) != 0;

            // Read RGB directly from Color32 bytes — no intermediate Color allocation
            int packedLightingColor = minimapColor.r |
                (minimapColor.g << 8) |
                (minimapColor.b << 16);

            // UV5 layout: (anchorFlag, anchor.x, anchor.y, 0)
            // Shader reads: packedData.x = anchorFlag, packedData.yz = anchoredUV
            _vertexBuffer[vIdx].UV5 = new Vector4(anchorFlag, anchor0.x, anchor0.y, 0f);

            _vertexBuffer[vIdx + 1].Color = color;
            _vertexBuffer[vIdx + 1].UV1 = atlasRect;
            _vertexBuffer[vIdx + 1].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 1].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 1].UV4 = animDataVec;
            _vertexBuffer[vIdx + 1].UV5 = new Vector4(anchorFlag, anchor1.x, anchor1.y, 0f);

            _vertexBuffer[vIdx + 2].Color = color;
            _vertexBuffer[vIdx + 2].UV1 = atlasRect;
            _vertexBuffer[vIdx + 2].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 2].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 2].UV4 = animDataVec;
            _vertexBuffer[vIdx + 2].UV5 = new Vector4(anchorFlag, anchor2.x, anchor2.y, 0f);

            _vertexBuffer[vIdx + 3].Color = color;
            _vertexBuffer[vIdx + 3].UV1 = atlasRect;
            _vertexBuffer[vIdx + 3].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 3].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 3].UV4 = animDataVec;
            _vertexBuffer[vIdx + 3].UV5 = new Vector4(anchorFlag, anchor3.x, anchor3.y, 0f);

            float glowFlags = 0f;
            if (isGlowing)
            {
                glowFlags += 1f;
            }

            if (!isBackground && MapManager.IsRoundableLoose(cellFgType))
            {
                glowFlags += 2f;
            }

            byte solidConnectivityMask = !isBackground && isSameCell
                ? precalc.CellSolidBoundaryMasks[x, y]
                : (byte)0;
            float solidBoundaryMask = solidConnectivityMask & 15;
            float solidDiagonalMask = solidConnectivityMask >> 4;
            bool hasRoundedPhysicalContour =
                !isBackground && MapManager.IsRoundableLoose(cellFgType);
            float emissionPower = isGlowing
                ? Mathf.Max(1f / byte.MaxValue, minimapColor.a / 255f)
                : 0f;
            float packedLightingFlags = solidBoundaryMask +
                (isGlowing ? 16f : 0f) +
                (hasRoundedPhysicalContour ? 32f : 0f) +
                (isPhysicalMass ? 64f : 0f) +
                (emissionPower * 0.25f);
            Vector4 glowVec = new Vector4(
                packedLightingColor,
                packedLightingFlags,
                glowFlags + (solidDiagonalMask * 4f),
                0f); // was solidBoundaryMask — redundant; shader now derives it via int(glowData.y) & 15
            _vertexBuffer[vIdx + 0].UV6 = glowVec;
            _vertexBuffer[vIdx + 1].UV6 = glowVec;
            _vertexBuffer[vIdx + 2].UV6 = glowVec;
            _vertexBuffer[vIdx + 3].UV6 = glowVec;

            return atlasIndex;
        }
    }
}
