#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Networking;
using UnityEngine;
using VContainer;

using Kern.Game.Entities.Robots;
using Kern.Game.Player.Input;

using Kern.Diagnostics.World;

namespace Kern.Game.Player.Cameras
{
    [ExecuteAlways]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Player", sourceAssembly: null, sourceClassName: "CameraFollow")]
    public class CameraFollow : MonoBehaviour
    {
        [Header("Follow Settings")]
        public const float DefaultOrthographicSize = 7f;
        public const float DefaultCameraDepthZ = -10f;
        [SerializeField] private Transform? _target;

        // Цель камеры уже сглажена ботом (RobotMovement), поэтому своё
        // сглаживание камеры короткое: оно только гасит дискретные шаги, а не
        // добавляет второй хвост задержки поверх первого.
        [SerializeField] private float _followSmoothTime = 0.06f;
        [SerializeField] private Vector2 _offset = Vector2.zero;

        [Header("Zoom Settings")]
        // Доля масштаба за один щелчок колёсика. Прокрутка приходит уже
        // нормализованной (Input System, UniformAcrossAllPlatforms): щелчок
        // мыши — ±1, трекпад — плавные доли. Масштаб меняется в разы, а не на
        // единицы: одинаковый жест одинаково ощущается на любом отдалении.
        [SerializeField] private float _zoomStepPerNotch = 0.12f;
        [SerializeField] private float _minZoom = 3f;
        [SerializeField] private float _maxZoom = 30f;

        // Скорость, с которой зум догоняет цель, в 1/с; форма 1 − e^(−k·dt)
        // не зависит от частоты кадров.
        [SerializeField] private float _zoomSmoothness = 8f;

        // Teleport detection uses the reference viewport, independent of zoom.
        private const float TeleportViewportHalfHeight = GameplayCameraDefaults.ReferenceOrthographicSize;

        // Original Effects.Create(Hurt), RVA 0x198FD48: assign, do not add.
        // TpCamera.Update/Update30, RVA 0x1A97420 / 0x1A975D4.
        // The original advances at most one effect step per rendered frame.


        private const float FollowSettleEpsilonSquared = 0.000001f;

        private float _originalZ;
        private Camera _camera = null!;
        private ILocalPlayer? _subscribedPlayer;
        public event Action<float>? OnZoomChanged;
        private CameraFollowEffects? _effects;
        private CameraZoomController? _zoomController;
        private bool _hasSnappedToServerPosition;
        private bool _localPlayerSpawnSubscription;
        private Vector3 _followVelocity;
        private Robot? _targetRobot;
        [Inject] private Camera _injectedCamera = null!;
        [Inject] private IInputBlocker _inputBlocker = null!;
        [Inject] private IClientConfigManager? _clientConfig = null;
        [Inject] private Kern.Game.Player.Input.TouchGestureTracker? _touchGestures = null;
        [Inject] private ILocalPlayerState _localPlayer = null!;
        [Inject] private IWorldPresentationState _presentationState = null!;
        [Inject] private IMapDataProvider? _mapDataProvider = null;


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
            _zoomController = new CameraZoomController(
                _camera,
                _clientConfig,
                _inputBlocker,
                _touchGestures,
                _minZoom,
                _maxZoom,
                _zoomStepPerNotch,
                _zoomSmoothness,
                value => OnZoomChanged?.Invoke(value));
            _zoomController.Initialize();
            _effects = new CameraFollowEffects(_camera, _localPlayer, _presentationState);
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
            if (_mapDataProvider != null)
            {
                _mapDataProvider.OnWorldInitialized -= HandleWorldInitialized;
                _mapDataProvider.OnWorldInitialized += HandleWorldInitialized;
            }
        }

        protected void OnEnable()
        {
            _zoomController?.Enable();
        }

