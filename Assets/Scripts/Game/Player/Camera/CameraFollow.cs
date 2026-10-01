#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Networking;
using Kern.Player.Logic;
using Kern.World.Streaming;
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
        [SerializeField] private float _minZoom = 5f;
        [SerializeField] private float _maxZoom = 30f;

        // Скорость, с которой зум догоняет цель, в 1/с; форма 1 − e^(−k·dt)
        // не зависит от частоты кадров.
        [SerializeField] private float _zoomSmoothness = 8f;

        private const float ZoomSettleEpsilon = 0.001f;

        // Мягкий упор: в последней доле диапазона (в логарифмической шкале)
        // шаг зума плавно уменьшается к границе. За границы зум не выходит —
        // освещение рассчитано на кадр не больше MaximumOrthographicSize.
        private const float ZoomEdgeBand = 0.15f;
        private const float ZoomEdgeMinimumFactor = 0.25f;

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
        private bool _scrollNullLogged;
        private bool _hasSnappedToServerPosition;
        private bool _localPlayerSpawnSubscription;
        private Vector3 _followVelocity;
        private float _pixelZoomNotches;
        private Robot? _targetRobot;
        [Inject] private Camera _injectedCamera = null!;
        [Inject] private IInputBlocker _inputBlocker = null!;
        [Inject] private ILocalPlayerState _localPlayer = null!;
        [Inject] private IClientConfigManager? _clientConfig = null;
        [Inject] private WorldViewTransition? _viewTransition = null;
        [Inject] private IMapDataProvider? _mapDataProvider = null;

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
            _targetZoom = QuantizeIfPixelPerfect(initialZoom);
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
            if (_mapDataProvider != null)
            {
                _mapDataProvider.OnWorldInitialized -= HandleWorldInitialized;
                _mapDataProvider.OnWorldInitialized += HandleWorldInitialized;
            }
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
            if (_mapDataProvider != null)
            {
                _mapDataProvider.OnWorldInitialized -= HandleWorldInitialized;
            }

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

        private void HandleWorldInitialized()
        {
            _hasSnappedToServerPosition = false;
            _followVelocity = Vector3.zero;
            _viewTransition?.Release();
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
                ApplyZoom(QuantizeIfPixelPerfect(DefaultOrthographicSize));

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
            Vector3 before = _camera.transform.position;
            HandleFollow();
            ReportCameraJump(before);
        }

        // Скачок камеры больше JumpReportCells клеток за кадр пишется в лог
        // вместе с тем, за чем она шла: так видно, кто его вызвал — позиция
        // с сервера, сглаживание бота или переход вида.
        private const float JumpReportCells = 2f;

        private void ReportCameraJump(Vector3 before)
        {
            Vector3 after = _camera.transform.position;
            float jump = Vector2.Distance(before, after);
            if (jump <= JumpReportCells * ProjectRuntimeContracts.World.CellSize)
            {
                return;
            }

            ILocalPlayer? player = _localPlayer?.Current;
            string robotState = _targetRobot != null
                ? $"anchor={_targetRobot.CameraAnchor}, target={_targetRobot.TargetPosition}"
                : "robot=none";
            Debug.LogWarning(
                $"[CameraFollow] Camera jumped {jump:F2} from {before} to {after}; {robotState}; " +
                $"server cell={(player != null ? player.Position.ToString() : "none")}, " +
                $"hasServerPosition={player?.HasServerPosition}, " +
                $"holding={_viewTransition?.IsHolding}, frame={Time.frameCount}.");
        }

        private void HandleZoom()
        {
            // Блокировка ввода (чат, меню, окно) и выключенная прокрутка
            // запрещают только новое колёсико. Досглаживание уже начатого зума
            // к цели продолжается: ранний выход замораживал зум на полпути.
            bool acceptsInput = _scrollEnabled &&
                !(_inputBlocker != null && _inputBlocker.IsInputBlocked);
            float scrollInput = acceptsInput ? ReadScroll() : 0f;

            if (Mathf.Abs(scrollInput) > 0.001f)
            {
                _targetZoom = _Aligner.QuantizesZoom
                    ? StepPixelPerfectZoom(_targetZoom, scrollInput)
                    : StepSmoothZoom(_targetZoom, scrollInput);
            }

            // Режим выборки могли переключить в настройках на ходу: цель
            // PixelPerfect обязана стоять на целом уровне, иначе зум никогда не
            // сядет на пиксельную сетку.
            _targetZoom = QuantizeIfPixelPerfect(_targetZoom);

            // Сглаживание идёт в логарифмической шкале, как и шаг: скорость
            // приближения одинакова вблизи и вдали. Во время движения размер
            // не квантуется, посадка на целый уровень — в конце, когда текущий
            // размер сходится с квантованной целью.
            float blend = 1f - Mathf.Exp(-_zoomSmoothness * Time.deltaTime);
            float nextZoom = Mathf.Exp(Mathf.Lerp(
                Mathf.Log(_currentZoom),
                Mathf.Log(_targetZoom),
                blend));
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

        private float ReadScroll()
        {
            if (_scrollAction != null && _scrollAction.enabled)
            {
                return _scrollAction.ReadValue<Vector2>().y;
            }

            if (Mouse.current != null)
            {
                return Mouse.current.scroll.ReadValue().y;
            }

            if (!_scrollNullLogged)
            {
                _scrollNullLogged = true;
                Debug.LogWarning("[CameraFollow] Scroll action is unavailable; mouse-wheel zoom is disabled.");
            }

            return 0f;
        }

        private float StepSmoothZoom(float size, float notches)
        {
            float logMinimum = Mathf.Log(MinimumZoom);
            float logMaximum = Mathf.Log(MaximumZoom);
            float logSize = Mathf.Log(size);

            // Прокрутка вверх приближает: кадр уменьшается.
            float delta = -notches * Mathf.Log(1f + _zoomStepPerNotch);
            float band = (logMaximum - logMinimum) * ZoomEdgeBand;
            if (band > 0f)
            {
                float remaining = delta < 0f ? logSize - logMinimum : logMaximum - logSize;
                float ease = Mathf.Clamp01(remaining / band);
                delta *= Mathf.Lerp(ZoomEdgeMinimumFactor, 1f, ease);
            }

            return Mathf.Exp(Mathf.Clamp(logSize + delta, logMinimum, logMaximum));
        }

        // В PixelPerfect масштаб — целое число пикселей на тексель, уровней
        // всего несколько. Трекпад присылает доли щелчка; они копятся, и
        // каждый целый щелчок ведёт ровно на соседний уровень.
        private float StepPixelPerfectZoom(float size, float notches)
        {
            _pixelZoomNotches += notches;
            int levels = (int)_pixelZoomNotches;
            if (levels == 0)
            {
                return size;
            }

            _pixelZoomNotches -= levels;
            return _Aligner.StepQuantizedSize(size, levels, MinimumZoom, MaximumZoom);
        }

        private float QuantizeIfPixelPerfect(float size) =>
            _Aligner.QuantizesZoom
                ? _Aligner.ResolveOrthographicSize(size, MinimumZoom, MaximumZoom)
                : size;

        // Камера следует за сглаженной позицией бота без дрожи
        // (Robot.CameraAnchor), а не за transform: в transform подмешан
        // случайный тремор и туда же на кадр пишется сырая клетка с сервера.
        private Vector3 ResolveFollowPoint(Transform target)
        {
            if (_targetRobot == null || _targetRobot.transform != target)
            {
                _targetRobot = target.TryGetComponent(out Robot robot) ? robot : null;
            }

            return _targetRobot != null ? _targetRobot.CameraAnchor : target.position;
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

            Vector3 targetPosition = ResolveFollowPoint(_target) + new Vector3(_offset.x, _offset.y, 0f);
            Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, _originalZ);

            if (HandleViewJump(cameraTransform, desiredPosition))
            {
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
            cameraTransform.position = SnapToPixelGrid(smoothed);
        }

        /// <summary>
        /// Прыжок цели дальше кадра камеры (телепорт) не догоняется
        /// сглаживанием: камера пролетела бы через всю карту, и игрок увидел бы
        /// прогрузку по дороге. Камера держит старый вид, пока террейн готовит
        /// место назначения, и переставляется одним кадром.
        /// </summary>
        /// <returns>true, если позицию камеры в этом кадре решил переход.</returns>
        private bool HandleViewJump(Transform cameraTransform, Vector3 desiredPosition)
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            if (_viewTransition is { IsHolding: true } holding)
            {
                holding.Hold(desiredPosition);
                float cellSize = ProjectRuntimeContracts.World.CellSize;
                bool ready = holding.IsReadyFor(
                    desiredPosition,
                    halfWidth / cellSize,
                    halfHeight / cellSize,
                    cellSize);
                bool expired = holding.HoldSeconds >= WorldViewTransition.MaximumHoldSeconds;
                if (!ready && !expired)
                {
                    return true;
                }

                if (!ready)
                {
                    Debug.LogWarning(
                        $"[CameraFollow] Teleport destination {desiredPosition} was not ready after " +
                        $"{WorldViewTransition.MaximumHoldSeconds:F0}s; releasing the view.");
                }

                Kern.Core.Interfaces.Diagnostics.FrameEventLog.Record(
                    $"телепорт: вид переставлен через {holding.HoldSeconds:F2} с");
                cameraTransform.position = SnapToPixelGrid(desiredPosition);
                _followVelocity = Vector3.zero;
                holding.Release();
                return true;
            }

            Vector3 offset = desiredPosition - cameraTransform.position;
            bool beyondView = Mathf.Abs(offset.x) > halfWidth || Mathf.Abs(offset.y) > halfHeight;
            if (!beyondView)
            {
                return false;
            }

            if (_viewTransition is { CanHold: true } transition)
            {
                transition.Hold(desiredPosition);
                return true;
            }

            // Показывать нечего (мир ещё грузится под экраном загрузки):
            // переставить сразу, без полёта через карту.
            cameraTransform.position = SnapToPixelGrid(desiredPosition);
            _followVelocity = Vector3.zero;
            return true;
        }

        // Размер применяется как есть. Квантование PixelPerfect делает цель
        // (QuantizeIfPixelPerfect), а не каждый кадр: иначе переход между
        // уровнями превращался в скачок, и сглаживание зума было не видно.
        private void ApplyZoom(float size)
        {
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
                Vector3 targetPosition = ResolveFollowPoint(_target) + new Vector3(_offset.x, _offset.y, 0f);
                cameraTransform.position = SnapToPixelGrid(
                    new Vector3(targetPosition.x, targetPosition.y, _originalZ));
                _followVelocity = Vector3.zero;
                _viewTransition?.Release();
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
