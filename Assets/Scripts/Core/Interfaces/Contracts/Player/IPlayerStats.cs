#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Core.Interfaces;
using Kern.Core.Models;
public interface IPlayerStats
{
    bool IsReady { get; }
    int Health { get; }
    int MaxHealth { get; }
    float HealthPercent { get; }
    string Nickname { get; }
    long Level { get; }
    long Money { get; }
    long Creds { get; }
    int GeologyCurrent { get; }
    int GeologyMax { get; }
    string GeologyText { get; }
    uint BasketCapacity { get; }
    long[] BasketContents { get; }
    int BasketMaxPercent { get; }
    void SetBasket(uint capacity, long[] contents);
    IReadOnlyDictionary<string, StatusLineEntry> StatusLines { get; }
    int OnlinePlayers { get; }
    int OnlineProgrammator { get; }
    int ClanID { get; }
    int MaxDepth { get; }
    int CurrentDepth { get; }
    bool IsMissionActive { get; }
    string MissionTitle { get; }
    string MissionDescription { get; }
    long MissionProgress { get; }
    long MissionMaxProgress { get; }
    bool DailyBonusAvailable { get; }

    void SetLevel(long level);
    void SetHealth(int current, int max);
    void SetCurrency(long money, long creds);
    void SetGeology(int current, int max, CellType cell, string text);
    void SetNickname(string nickname);
    void SetClanID(int clanID);
    void SetMaxDepth(int depth);
    void SetDailyBonusAvailable(bool available);
    void SetSkillProgress(SkillType skill, long current, long max);
    void SetMission(string title, string description, long max);
    void SetMissionArrow(ushort x, ushort y);
    void SetMissionProgress(long current);
    void SetMissionMaxProgress(long max);
    void ClearMission();
    void SetOnline(int players, int programmator);
    void AddStatusLine(string tag, string[] text, Color color, byte blinkRate, long expiry);
    void RemoveStatusLine(string tag);
    void ClearStatusLines();
    event Action OnStatsChanged;
    event Action OnHealthChanged;
    event Action OnCurrencyChanged;
    event Action OnGeologyChanged;
    event Action OnLevelChanged;
    event Action OnNicknameChanged;
    event Action OnBasketChanged;
    event Action<SkillType, long, long> OnSkillProgress;
    event Action OnDailyBonusChanged;
    event Action OnMissionChanged;
    event Action OnStatusLinesChanged;
}
