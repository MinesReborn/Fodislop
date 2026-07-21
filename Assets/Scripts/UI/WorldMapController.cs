using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.UI
{
    public class WorldMapController : MonoBehaviour
    {
        private CameraFollow _cameraFollow;
        private PlayerMovementController _player;
        private SingleMeshTerrainRenderer _terrain;
        private WorldMapRenderer _mapRenderer;
        private InputAction _mapToggleAction;

        private bool _isInMapMode;
        private Vector3 _storedCamPos;
        private float _storedCamZoom;

        // HUD elements
        private PlayerHUD _playerHud;
        private InventoryUI _inventory;
        private FPSCounter _fps;
        private MinimapController _minimap;
        private PauseMenu _pauseMenu;

        protected void Start()
        {
            _cameraFollow = FindAnyObjectByType<CameraFollow>();
            _player = FindAnyObjectByType<PlayerMovementController>();
            _terrain = FindAnyObjectByType<SingleMeshTerrainRenderer>();
            _playerHud = FindAnyObjectByType<PlayerHUD>();
            _inventory = FindAnyObjectByType<InventoryUI>();
            _fps = FindAnyObjectByType<FPSCounter>();
            _minimap = FindAnyObjectByType<MinimapController>();
            _pauseMenu = FindAnyObjectByType<PauseMenu>();

            if (_cameraFollow == null)
            {
                Debug.LogError("[WorldMapController] CameraFollow not found");
                enabled = false;
                return;
            }

            _mapToggleAction = new InputAction("MapToggle", binding: "<Keyboard>/m");
            _mapToggleAction.performed += _ => ToggleMapMode();
            _mapToggleAction.Enable();
        }

        protected void OnDestroy()
        {
            _mapToggleAction?.Dispose();
        }

        private void ToggleMapMode()
        {
            if (!enabled)
            {
                return;
            }

            if (MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                return;
            }

            if (_isInMapMode)
            {
                ExitMapMode();
            }
            else
            {
                EnterMapMode();
            }
        }

        private void EnterMapMode()
        {
            if (_isInMapMode)
            {
                return;
            }

            if (MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                return;
            }

            _isInMapMode = true;
            _cameraFollow.SetScrollEnabled(false);

            // Store camera state
            _storedCamPos = _cameraFollow.transform.position;
            _storedCamZoom = _cameraFollow.GetCurrentZoom();

            if (_terrain != null)
            {
                _terrain.enabled = false;
            }

            if (_mapRenderer == null)
            {
                var go = new GameObject("WorldMapRenderer");
                _mapRenderer = go.AddComponent<WorldMapRenderer>();
            }

            _mapRenderer.Show();

            SetHudVisible(false);

            // Center map on player position
            int centerX = _player != null ? _player.Position.x : MapManager.Instance.WorldWidth / 2;
            int centerY = _player != null ? _player.Position.y : MapManager.Instance.WorldHeight / 2;
            _mapRenderer.SetViewCenter(centerX, centerY);
        }

        private void ExitMapMode()
        {
            if (!_isInMapMode)
            {
                return;
            }

            _isInMapMode = false;
            _cameraFollow.SetScrollEnabled(true);

            if (_mapRenderer != null)
            {
                _mapRenderer.Hide();
            }

            if (_terrain != null)
            {
                _terrain.enabled = true;
            }

            SetHudVisible(true);
        }

        private void SetHudVisible(bool visible)
        {
            if (_playerHud != null)
            {
                _playerHud.enabled = visible;
            }

            if (_inventory != null)
            {
                _inventory.enabled = visible;
            }

            if (_fps != null)
            {
                _fps.enabled = visible;
            }

            if (_minimap != null)
            {
                _minimap.enabled = visible;
            }
        }
    }
}
