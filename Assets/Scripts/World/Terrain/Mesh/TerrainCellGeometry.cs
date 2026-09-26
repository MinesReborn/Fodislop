#nullable enable

using System;
using UnityEngine;

namespace Kern.World.Terrain;

// Canonical local geometry for one terrain cell. The GPU cell path rebuilds the
// same four corners from the cell geometry textures; CPU overlays use this
// value directly for POSITION. Keeping the corner order here prevents each
// path from inventing its own anchor convention.
public readonly record struct TerrainCellGeometry(
    Vector2 Corner00,
    Vector2 Corner10,
    Vector2 Corner11,
    Vector2 Corner01)
{
    public const int GridSize = TerrainVertexOffset.GridSize;

    // Organic edge data is part of the cell shape contract. The base-5 digits
    // store bottom, right, top, left bends in [-2, 2]; zero is reserved for a
    // non-organic cell, so encoded organic values are [1, 625].
    public const int OrganicEdgeBase = 5;
    public const int OrganicEdgeCenter = 2;
    public const int OrganicEdgeMetadataOffset = 128;
    public const int MaximumOrganicEdgeCode = 625;

    public bool IsAnchored =>
        Corner00 != new Vector2(0f, 0f) ||
        Corner10 != new Vector2(1f, 0f) ||
        Corner11 != new Vector2(1f, 1f) ||
        Corner01 != new Vector2(0f, 1f);

    public static TerrainCellGeometry FromOffsets(
        Vector3 offset00,
        Vector3 offset10,
        Vector3 offset11,
        Vector3 offset01)
    {
        return new TerrainCellGeometry(
            new Vector2(offset00.x, offset00.y),
            new Vector2(1f + offset10.x, offset10.y),
            new Vector2(1f + offset11.x, 1f + offset11.y),
            new Vector2(offset01.x, 1f + offset01.y));
    }

    public static int EncodeOrganicEdges(int bottom, int right, int top, int left)
    {
        int code = 1;
        int multiplier = 1;
        code += EncodeOrganicBend(bottom) * multiplier;
        multiplier *= OrganicEdgeBase;
        code += EncodeOrganicBend(right) * multiplier;
        multiplier *= OrganicEdgeBase;
        code += EncodeOrganicBend(top) * multiplier;
        multiplier *= OrganicEdgeBase;
        code += EncodeOrganicBend(left) * multiplier;
        return code;
    }

    // Meta.b stores the low byte; Meta.a uses 0 for regular, 255 for classic,
    // and 128 + high bits for organic geometry.
    public static (byte LowByte, byte HighByte) PackOrganicEdgeMetadata(int code)
    {
        if (code < 0 || code > MaximumOrganicEdgeCode)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        return code == 0
            ? ((byte)0, (byte)0)
            : ((byte)(code & 0xFF), (byte)(OrganicEdgeMetadataOffset + (code >> 8)));
    }

    private static int EncodeOrganicBend(int bend)
    {
        if (bend < -2 || bend > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(bend));
        }

        return bend + OrganicEdgeCenter;
    }

    public Vector2 GetCorner(int corner)
    {
        return corner switch
        {
            0 => Corner00,
            1 => Corner10,
            2 => Corner11,
            3 => Corner01,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };
    }
}
