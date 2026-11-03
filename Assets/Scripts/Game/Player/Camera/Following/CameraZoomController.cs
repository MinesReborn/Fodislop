#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Player.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kern.Game.Player.Cameras;

internal sealed class CameraZoomController
{
    private const float ZoomSettleEpsilon = 0.001f;
    private const float InteractiveMinimumZoom = 7f;
    private const float InteractiveMaximumZoom = 22f;
    private const float ZoomInputSensitivity = 0.8f;
    private const float MaximumZoomTargetRatio = 1.25f;
    private const float ZoomEdgeBand = 0.15f;
    private const float ZoomEdgeMinimumFactor = 0.25f;

    private readonly Camera _camera;
    private readonly IInputBlocker _inputBlocker;
    private readonly TouchGestureTracker? _touchGestures;
    private readonly Action<float> _zoomChanged;
    private readonly float _zoomStepPerNotch;
    private readonly float _zoomSmoothness;
    private readonly float _minimumZoom;
    private readonly float _maximumZoom;
    private readonly CameraPixelGridAligner _aligner;
    private float MinimumZoom => _minimumZoom;
    private float MaximumZoom => _maximumZoom;
    private InputAction? _scrollAction;
    private bool _scrollEnabled = true;
    private bool _scrollNullLogged;
    private float _pixelZoomNotches;
    private float _targetZoom;
    private float _currentZoom;
    private float _lastZoom;

    public CameraZoomController(
        Camera camera,
        IClientConfigManager? clientConfig,
        IInputBlocker inputBlocker,
        TouchGestureTracker? touchGestures,
        float minZoom,
        float maxZoom,
        float zoomStepPerNotch,
        float zoomSmoothness,
        Action<float> zoomChanged)
    {
        _camera = camera;
        _inputBlocker = inputBlocker;
        _touchGestures = touchGestures;
        _zoomStepPerNotch = zoomStepPerNotch;
        _zoomSmoothness = zoomSmoothness;
        _zoomChanged = zoomChanged;
        _minimumZoom = Mathf.Max(InteractiveMinimumZoom,
            Mathf.Max(minZoom, GameplayCameraDefaults.MinimumOrthographicSize));
        _maximumZoom = Mathf.Clamp(maxZoom, _minimumZoom,
            Mathf.Min(InteractiveMaximumZoom, GameplayCameraDefaults.MaximumOrthographicSize));
        _aligner = new CameraPixelGridAligner(clientConfig);
    }

    public void Initialize()
    {
        float initialZoom = (_minimumZoom + _maximumZoom) * 0.5f;
        _targetZoom = QuantizeIfPixelPerfect(initialZoom);
        _currentZoom = _targetZoom;
        _lastZoom = _currentZoom;
        ApplyZoom(_currentZoom);
        InitializeInput();
    }

    public void Enable()
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

    public void Disable() => DisposeScrollAction();

    public void SetScrollEnabled(bool enabled) => _scrollEnabled = enabled;

    public void Update() => HandleZoom();

    public void ApplyDefaultZoom(float zoom) => ApplyZoom(QuantizeIfPixelPerfect(zoom));

    public Vector3 SnapToPixelGrid(Vector3 position) =>
        _aligner.SnapPosition(position, _camera.orthographicSize);

        private void InitializeInput()
        {
            _scrollAction = InputSystem.actions?.FindAction(
                "UI/ScrollWheel",
                throwIfNotFound: false);
            _scrollAction?.Enable();
        }


        private void DisposeScrollAction()
        {
            _scrollAction?.Disable();
            _scrollAction = null;
        }


        private void HandleZoom()
        {
            // Блокировка ввода (чат, меню, окно) и выключенная прокрутка
            // запрещают только новое колёсико. Колесо, отданное прокрутке UI
            // (лог глобального чата), зум тоже не двигает. Досглаживание уже
            // начатого зума к цели продолжается: ранний выход замораживал
            // зум на полпути.
            bool acceptsInput = _scrollEnabled &&
                !(_inputBlocker != null &&
                  (_inputBlocker.IsInputBlocked || _inputBlocker.IsMouseWheelClaimedByUI));
            float scrollInput = acceptsInput ? ReadScroll() : 0f;

            // Пинч двумя пальцами: пальцы разошлись — приближение. Отношение
            // расстояний переводится в щелчки колёсика, и дальше зум идёт тем
            // же путём — с теми же границами, упором и квантованием.
            float pinch = _touchGestures?.ConsumePinchScale() ?? 1f;
            if (acceptsInput && Mathf.Abs(pinch - 1f) > 0.0001f)
            {
                scrollInput += Mathf.Log(pinch) / Mathf.Log(1f + _zoomStepPerNotch);
            }

            if (Mathf.Abs(scrollInput) > 0.001f)
            {
                ApplyZoomInput(scrollInput * ZoomInputSensitivity);
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
                _zoomChanged(_currentZoom);
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


        private void ApplyZoomInput(float notches)
        {
            // Reverse from the visible scale, rather than first consuming the
            // previous gesture's queued movement towards the opposite limit.
            if (notches * (_targetZoom - _currentZoom) > 0f)
            {
                _targetZoom = _currentZoom;
            }

            if (notches * _pixelZoomNotches < 0f)
            {
                _pixelZoomNotches = 0f;
            }

            if (_aligner.QuantizesZoom)
            {
                _targetZoom = StepPixelPerfectZoom(_targetZoom, notches);
                return;
            }

            float next = StepSmoothZoom(_targetZoom, notches);
            // A long scroll burst must not queue an entire trip to an edge.
            _targetZoom = Mathf.Clamp(next,
                Mathf.Max(MinimumZoom, _currentZoom / MaximumZoomTargetRatio),
                Mathf.Min(MaximumZoom, _currentZoom * MaximumZoomTargetRatio));
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

    private float StepPixelPerfectZoom(float size, float notches)
        {
            _pixelZoomNotches += notches;
            int levels = (int)_pixelZoomNotches;
            if (levels == 0)
            {
                return size;
            }

            _pixelZoomNotches -= levels;
            return _aligner.StepQuantizedSize(size, levels, MinimumZoom, MaximumZoom);
        }

    private float QuantizeIfPixelPerfect(float size) =>
            _aligner.QuantizesZoom
                ? _aligner.ResolveOrthographicSize(size, MinimumZoom, MaximumZoom)
                : size;

        private void ApplyZoom(float zoom)
        {
            float size = CameraViewRules.OrthographicSize(zoom, _camera.aspect);
            if (size < zoom && _aligner.QuantizesZoom)
            {
                size = _aligner.ResolveOrthographicSize(size, Mathf.Min(size, MinimumZoom), MaximumZoom);
            }

            if (!Mathf.Approximately(_camera.orthographicSize, size))
            {
                _camera.orthographicSize = size;
            }
        }


}
