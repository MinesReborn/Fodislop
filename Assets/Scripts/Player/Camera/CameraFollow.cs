#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.Player.Logic;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Kern.Player
{
    [ExecuteAlways]
    public class CameraFollow : MonoBehaviour
    {
        [Header("Follow Settings")]
        public const float DefaultOrthographicSize = 7f;
        public const float DefaultCameraDepthZ = -10f;
        [SerializeField]
        private Transform? _target;
        [SerializeField]
        private float _smoothSpeed = 5f;
        [SerializeField]
        private Vector2 _offset = Vector2.zero;

        [Header("Zoom Settings")]
        [SerializeField]
        private float _zoomSpeed = 300f;
        [SerializeField]
        private float _minZoom = 5f;
        [SerializeField]
        private float _maxZoom = 30f;
        [SerializeField]
        private float _zoomSmoothness = 8f;

        private const float ZoomSettleEpsilon = 0.001f;

        // Сериализованные пределы не выходят за контракт: освещение рассчитано
        // на кадр ProjectRuntimeContracts.Camera.MaximumOrthographicSize.
        private float MinimumZoom => Mathf.Max(_minZoom, ProjectRuntimeContracts.Camera.MinimumOrthographicSize);

        private float MaximumZoom => Mathf.Clamp(_maxZoom, MinimumZoom, ProjectRuntimeContracts.Camera.MaximumOrthographicSize);
        private const float FollowSettleEpsilonSquared = 0.000001f;

        private float _originalZ;
        private Camera _camera = null!;
        private ILocalPlayer? _subscribedPlayer;
        private float _targetZoom;
        private float _currentZoom;
        private float _lastZoom;
        public event Action<float>? OnZoomChanged;
        private InputAction? _scrollAction;
        private bool _scrollEnabled = true;
        private bool _cameraNullLogged;
        private bool _scrollNullLogged;
        private bool _hasSnappedToServerPosition;
        private bool _localPlayerSpawnSubscription;
        private Vector3 _followVelocity;
        [Inject]
        private Camera _injectedCamera = null!;
        [Inject]
        private IInputBlocker _inputBlocker = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;

        [Inject]
        private IClientConfigManager? _clientConfig;

        private CameraPixelGridAligner? _aligner;

        protected void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            InitializeRuntime();
        }

        private void InitializeRuntime()
        {
            _camera = _injectedCamera;

            _originalZ = _camera.transform.position.z;
            float initialZoom = (MinimumZoom + MaximumZoom) * 0.5f;
            _targetZoom = initialZoom;
            _currentZoom = _targetZoom;
            _lastZoom = _currentZoom;
            ApplyZoom(_currentZoom);
            if (_target == null || _target == _camera.transform)
            {
                var player = _localPlayer?.Current;
                if (player != null)
                {
                    _target = player.transform;
                }
                else
                {
                    Debug.LogWarning("[CameraFollow] No target assigned and no ILocalPlayer found!");
                }
            }

            ILocalPlayer? localPlayer = _localPlayer?.Current;
            if (localPlayer != null)
            {
                SubscribeToPlayer(localPlayer);
            }

            if (!_localPlayerSpawnSubscription)
            {
                if (_localPlayer != null)
                {
                    _localPlayer.Changed += HandleLocalPlayerChanged;
                }
                _localPlayerSpawnSubscription = true;
            }

            SnapToTarget();
            InitializeInput();
        }

        protected void OnEnable()
        {
            if (_scrollAction == null)
            {
                InitializeInput();
            }
            else
            {
                _scrollAction.Enable();
            }
        }

        private void InitializeInput()
        {
            _scrollAction = InputSystem.actions?.FindAction(
                "UI/ScrollWheel",
                throwIfNotFound: false);
            _scrollAction?.Enable();
        }

        protected void OnDestroy()
        {
            if (_subscribedPlayer != null)
            {
                _subscribedPlayer.OnPlayerMoved -= HandlePlayerMoved;
                _subscribedPlayer = null;
            }

            if (_localPlayerSpawnSubscription)
            {
                if (_localPlayer != null)
                {
                    _localPlayer.Changed -= HandleLocalPlayerChanged;
                }

                _localPlayerSpawnSubscription = false;
            }

            DisposeScrollAction();
        }

        protected void OnDisable()
        {
            DisposeScrollAction();
        }

        private void DisposeScrollAction()
        {
            _scrollAction?.Disable();
            _scrollAction = null;
        }

        private void HandlePlayerMoved(Vector2Int oldPosition, Vector2Int newPosition)
        {
            if (_hasSnappedToServerPosition || oldPosition == newPosition)
            {
                return;
            }

            _hasSnappedToServerPosition = true;
            SnapToTarget();
        }

        private void HandleLocalPlayerChanged(ILocalPlayer? player)
        {
            if (player == null)
            {
                return;
            }

            if (_target == null || _target == transform)
            {
                _target = player.transform;
            }

            SubscribeToPlayer(player);

            SnapToTarget();
        }

        private void SubscribeToPlayer(ILocalPlayer player)
        {
            if (ReferenceEquals(_subscribedPlayer, player))
            {
                return;
            }

            if (_subscribedPlayer != null)
            {
                _subscribedPlayer.OnPlayerMoved -= HandlePlayerMoved;
            }

            _subscribedPlayer = player;
            _subscribedPlayer.OnPlayerMoved -= HandlePlayerMoved;
            _subscribedPlayer.OnPlayerMoved += HandlePlayerMoved;
        }

        protected void LateUpdate()
        {
            // Камера принадлежит Bootstrap и может быть уничтожена раньше этой
            // сцены: порядок разрушения сцен при выходе и в тестах не гарантирован.
            if (_camera == null)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                ApplyZoom(DefaultOrthographicSize);

                var player = _localPlayer?.Current;
                if (player != null)
                {
                    _camera.transform.position = SnapToPixelGrid(new Vector3(
                        player.transform.position.x,
                        player.transform.position.y,
                        DefaultCameraDepthZ));
                }

                return;
            }

            HandleZoom();
            HandleFollow();
        }

        private void HandleZoom()
        {
            if (_inputBlocker != null && _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (!_scrollEnabled)
            {
                return;
            }

            float scrollInput = 0f;
            if (_scrollAction != null && _scrollAction.enabled)
            {
                scrollInput = _scrollAction.ReadValue<Vector2>().y;
            }
            else if (Mouse.current != null)
            {
                scrollInput = Mouse.current.scroll.ReadValue().y;
            }
            else
            {
                if (!_scrollNullLogged)
                {
                    _scrollNullLogged = true;
                    Debug.LogWarning("[CameraFollow] Scroll action is unavailable; mouse-wheel zoom is disabled.");
                }

                return;
            }

            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                _targetZoom -= scrollInput * _zoomSpeed * Time.deltaTime;
                _targetZoom = Mathf.Clamp(_targetZoom, MinimumZoom, MaximumZoom);
            }

            float nextZoom = Mathf.Lerp(
                _currentZoom,
                _targetZoom,
                _zoomSmoothness * Time.deltaTime);
            if (Mathf.Abs(nextZoom - _targetZoom) <= ZoomSettleEpsilon)
            {
                nextZoom = _targetZoom;
            }

            _currentZoom = nextZoom;
            ApplyZoom(_currentZoom);

            if (Mathf.Abs(_currentZoom - _lastZoom) > 0.01f)
            {
                _lastZoom = _currentZoom;
                OnZoomChanged?.Invoke(_currentZoom);
            }
        }

        private void HandleFollow()
        {
            if (_localPlayer?.Current is { HasServerPosition: false })
            {
                return;
            }

            Transform cameraTransform = _camera.transform;
            if (_target == null || _target == cameraTransform)
            {
                if (_localPlayer?.Current != null)
                {
                    _target = _localPlayer.Current.transform;
                }
                else
                {
                    return;
                }
            }

            Vector3 targetPosition = _target.position + new Vector3(_offset.x, _offset.y, 0f);
            Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, _originalZ);

            // SmoothDamp is frame-rate independent — unlike Lerp(dt), it handles variable dt
            // without introducing jitter during frame spikes (e.g. terrain mesh rebuilds).
            // smoothTime ≈ 1 / _smoothSpeed gives equivalent response to the old Lerp, but we
            // tune it a touch snappier to reduce swimmy lag at high movement speeds.
            float smoothTime = 1f / Mathf.Max(_smoothSpeed, 0.001f);
            if ((cameraTransform.position - desiredPosition).sqrMagnitude <= FollowSettleEpsilonSquared &&
                _followVelocity.sqrMagnitude <= FollowSettleEpsilonSquared)
            {
                if (cameraTransform.position != desiredPosition)
                {
                    cameraTransform.position = desiredPosition;
                }

                _followVelocity = Vector3.zero;
                return;
            }

            Vector3 smoothed = Vector3.SmoothDamp(
                cameraTransform.position,
                desiredPosition,
                ref _followVelocity,
                smoothTime,
                float.PositiveInfinity,
                Time.deltaTime);
            cameraTransform.position = SnapToPixelGrid(smoothed);
        }

        private void ApplyZoom(float desiredSize)
        {
            float size = _Aligner.ResolveOrthographicSize(desiredSize, _minZoom, _maxZoom);
            if (!Mathf.Approximately(_camera.orthographicSize, size))
            {
                _camera.orthographicSize = size;
            }
        }

        private Vector3 SnapToPixelGrid(Vector3 position) =>
            _Aligner.SnapPosition(position, _camera.orthographicSize);

        private CameraPixelGridAligner _Aligner =>
            _aligner ??= new CameraPixelGridAligner(_clientConfig);


        public void SnapToTarget()
        {
            if (_localPlayer?.Current is not { HasServerPosition: true })
            {
                return;
            }

            Transform cameraTransform = _camera.transform;
            if (_target == null || _target == cameraTransform)
            {
                if (_localPlayer?.Current != null)
                {
                    _target = _localPlayer.Current.transform;
                }
            }

            if (_target != null && _target != cameraTransform)
            {
                Vector3 targetPosition = _target.position + new Vector3(_offset.x, _offset.y, 0f);
                cameraTransform.position = SnapToPixelGrid(
                    new Vector3(targetPosition.x, targetPosition.y, _originalZ));
                _followVelocity = Vector3.zero;
            }
        }
        public void SetScrollEnabled(bool enabled) => _scrollEnabled = enabled;
#if UNITY_EDITOR
        protected void OnDrawGizmosSelected()
        {
            if (_target != null)
            {
                // Draw line to target
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, _target.position);

                // Draw target marker
                Gizmos.DrawWireSphere(_target.position, 0.5f);

                Kern.World.KernGizmos.DrawLabel(_target.position + (Vector3.up * 0.7f), "Camera Target", Color.yellow);
            }

            // Draw current viewport visualization
            if (_camera != null && _camera.orthographic)
            {
                float height = _camera.orthographicSize * 2;
                float width = height * _camera.aspect;
                Gizmos.color = new Color(0, 1, 0, 0.3f);
                Gizmos.DrawWireCube(transform.position, new Vector3(width, height, 0));
            }
        }
#endif
    }
}
