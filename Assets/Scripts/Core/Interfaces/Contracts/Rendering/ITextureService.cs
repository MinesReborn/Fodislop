#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Core.Interfaces;
public interface ITextureService
{
    event Action<string, Texture2D>? OnTextureLoaded;
    int PendingCellTextureRequests { get; }
    void RequestTexture(CellType cellType);
    AtlasCoordinate GetCellTextureCoordinate(CellType cellType);
    Vector4 GetCellFrameRect(CellType cellType);
    int GetAnimationFrameCount(CellType cellType);
    int GetFrameSize(CellType cellType);
    float GetAnimationSpeedForCell(CellType cellType);
    UniTask<AtlasCoordinate> GetCellTextureCoordinate(
        CellType cellType,
        int globalX,
        int globalY);
    Texture2D? FlowMapTexture { get; }
    IReadOnlyList<IAtlasDescriptor> GetAllAtlases();
    string GetCacheStats();
    void FlushDirtyAtlases();
    void Clear();
}
