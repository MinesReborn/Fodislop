using System;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Assets.Scripts.UI
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

        public event Action OnStatsChanged;
        public event Action OnHealthChanged;
        public event Action OnCurrencyChanged;
        public event Action OnGeologyChanged;
        public event Action OnLevelChanged;
        public event Action OnNicknameChanged;

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
    }
}
