#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.Player;
using Kern.Player.Logic;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Kern.UI
{
    public class WorldMapController : MonoBehaviour
    {
        [Inject]
        private CameraFollow _cameraFollow = null!;
        [Inject]
        private Kern.UI.HUD.Player.View.PlayerHUDView _playerHud = null!;
        [Inject]
        private Kern.UI.Inventory.InventoryView _inventory = null!;
        [Inject]
        private FPSCounter _fps = null!;
        [Inject]
        private WorldMapRenderer _mapRenderer = null!;
        [Inject]
        private MapStorage _mapStorage = null!;

        private ILocalPlayer? _player;

        private bool _isInMapMode;
        private bool _playerSpawnSubscription;
        [Inject]
        private MapModeState _mapModeState = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private UIInputManager _uiInput = null!;

        protected void Start()
        {
            _mapModeState.Changed += OnMapModeChanged;
            _player = _localPlayer.Current;
            if (_player == null)
            {
                _localPlayer.Changed += OnLocalPlayerChanged;
                _playerSpawnSubscription = true;
            }
        }

        protected void Update()
        {
            if (_isInMapMode && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _mapModeState.SetOpen(false);
                return;
            }

            // Map toggle as a direct keyboard check (mirrors MinimapController's N key);
            // Ignore when typing in chat.
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame && !_uiInput.IsChatFocused)
            {
                ToggleMapMode();
            }
        }

        protected void OnDestroy()
        {
            _mapModeState.Changed -= OnMapModeChanged;

            UnsubscribeFromPlayerSpawn();
        }

        protected void OnDisable()
        {
            if (_isInMapMode)
            {
                ExitMapMode();
                _mapModeState.SetOpen(false);
            }

            UnsubscribeFromPlayerSpawn();
        }

        private void OnLocalPlayerChanged(ILocalPlayer? player)
        {
            UnsubscribeFromPlayerSpawn();
            if (player == null)
            {
                return;
            }

            _player = player;
        }

        private void UnsubscribeFromPlayerSpawn()
        {
            if (!_playerSpawnSubscription)
            {
                return;
            }

            _localPlayer.Changed -= OnLocalPlayerChanged;
            _playerSpawnSubscription = false;
        }

        public void ToggleMapMode()
        {
            if (!enabled)
            {
                return;
            }

            _mapModeState.SetOpen(!_mapModeState.IsOpen);
        }

        private void OnMapModeChanged(bool open)
        {
            if (open && !_isInMapMode)
            {
                EnterMapMode();
            }
            else if (!open && _isInMapMode)
            {
                ExitMapMode();
            }
        }

        private void EnterMapMode()
        {
            if (_isInMapMode)
            {
                return;
            }

            ILocalPlayer? player = _player ?? _localPlayer.Current;
            if (player == null || !player.HasServerPosition || !_mapStorage.IsReady)
            {
                _mapModeState.SetOpen(false);
                return;
            }

            _player = player;

            _isInMapMode = true;
            _cameraFollow.SetScrollEnabled(false);

            _mapRenderer.Show();

            SetHudVisible(false);

            _mapRenderer.SetViewCenter(player.Position.x, player.Position.y);
        }

        private void ExitMapMode()
        {
            if (!_isInMapMode)
            {
                return;
            }

            _isInMapMode = false;
            _cameraFollow.SetScrollEnabled(true);
            _mapRenderer.Hide();

            SetHudVisible(true);
        }

        private void SetHudVisible(bool visible)
        {
            _playerHud.enabled = visible;
            _inventory.enabled = visible;
            _fps.enabled = visible;

        }
    }
}
