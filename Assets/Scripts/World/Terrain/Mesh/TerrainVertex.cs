#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Kern.World.Terrain;
[StructLayout(LayoutKind.Explicit, Size = 84)]
public struct TerrainVertex
{
    // ── Float32 ────────────────────────────────────── offset  bytes
    [FieldOffset(0)]  public Vector3 Position;    //  0    12
    [FieldOffset(12)] public Color32 Color;        // 12     4

    // ── Float16 raw storage — UV0 ─────────────────
    [FieldOffset(16)] public ushort UV0x;          // 16     2
    [FieldOffset(18)] public ushort UV0y;          // 18     2

    // ── Float16 raw storage — UV1 ─────────────────
    [FieldOffset(20)] public ushort UV1x;          // 20     2
    [FieldOffset(22)] public ushort UV1y;          // 22     2
    [FieldOffset(24)] public ushort UV1z;          // 24     2
    [FieldOffset(26)] public ushort UV1w;          // 26     2

    // ── Float16 raw storage — UV2 ─────────────────
    [FieldOffset(28)] public ushort UV2x;          // 28     2
    [FieldOffset(30)] public ushort UV2y;          // 30     2
    [FieldOffset(32)] public ushort UV2z;          // 32     2
    [FieldOffset(34)] public ushort UV2w;          // 34     2

    // ── Float32 (world tile coords, can exceed 2048) ──
    [FieldOffset(36)] public Vector4 UV3;          // 36    16

    // ── Float16 raw storage — UV4 ─────────────────
    [FieldOffset(52)] public ushort UV4x;          // 52     2
    [FieldOffset(54)] public ushort UV4y;          // 54     2
    [FieldOffset(56)] public ushort UV4z;          // 56     2
    [FieldOffset(58)] public ushort UV4w;          // 58     2

    // ── Float16 raw storage — UV5 ─────────────────
    [FieldOffset(60)] public ushort UV5x;          // 60     2
    [FieldOffset(62)] public ushort UV5y;          // 62     2
    [FieldOffset(64)] public ushort UV5z;          // 64     2
    [FieldOffset(66)] public ushort UV5w;          // 66     2

    // ── Float32 (packed RGB color reaches 16 777 215) ──
    [FieldOffset(68)] public Vector4 UV6;          // 68    16
    //                                             ───────────
    //                                             total   84

    // ── Write-only properties: float → half ───────────────

    public Vector2 UV0
    {
        set
        {
            UV0x = H(value.x);
            UV0y = H(value.y);
        }
    }

    public Vector4 UV1
    {
        set
        {
            UV1x = H(value.x);
            UV1y = H(value.y);
            UV1z = H(value.z);
            UV1w = H(value.w);
        }
    }

    public Vector4 UV2
    {
        set
        {
            UV2x = H(value.x);
            UV2y = H(value.y);
            UV2z = H(value.z);
            UV2w = H(value.w);
        }
    }

    public Vector4 UV4
    {
        set
        {
            UV4x = H(value.x);
            UV4y = H(value.y);
            UV4z = H(value.z);
            UV4w = H(value.w);
        }
    }

    public Vector4 UV5
    {
        set
        {
            UV5x = H(value.x);
            UV5y = H(value.y);
            UV5z = H(value.z);
            UV5w = H(value.w);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort H(float f)
    {
        int bits = BitConverter.SingleToInt32Bits(f);
        int sign = (bits >> 16) & 0x8000;
        int exp = ((bits >> 23) & 0xFF) - 127 + 15;
        int mantissa = bits & 0x7FFFFF;

        if (exp <= 0)
        {
            return (ushort)sign; // underflow → ±0
        }

        if (exp >= 31)
        {
            return (ushort)(sign | 0x7C00); // overflow → ±Inf
        }

        return (ushort)(sign | (exp << 10) | (mantissa >> 13));
    }
}
