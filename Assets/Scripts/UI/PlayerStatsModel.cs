using System;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.UI
{
    public class PlayerStatsModel : MonoBehaviour
    {
        private static PlayerStatsModel _instance;
        public static PlayerStatsModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<PlayerStatsModel>();
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

        private void Awake()
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

        public event Action OnStatsChanged;
        public event Action OnHealthChanged;
        public event Action OnCurrencyChanged;
        public event Action OnGeologyChanged;
        public event Action OnLevelChanged;
        public event Action OnNicknameChanged;
        public event Action OnBasketChanged;
        public event Action<SkillType, long, long> OnSkillProgress;

        public void SetNickname(string nickname)
        {
            Nickname = nickname;
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
                if (pct > maxPct) maxPct = pct;
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
    }
}
