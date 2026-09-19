#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI.HUD.Player.Model;
public sealed class PlayerStatsModel : IPlayerStats
{
    private readonly Dictionary<string, StatusLineEntry> _statusLines = new();

    public event Action? OnStatusLinesChanged;

    public IReadOnlyDictionary<string, StatusLineEntry> StatusLines => _statusLines;

    public void AddStatusLine(string tag, string[] text, Color color, byte blinkRate, long expiry)
    {
        tag ??= string.Empty;
        text ??= Array.Empty<string>();
        if (_statusLines.TryGetValue(tag, out StatusLineEntry existing) &&
            AreStatusEntriesEqual(existing, text, color, blinkRate, expiry))
        {
            return;
        }

        _statusLines[tag] = new StatusLineEntry((string[])text.Clone(), color, blinkRate, expiry);
        OnStatusLinesChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void RemoveStatusLine(string tag)
    {
        if (_statusLines.Remove(tag))
        {
            OnStatusLinesChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }
    }

    public void ClearStatusLines()
    {
        if (_statusLines.Count == 0)
        {
            return;
        }

        _statusLines.Clear();
        OnStatusLinesChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public bool IsReady => MaxHealth > 0 && BasketCapacity > 0 && !string.IsNullOrEmpty(Nickname) && Level > 0;
    public string Nickname { get; private set; } = string.Empty;
    public long Level { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
    public long Money { get; private set; }
    public long Creds { get; private set; }
    public int GeologyCurrent { get; private set; }
    public int GeologyMax { get; private set; }
    public string GeologyText { get; private set; } = string.Empty;
    public uint BasketCapacity { get; private set; }
    public long[] BasketContents { get; private set; } = Array.Empty<long>();
    public int BasketMaxPercent { get; private set; }
    public int OnlinePlayers { get; private set; }
    public int OnlineProgrammator { get; private set; }
    public int ClanID { get; private set; }
    public int MaxDepth { get; private set; }
    public int CurrentDepth { get; private set; }

    public bool IsMissionActive { get; private set; }
    public string MissionTitle { get; private set; } = string.Empty;
    public string MissionDescription { get; private set; } = string.Empty;
    public long MissionProgress { get; private set; }
    public long MissionMaxProgress { get; private set; }
    public ushort? MissionArrowX { get; private set; }
    public ushort? MissionArrowY { get; private set; }

    public event Action? OnStatsChanged;
    public event Action? OnHealthChanged;
    public event Action? OnCurrencyChanged;
    public event Action? OnGeologyChanged;
    public event Action? OnLevelChanged;
    public event Action? OnNicknameChanged;
    public event Action? OnBasketChanged;
    public event Action<SkillType, long, long>? OnSkillProgress;
    public event Action? OnDailyBonusChanged;
    public event Action? OnMissionChanged;
    public event Action? OnMissionArrowChanged;

    public bool DailyBonusAvailable { get; private set; }

    public void SetDailyBonusAvailable(bool available)
    {
        if (DailyBonusAvailable == available)
        {
            return;
        }

        DailyBonusAvailable = available;
        OnDailyBonusChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetNickname(string nickname)
    {
        nickname ??= string.Empty;
        if (string.Equals(Nickname, nickname, StringComparison.Ordinal))
        {
            return;
        }

        Nickname = nickname;
        OnNicknameChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetLevel(long level)
    {
        if (Level == level)
        {
            return;
        }

        Level = level;
        OnLevelChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetHealth(int current, int max)
    {
        if (Health == current && MaxHealth == max)
        {
            return;
        }

        Health = current;
        MaxHealth = max;
        OnHealthChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetCurrency(long money, long creds)
    {
        if (Money == money && Creds == creds)
        {
            return;
        }

        Money = money;
        Creds = creds;
        OnCurrencyChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetGeology(int current, int max, CellType cell, string text)
    {
        text ??= string.Empty;
        if (GeologyCurrent == current && GeologyMax == max &&
            string.Equals(GeologyText, text, StringComparison.Ordinal))
        {
            return;
        }

        GeologyCurrent = current;
        GeologyMax = max;
        GeologyText = text;
        OnGeologyChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetBasket(uint capacity, long[] contents)
    {
        if (contents == null)
        {
            throw new ArgumentNullException(nameof(contents), "[PlayerStatsModel] Basket contents from server are null");
        }

        if (BasketCapacity == capacity && AreContentsEqual(BasketContents, contents))
        {
            return;
        }

        BasketCapacity = capacity;
        // Packet payload arrays are owned by the networking layer. Keep an
        // immutable snapshot so a reused/deserialized buffer cannot mutate
        // the model without raising OnBasketChanged.
        BasketContents = (long[])contents.Clone();
        int maxPct = 0;
        for (int i = 0; i < BasketContents.Length; i++)
        {
            int pct = capacity > 0 ? (int)(BasketContents[i] * 100 / capacity) : 0;
            if (pct > maxPct)
            {
                maxPct = pct;
            }
        }

        BasketMaxPercent = Mathf.Clamp(maxPct, 0, 100);
        OnBasketChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetSkillProgress(SkillType skill, long current, long max)
    {
        OnSkillProgress?.Invoke(skill, current, max);
    }

    public void SetOnline(int players, int programmator)
    {
        if (OnlinePlayers == players && OnlineProgrammator == programmator)
        {
            return;
        }

        OnlinePlayers = players;
        OnlineProgrammator = programmator;
        OnStatsChanged?.Invoke();
    }

    public void SetClanID(int clanID)
    {
        if (ClanID == clanID)
        {
            return;
        }

        ClanID = clanID;
        OnStatsChanged?.Invoke();
    }

    public void SetMaxDepth(int depth)
    {
        if (MaxDepth == depth)
        {
            return;
        }

        MaxDepth = depth;
        OnStatsChanged?.Invoke();
    }
    public void SetMission(string title, string description, long max)
    {
        title ??= string.Empty;
        description ??= string.Empty;
        if (IsMissionActive && string.Equals(MissionTitle, title, StringComparison.Ordinal) &&
            string.Equals(MissionDescription, description, StringComparison.Ordinal) &&
            MissionMaxProgress == max && MissionProgress == 0)
        {
            return;
        }

        IsMissionActive = true;
        MissionTitle = title;
        MissionDescription = description;
        MissionProgress = 0;
        MissionMaxProgress = max;
        OnMissionChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetMissionProgress(long current)
    {
        if (MissionProgress == current)
        {
            return;
        }

        MissionProgress = current;
        OnMissionChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetMissionMaxProgress(long max)
    {
        if (MissionMaxProgress == max)
        {
            return;
        }

        MissionMaxProgress = max;
        OnMissionChanged?.Invoke();
        OnStatsChanged?.Invoke();
    }

    public void SetMissionArrow(ushort x, ushort y)
    {
        if (MissionArrowX == x && MissionArrowY == y)
        {
            return;
        }

        MissionArrowX = x;
        MissionArrowY = y;
        OnMissionArrowChanged?.Invoke();
    }

    public void ClearMission()
    {
        if (!IsMissionActive && string.IsNullOrEmpty(MissionTitle) &&
            string.IsNullOrEmpty(MissionDescription) && MissionProgress == 0 &&
            MissionMaxProgress == 0 && !MissionArrowX.HasValue && !MissionArrowY.HasValue)
        {
            return;
        }

        bool hadArrow = MissionArrowX.HasValue || MissionArrowY.HasValue;
        IsMissionActive = false;
        MissionTitle = string.Empty;
        MissionDescription = string.Empty;
        MissionProgress = 0;
        MissionMaxProgress = 0;
        MissionArrowX = null;
        MissionArrowY = null;
        OnMissionChanged?.Invoke();
        if (hadArrow)
        {
            OnMissionArrowChanged?.Invoke();
        }

        OnStatsChanged?.Invoke();
    }

    private static bool AreContentsEqual(long[] left, long[] right)
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

    private static bool AreStatusEntriesEqual(
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
