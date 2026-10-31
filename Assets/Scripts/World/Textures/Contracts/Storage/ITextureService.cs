#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.World;
using MinesServer.Data;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Core.Interfaces;
public interface ITextureService
{
    event Action<CellType, Texture2D>? OnTextureLoaded;
    event Action? OnTerrainDecalAtlasLoaded;
    int PendingCellTextureRequests { get; }
    /// <summary>
    /// True when every payload in the current world asset manifest is retained
    /// in memory and the decal atlas is ready.
    /// </summary>
    bool IsWorldAssetsReady { get; }
    void RequestTexture(CellType cellType);
    AtlasCoordinate GetCellTextureCoordinate(CellType cellType);
    Vector4 GetCellFrameRect(CellType cellType);
    int GetAnimationFrameCount(CellType cellType);
    UniTask<AtlasCoordinate> GetCellTextureCoordinate(
        CellType cellType,
        int globalX,
        int globalY);
    Texture2D? PrismaticFlowMapTexture { get; }
    Texture2D? FlowMapTexture { get; }
    Texture2DArray? TerrainDecalAtlasArray { get; }
    void LoadWorldAssets(
        IReadOnlyList<WorldAssetDefinitionPacket> assetManifest);
    IReadOnlyList<IAtlasDescriptor> GetAllAtlases();
    string GetCacheStats();
    void FlushDirtyAtlases();
    void Clear();
}
