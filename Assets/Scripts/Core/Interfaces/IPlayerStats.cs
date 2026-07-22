using System;
using MinesServer.Data;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IPlayerStats
    {
        int Health { get; }
        int MaxHealth { get; }
        string Nickname { get; }
        void SetLevel(long level);
        void SetHealth(int current, int max);
        void SetCurrency(long money, long creds);
        void SetGeology(int current, int max, CellType cell, string text);
        void SetNickname(string nickname);
        void SetClanId(int clanId);
        void SetMaxDepth(int depth);
        void SetDailyBonusAvailable(bool available);
        void SetSkillProgress(SkillType skill, long current, long max);
        void SetMission(string title, string description, long max);
        void SetMissionProgress(long current);
        void SetMissionMaxProgress(long max);
        void ClearMission();
        void SetBasket(uint capacity, long[] contents);
        void AddStatusLine(string tag, string[] text, UnityEngine.Color color, byte blinkRate, long expiry);
        void RemoveStatusLine(string tag);
        void ClearStatusLines();
        void SetOnline(int players, int programmator);
        event Action OnStatsChanged;
        event Action OnHealthChanged;
    }
}
