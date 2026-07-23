using System;
using System.Collections.Generic;
using Fodinae.Scripts.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.UI.HUD.Player.Model
{
    public readonly record struct StatusLineEntry(string[] Text, Color Color, byte BlinkRate, long Expiry);
    public class PlayerStatsModel : MonoBehaviour, IPlayerStats
    {
        private static PlayerStatsModel _instance;
        public static PlayerStatsModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<PlayerStatsModel>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[PlayerStatsModel]");
                        _instance = go.AddComponent<PlayerStatsModel>();
                        DontDestroyOnLoad(go);
                    }
                }

                return _instance;
            }
        }

        private readonly Dictionary<string, StatusLineEntry> _statusLines = new();

        public event Action OnStatusLinesChanged;

        public IReadOnlyDictionary<string, StatusLineEntry> StatusLines => _statusLines;

        public void AddStatusLine(string tag, string[] text, Color color, byte blinkRate, long expiry)
        {
            _statusLines[tag] = new StatusLineEntry(text, color, blinkRate, expiry);
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

        protected void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public string Nickname { get; private set; }
        public long Level { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
        public long Money { get; private set; }
        public long Creds { get; private set; }
        public int GeologyCurrent { get; private set; }
        public int GeologyMax { get; private set; }
        public string GeologyText { get; private set; }
        public uint BasketCapacity { get; private set; }
        public long[] BasketContents { get; private set; } = Array.Empty<long>();
        public int BasketMaxPercent { get; private set; }
        public int OnlinePlayers { get; private set; }
        public int OnlineProgrammator { get; private set; }
        public int ClanId { get; private set; }
        public int MaxDepth { get; private set; }
        public int CurrentDepth { get; private set; }

        public bool IsMissionActive { get; private set; }
        public string MissionTitle { get; private set; }
        public string MissionDescription { get; private set; }
        public long MissionProgress { get; private set; }
        public long MissionMaxProgress { get; private set; }

        public event Action OnStatsChanged;
        public event Action OnHealthChanged;
        public event Action OnCurrencyChanged;
        public event Action OnGeologyChanged;
        public event Action OnLevelChanged;
        public event Action OnNicknameChanged;
        public event Action OnBasketChanged;
        public event Action<SkillType, long, long> OnSkillProgress;
        public event Action OnDailyBonusChanged;
        public event Action OnMissionChanged;

        public bool DailyBonusAvailable { get; private set; }

        public void SetDailyBonusAvailable(bool available)
        {
            DailyBonusAvailable = available;
            OnDailyBonusChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetNickname(string nickname)
        {
            Nickname = nickname;
            OnNicknameChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetLevel(long level)
        {
            Level = level;
            OnLevelChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetHealth(int current, int max)
        {
            Health = current;
            MaxHealth = max;
            OnHealthChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetCurrency(long money, long creds)
        {
            Money = money;
            Creds = creds;
            OnCurrencyChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetGeology(int current, int max, CellType cell, string text)
        {
            GeologyCurrent = current;
            GeologyMax = max;
            GeologyText = text;
            OnGeologyChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetBasket(uint capacity, long[] contents)
        {
            BasketCapacity = capacity;
            BasketContents = contents ?? Array.Empty<long>();
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
            OnlinePlayers = players;
            OnlineProgrammator = programmator;
            OnStatsChanged?.Invoke();
        }

        public void SetClanId(int clanId)
        {
            ClanId = clanId;
            OnStatsChanged?.Invoke();
        }

        public void SetMaxDepth(int depth)
        {
            MaxDepth = depth;
            OnStatsChanged?.Invoke();
        }

        public void SetCurrentDepth(int serverY)
        {
            CurrentDepth = serverY;
            OnStatsChanged?.Invoke();
        }

        public void SetMission(string title, string description, long max)
        {
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
            MissionProgress = current;
            OnMissionChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void SetMissionMaxProgress(long max)
        {
            MissionMaxProgress = max;
            OnMissionChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }

        public void ClearMission()
        {
            IsMissionActive = false;
            MissionTitle = null;
            MissionDescription = null;
            MissionProgress = 0;
            MissionMaxProgress = 0;
            OnMissionChanged?.Invoke();
            OnStatsChanged?.Invoke();
        }
    }
}