        protected void OnDestroy()
        {
            if (_mapDataProvider != null)
            {
                _mapDataProvider.OnWorldInitialized -= HandleWorldInitialized;
            }

            if (_subscribedPlayer != null)
            {
                _subscribedPlayer.OnPlayerMoved -= HandlePlayerMoved;
                _subscribedPlayer.OnPlayerTeleported -= HandlePlayerTeleported;
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

            _zoomController?.Disable();
        }

        protected void OnDisable()
        {
            _zoomController?.Disable();
        }

        private Vector3 ResolveFollowPoint(Transform target)
        {
            if (_targetRobot == null || _targetRobot.transform != target)
            {
                _targetRobot = target.TryGetComponent(out Robot robot) ? robot : null;
            }

            return _targetRobot != null ? _targetRobot.CameraAnchor : target.position;
        }

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
                Vector3 targetPosition = ResolveFollowPoint(_target) + new Vector3(_offset.x, _offset.y, 0f);
                cameraTransform.position = _zoomController!.SnapToPixelGrid(
                    new Vector3(targetPosition.x, targetPosition.y, _originalZ));
                _hasSnappedToServerPosition = true;
                _followVelocity = Vector3.zero;
            }
        }

        private void HandleWorldInitialized()
        {
            _hasSnappedToServerPosition = false;
            _followVelocity = Vector3.zero;
        }

        private void HandlePlayerMoved(Vector2Int oldPosition, Vector2Int newPosition)
        {
            if (oldPosition == newPosition)
            {
                return;
            }

            if (!_hasSnappedToServerPosition)
            {
                _hasSnappedToServerPosition = true;
                SnapToTarget();
                return;
            }

            SnapIfOutsideTeleportViewport();
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
                _subscribedPlayer.OnPlayerTeleported -= HandlePlayerTeleported;
            }

            _subscribedPlayer = player;
            _subscribedPlayer.OnPlayerMoved -= HandlePlayerMoved;
            _subscribedPlayer.OnPlayerTeleported -= HandlePlayerTeleported;
            _subscribedPlayer.OnPlayerMoved += HandlePlayerMoved;
            _subscribedPlayer.OnPlayerTeleported += HandlePlayerTeleported;
        }

        // Only teleports outside the fixed reference viewport snap the camera.
        private void HandlePlayerTeleported()
        {
            if (_presentationState.IsTeleportPreparing)
            {
                WorldPresentationSnapshot transition = _presentationState.Current;
                Vector3 cameraDestination = transition.Destination +
                    new Vector3(_offset.x, _offset.y, 0f);
                cameraDestination.z = _originalZ;
                _presentationState.UpdateTeleportDestination(
                    transition.TransitionId,
                    cameraDestination);
                return;
            }

            SnapIfOutsideTeleportViewport();
        }

        private void SnapIfOutsideTeleportViewport()
        {
            if (_presentationState.IsTeleportPreparing)
            {
                return;
            }

            if (_camera == null || _target == null || _target == _camera.transform)
            {
                return;
            }

            Vector3 point = ResolveFollowPoint(_target);
            Vector3 desired = new(point.x + _offset.x, point.y + _offset.y, _originalZ);
            HandleViewJump(_camera.transform, desired);
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
                _zoomController?.ApplyDefaultZoom(DefaultOrthographicSize);

                var player = _localPlayer?.Current;
                if (player != null)
                {
                    _camera.transform.position = _zoomController!.SnapToPixelGrid(new Vector3(
                        player.transform.position.x,
                        player.transform.position.y,
                        DefaultCameraDepthZ));
                }

                return;
            }

            if (!_presentationState.IsTeleportPreparing)
            {
                _zoomController?.Update();
            }

            Vector3 before = _camera.transform.position;
            Vector3 velocityBefore = _followVelocity;
            HandleFollow(out bool usedViewTransition);
            if (!_presentationState.IsTeleportPreparing)
            {
                _effects?.UpdateHurtShake();
            }

            _effects?.ReportCameraJump(
                before,
                velocityBefore,
                _followVelocity,
                usedViewTransition,
                _targetRobot,
                _followSmoothTime);
        }

        // В PixelPerfect масштаб — целое число пикселей на тексель, уровней
        // всего несколько. Трекпад присылает доли щелчка; они копятся, и
        // каждый целый щелчок ведёт ровно на соседний уровень.
        private void HandleFollow(out bool usedViewTransition)
        {
            usedViewTransition = false;
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

            Vector3 targetPosition = ResolveFollowPoint(_target) + new Vector3(_offset.x, _offset.y, 0f);
            Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, _originalZ);

            if (_presentationState.IsTeleportPreparing)
            {
                WorldPresentationSnapshot transition = _presentationState.Current;
                desiredPosition = transition.Destination;
                if (_presentationState.CommitTeleport(
                    transition.TransitionId,
                    desiredPosition,
                    _camera.orthographicSize * _camera.aspect,
                    _camera.orthographicSize,
                    WorldGridDefaults.CellSize))
                {
                    if (IsOutsideTeleportViewport(
                        desiredPosition - cameraTransform.position,
                        _camera.aspect))
                    {
                        cameraTransform.position = _zoomController!.SnapToPixelGrid(desiredPosition);
                        _hasSnappedToServerPosition = true;
                        _followVelocity = Vector3.zero;
                    }
                }

                usedViewTransition = true;
                return;
            }

            if (HandleViewJump(cameraTransform, desiredPosition))
            {
                usedViewTransition = true;
                return;
            }

            // SmoothDamp is frame-rate independent — unlike Lerp(dt), it handles variable dt
            // without introducing jitter during frame spikes (e.g. terrain mesh rebuilds).
            float smoothTime = Mathf.Max(_followSmoothTime, 0.001f);
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
            cameraTransform.position = _zoomController!.SnapToPixelGrid(smoothed);
        }

        // The teleport threshold is fixed at the project's reference size.
        // Teleports enter the shared presentation transition before changing
        // entity poses. This path remains for non-teleport camera corrections.
        private bool HandleViewJump(Transform cameraTransform, Vector3 desiredPosition)
        {
            Vector3 offset = desiredPosition - cameraTransform.position;
            if (!IsOutsideTeleportViewport(offset, _camera.aspect))
            {
                return false;
            }

            _followVelocity = Vector3.zero;
            cameraTransform.position = _zoomController!.SnapToPixelGrid(desiredPosition);
            _hasSnappedToServerPosition = true;
            return true;
        }

        internal static bool IsOutsideTeleportViewport(Vector3 offset, float aspect) =>
            Mathf.Abs(offset.x) > TeleportViewportHalfHeight * aspect ||
            Mathf.Abs(offset.y) > TeleportViewportHalfHeight;

        // Размер применяется как есть. Квантование PixelPerfect делает цель
        // (QuantizeIfPixelPerfect), а не каждый кадр: иначе переход между
        // уровнями превращался в скачок, и сглаживание зума было не видно.
        // Зум — половина короткой стороны кадра; камере нужна половина
        // высоты. На экране шире 2:1 высота уменьшается, чтобы ширина не
        // выходила за потолок (CameraViewRules). В PixelPerfect уменьшенный
        // кадр снова садится на целое число пикселей на тексель.
        public void TriggerHurtShake() => _effects?.TriggerHurtShake();

        public void ClearHurtShake() => _effects?.ClearHurtShake();

        public void SetScrollEnabled(bool enabled) => _zoomController?.SetScrollEnabled(enabled);
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

                Kern.Diagnostics.World.KernGizmos.DrawLabel(_target.position + (Vector3.up * 0.7f), "Camera Target", Color.yellow);
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
