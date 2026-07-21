using System;
using Fodinae.Scripts.Core;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public enum GameState
    {
        Offline,
        Connecting,
        InGame,
        Disconnected
    }

    /// <summary>
    /// Единый менеджер жизненного цикла игры и сессии.
    ///
    /// Управляет высокими состояниями сессии и связывает событийно геймплейные подсистемы.
    /// </summary>
    public sealed class GameManager : SingletonMonoBehaviour<GameManager>
    {
        public GameState CurrentState { get; private set; } = GameState.Offline;

        public event Action<GameState> OnGameStateChanged;
        public event Action OnWorldLoaded;

        public void SetState(GameState newState)
        {
            if (CurrentState == newState)
            {
                return;
            }

            CurrentState = newState;
            Debug.Log($"[GameManager] Game state changed to: {newState}");
            OnGameStateChanged?.Invoke(newState);
        }

        public void NotifyWorldLoaded()
        {
            Debug.Log("[GameManager] World load completed, notifying listeners.");
            OnWorldLoaded?.Invoke();
        }
    }
}
