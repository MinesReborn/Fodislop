#nullable enable

using System;
using System.IO;

namespace Fodinae.World;

internal static class GifLzwDecoder
{
    public static byte[] Decompress(byte[] d, int m, int pc)
    {
        if (m < 2 || m > 8)
        {
            throw new InvalidDataException(
                $"GIF LZW minimum code size {m} is outside the supported range 2..8.");
        }

        if (pc <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pc),
                pc,
                "GIF frame pixel count must be positive.");
        }

        int cc = 1 << m;
        int eoi = cc + 1;
        int nc = cc + 2;
        int cs = m + 1;
        int cm = (1 << cs) - 1;
        int[] pref = new int[4096];
        byte[] suff = new byte[4096];
        byte[] ps = new byte[4097];

        for (int i = 0; i < cc; i++)
        {
            suff[i] = (byte)i;
        }

        byte[] o = new byte[pc];
        int op = 0;
        int bb = 0;
        int bc = 0;
        int dp = 0;
        int t = 0;
        int oc = -1;

        while (op < pc)
        {
            while (bc < cs && dp < d.Length)
            {
                bb |= d[dp++] << bc;
                bc += 8;
            }

            if (bc < cs)
            {
                break;
            }

            int c = bb & cm;
            bb >>= cs;
            bc -= cs;

            if (c == cc)
            {
                cs = m + 1;
                cm = (1 << cs) - 1;
                nc = cc + 2;
                oc = -1;
                continue;
            }

            if (c == eoi)
            {
                break;
            }

            if (oc == -1)
            {
                if (c >= cc)
                {
                    throw new InvalidDataException(
                        $"GIF LZW stream starts with invalid code {c}.");
                }

                o[op++] = suff[c];
                oc = c;
                continue;
            }

            int cur = c;
            if (c > nc)
            {
                throw new InvalidDataException(
                    $"GIF LZW code {c} exceeds the next dictionary index {nc}.");
            }

            if (c == nc)
            {
                if (t >= ps.Length)
                {
                    throw new InvalidDataException(
                        "GIF LZW expansion stack overflowed.");
                }

                ps[t++] = (byte)LzwFirst(oc, cc, pref, suff);
                cur = oc;
            }

            while (cur >= cc)
            {
                if (cur >= nc || t >= ps.Length)
                {
                    throw new InvalidDataException(
                        "GIF LZW dictionary chain is corrupt.");
                }

                ps[t++] = suff[cur];
                cur = pref[cur];
            }

            if (cur < 0 || cur >= cc || t >= ps.Length)
            {
                throw new InvalidDataException(
                    "GIF LZW dictionary resolved to an invalid root code.");
            }

            ps[t++] = suff[cur];
            byte f = ps[t - 1];
            while (t > 0)
            {
                if (op >= o.Length)
                {
                    throw new InvalidDataException(
                        "GIF LZW stream expands past the declared frame size.");
                }

                o[op++] = ps[--t];
            }

            if (nc < 4096)
            {
                pref[nc] = oc;
                suff[nc] = f;
                nc++;
                if (nc == (1 << cs) && cs < 12)
                {
                    cs++;
                    cm = (1 << cs) - 1;
                }
            }

            oc = c;
        }

        if (op != pc)
        {
            throw new InvalidDataException(
                $"GIF LZW stream produced {op} pixels; expected {pc}.");
        }

        return o;
    }

    private static int LzwFirst(int c, int cc, int[] pref, byte[] suff)
    {
        int steps = 0;
        while (c >= cc)
        {
            if (c < 0 || c >= pref.Length || steps++ >= pref.Length)
            {
                throw new InvalidDataException(
                    "GIF LZW dictionary contains a cyclic or invalid prefix chain.");
            }

            c = pref[c];
        }

        if (c < 0 || c >= suff.Length)
        {
            throw new InvalidDataException(
                "GIF LZW dictionary resolved outside the suffix table.");
        }

        return suff[c];
    }
}
