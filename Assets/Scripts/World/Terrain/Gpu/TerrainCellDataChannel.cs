#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Owns the GPU textures and fixed-size staging textures for one terrain cell-data channel.
/// </summary>
internal sealed class TerrainCellDataChannel<T>(TextureFormat format, string name)
    where T : struct
{
    private const int StagingRows = 128;
    private readonly List<Texture2D> _staging = [];

    public Texture2D? Target;
    public T[] Data = [];

    public static long CopyTicks;
    public static long ApplyTicks;

    public int StagingHeight => Math.Min(StagingRows, Target!.height);
    public int StagingWidth => Target!.width;

    public void Allocate(int width, int height)
    {
        Target = Create(width, height, format, name);
        Data = new T[width * height];
        EnsureStagingSlot(0);
    }

    public void UploadAll()
    {
        NativeArray<T> pixels = Target!.GetPixelData<T>(0);
        pixels.CopyFrom(Data);
        Target.Apply(false, false);
    }

    public void EnsureStagingSlot(int slot)
    {
        while (_staging.Count <= slot)
        {
            _staging.Add(Create(StagingWidth, StagingHeight, format, name + "Staging" + _staging.Count));
        }
    }

    public void Stage(int slot, List<TerrainStagedPiece> pieces, int start, int end)
    {
        long copyStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Texture2D staging = _staging[slot];
        int textureWidth = Target!.width;
        int stagingWidth = staging.width;
        NativeArray<T> pixels = staging.GetPixelData<T>(0);
        for (int index = start; index < end; index++)
        {
            TerrainStagedPiece piece = pieces[index];
            RectInt target = piece.Target;
            for (int row = 0; row < target.height; row++)
            {
                NativeArray<T>.Copy(
                    Data,
                    ((target.y + row) * textureWidth) + target.x,
                    pixels,
                    ((piece.StageY + row) * stagingWidth) + piece.StageX,
                    target.width);
            }
        }

        CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - copyStart;

        long applyStart = System.Diagnostics.Stopwatch.GetTimestamp();
        staging.Apply(false, false);
        ApplyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - applyStart;
    }

    public void CopyStaged(int slot, List<TerrainStagedPiece> pieces, int start, int end)
    {
        Texture2D staging = _staging[slot];
        for (int index = start; index < end; index++)
        {
            TerrainStagedPiece piece = pieces[index];
            RectInt target = piece.Target;
            Graphics.CopyTexture(
                staging, 0, 0, piece.StageX, piece.StageY, target.width, target.height,
                Target!, 0, 0, target.x, target.y);
        }
    }

    public void Destroy()
    {
        DestroyTexture(ref Target);
        for (int index = 0; index < _staging.Count; index++)
        {
            Texture2D? staging = _staging[index];
            DestroyTexture(ref staging);
        }

        _staging.Clear();
        Data = [];
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
