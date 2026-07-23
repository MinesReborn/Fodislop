using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    /// <summary>
    /// Высокоуровневые состояния игрового сеанса.
    /// Расширяют сетевой статус <see cref="MinesServer.Networking.Shared.ConnectionStatus"/>,
    /// разделяя состояния оффлайн режима, подключения, геймплея и дисконнекта.
    /// </summary>
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
        public bool IsUIAuthorized { get; private set; }

        public event Action<GameState> OnGameStateChanged;
        public event Action OnWorldLoaded;

        private GameObject _uiRoot;

        protected override void OnAwake()
        {
            SetupUI();
        }

        protected override void OnDestroyed()
        {
            if (_uiRoot != null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
            }
        }

        private void SetupUI()
        {
            _uiRoot = new GameObject("UIRoot");
            _uiRoot.SetActive(false);
            _uiRoot.transform.SetParent(transform);

            var fpsGO = new GameObject("FPSCounter");
            fpsGO.AddComponent<FPSCounter>();
            fpsGO.transform.SetParent(transform);

            var mmGO = new GameObject("MinimapRoot");
            mmGO.AddComponent<MinimapController>();
            mmGO.transform.SetParent(_uiRoot.transform);

            var invGO = new GameObject("InventoryRoot");
            invGO.AddComponent<Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView>();
            invGO.AddComponent<Fodinae.Scripts.UI.HUD.Inventory.Presenter.InventoryPresenter>();
            invGO.transform.SetParent(_uiRoot.transform);

            var hudGO = new GameObject("PlayerHUD");
            hudGO.AddComponent<PlayerStatsModel>();
            hudGO.AddComponent<Fodinae.Scripts.UI.HUD.Player.View.PlayerHUDView>();
            hudGO.AddComponent<Fodinae.Scripts.UI.HUD.Player.Presenter.PlayerHUDPresenter>();
            hudGO.transform.SetParent(_uiRoot.transform);

            var pauseGO = new GameObject("PauseMenu");
            pauseGO.AddComponent<PauseMenu>();
            pauseGO.transform.SetParent(_uiRoot.transform);

            var chatGO = new GameObject("ChatSystem");
            chatGO.AddComponent<LocalChatPopup>();
            chatGO.AddComponent<GlobalChatUI>();
            chatGO.AddComponent<FloatingChatManager>();
            chatGO.transform.SetParent(_uiRoot.transform);
        }

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

        public void AuthorizeUI()
        {
            IsUIAuthorized = true;
            if (_uiRoot != null)
            {
                _uiRoot.SetActive(true);
            }

            Debug.Log("[GameManager] UI authorized");
        }

        public void DeauthorizeUI()
        {
            IsUIAuthorized = false;
            if (_uiRoot != null)
            {
                _uiRoot.SetActive(false);
            }

            Debug.Log("[GameManager] UI deauthorized");
        }
    }
}
