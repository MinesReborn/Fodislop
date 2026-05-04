using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Assets.Scripts.Player
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Follow Settings")]
        [SerializeField] private Transform _target;
        [SerializeField] private float _smoothSpeed = 5f;
        [SerializeField] private Vector2 _offset = Vector2.zero;

        [Header("Zoom Settings")]
        [SerializeField] private float _zoomSpeed = 10f;
        [SerializeField] private float _minZoom = 5f;
        [SerializeField] private float _maxZoom = 30f;
        [SerializeField] private float _zoomSmoothness = 8f;

        private float _originalZ;
        private Camera _camera;
        private float _targetZoom;
        private float _currentZoom;
        private PlayerInput _playerInput;
        private InputAction _scrollAction;

        private void Start()
        {
            _originalZ = transform.position.z;
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("CameraFollow: Camera component not found on this GameObject!");
                enabled = false;
                return;
            }
            _targetZoom = _camera.orthographicSize;
            _currentZoom = _targetZoom;
            if (_target == null)
            {
                var player = FindObjectOfType<PlayerMovementController>();
                if (player != null)
                    _target = player.transform;
                else
                    Debug.LogWarning("CameraFollow: No target assigned and no PlayerMovementController found!");
            }
            InitializeInput();
        }

        private void InitializeInput()
        {
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null)
            {
                _playerInput = gameObject.AddComponent<PlayerInput>();
            }
            _scrollAction = new InputAction("Scroll", binding: "<Mouse>/scroll");
            _scrollAction.Enable();
        }

        private void OnDestroy()
        {
            _scrollAction?.Disable();
            _scrollAction?.Dispose();
        }

        private void LateUpdate()
        {
            HandleZoom();
            HandleFollow();
        }

        private void HandleZoom()
        {
            if (_camera == null)
            {
                Debug.LogError("CameraFollow: Camera is null in HandleZoom!");
                return;
            }
            if (_scrollAction == null)
            {
                Debug.LogError("CameraFollow: Scroll action is null in HandleZoom!");
                return;
            }
            float scrollInput = 0f;
            try
            {
                scrollInput = _scrollAction.ReadValue<Vector2>().y;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"CameraFollow: Error reading scroll input: {e.Message}");
                return;
            }

            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                _targetZoom -= scrollInput * _zoomSpeed * Time.deltaTime;
                _targetZoom = Mathf.Clamp(_targetZoom, _minZoom, _maxZoom);
            }

            _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, _zoomSmoothness * Time.deltaTime);
            _camera.orthographicSize = _currentZoom;
        }

        private void HandleFollow()
        {
            if (_target == null)
            {
                var player = FindObjectOfType<PlayerMovementController>();
                if (player != null)
                    _target = player.transform;
                return;
            }
            Vector3 targetPosition = _target.position + new Vector3(_offset.x, _offset.y, 0f);
            Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, _originalZ);
            float t = _smoothSpeed;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, t);
        }

        public void SetTarget(Transform newTarget) => _target = newTarget;

        public void SetZoom(float zoomLevel)
        {
            if (_camera != null)
                _targetZoom = Mathf.Clamp(zoomLevel, _minZoom, _maxZoom);
            else
                Debug.LogError("CameraFollow: Cannot set zoom - camera is null!");
        }

        public float GetCurrentZoom() => _currentZoom;
        public void Reinitialize()
        {
            Start();
        }
    }
}