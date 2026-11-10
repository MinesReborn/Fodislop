#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Game.Player.Stats;

internal static class PlayerStatsChangeDetection
{

    internal static bool AreContentsEqual(long[] left, long[] right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool AreStatusEntriesEqual(
        StatusLineEntry existing,
        string[] text,
        Color color,
        byte blinkRate,
        long expiry)
    {
        if (existing.Color != color || existing.BlinkRate != blinkRate || existing.Expiry != expiry ||
            existing.Text.Length != text.Length)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (!string.Equals(existing.Text[i], text[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

}
