using System;
using Fodinae.Scripts.Player.Logic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.Player
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Follow Settings")]
        [SerializeField]
        private Transform _target;
        [SerializeField]
        private float _smoothSpeed = 5f;
        [SerializeField]
        private Vector2 _offset = Vector2.zero;

        [Header("Zoom Settings")]
        [SerializeField]
        private float _zoomSpeed = 10f;
        [SerializeField]
        private float _minZoom = 5f;
        [SerializeField]
        private float _maxZoom = 30f;
        [SerializeField]
        private float _zoomSmoothness = 8f;

        private float _originalZ;
        private Camera _camera;
        private float _targetZoom;
        private float _currentZoom;
        private float _lastZoom;
        public event Action<float> OnZoomChanged;
        private InputAction _scrollAction;
        private bool _scrollEnabled = true;
        private Vector3 _followVelocity;

        protected void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        protected void Start()
        {
            _originalZ = transform.position.z;
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("[CameraFollow] Camera component not found on this GameObject!");
                enabled = false;
                return;
            }

            if (GetComponent<FMODUnity.StudioListener>() == null)
            {
                gameObject.AddComponent<FMODUnity.StudioListener>();
            }

            _targetZoom = _camera.orthographicSize;
            _currentZoom = _targetZoom;
            _lastZoom = _currentZoom;
            if (_target == null)
            {
                var player = PlayerMovementController.LocalPlayer;
                if (player != null)
                {
                    _target = player.transform;
                }
                else
                {
                    Debug.LogWarning("[CameraFollow] No target assigned and no PlayerMovementController found!");
                }
            }

            InitializeInput();
        }

        private void InitializeInput()
        {
            _scrollAction = new InputAction("Scroll", binding: "<Mouse>/scroll");
            _scrollAction.Enable();
        }

        protected void OnDestroy()
        {
            _scrollAction?.Disable();
            _scrollAction?.Dispose();
        }

        protected void LateUpdate()
        {
            HandleZoom();
            HandleFollow();
        }

        private void HandleZoom()
        {
            if (!_scrollEnabled)
            {
                return;
            }

            if (_camera == null)
            {
                Debug.LogError("[CameraFollow] Camera is null in HandleZoom!");
                return;
            }

            if (_scrollAction == null)
            {
                Debug.LogError("[CameraFollow] Scroll action is null in HandleZoom!");
                return;
            }

            float scrollInput = 0f;
            try
            {
                scrollInput = _scrollAction.ReadValue<Vector2>().y;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[CameraFollow] Error reading scroll input: {e.Message}");
                return;
            }

            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                _targetZoom -= scrollInput * _zoomSpeed * Time.deltaTime;
                _targetZoom = Mathf.Clamp(_targetZoom, _minZoom, _maxZoom);
            }

            _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, _zoomSmoothness * Time.deltaTime);
            _camera.orthographicSize = _currentZoom;

            if (Mathf.Abs(_currentZoom - _lastZoom) > 0.01f)
            {
                _lastZoom = _currentZoom;
                OnZoomChanged?.Invoke(_currentZoom);
            }
        }

        private void HandleFollow()
        {
            if (_target == null)
            {
                if (PlayerMovementController.LocalPlayer != null)
                {
                    _target = PlayerMovementController.LocalPlayer.transform;
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
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _followVelocity, smoothTime, float.PositiveInfinity, Time.deltaTime);
        }

        public void SetTarget(Transform newTarget) => _target = newTarget;
        public void SetZoom(float zoomLevel)
        {
            if (_camera != null)
            {
                _targetZoom = Mathf.Clamp(zoomLevel, _minZoom, _maxZoom);
            }
            else
            {
                Debug.LogError("[CameraFollow] Cannot set zoom - camera is null!");
            }
        }

        public float GetCurrentZoom() => _currentZoom;
        public void SetScrollEnabled(bool enabled) => _scrollEnabled = enabled;
        public void Reinitialize()
        {
            Start();
        }

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

                Fodinae.Scripts.World.FodinaeGizmos.DrawLabel(_target.position + (Vector3.up * 0.7f), "Camera Target", Color.yellow);
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
