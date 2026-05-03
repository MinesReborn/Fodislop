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
                Debug.LogError("CameraFollow requires a Camera component!");
                return;
            }
            _targetZoom = _camera.orthographicSize;
            _currentZoom = _targetZoom;
            if (_target == null)
            {
                var player = FindObjectOfType<PlayerMovementController>();
                if (player != null) _target = player.transform;
            }
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
        }

        private void LateUpdate()
        {
            HandleZoom();
            HandleFollow();
        }

        private void HandleZoom()
        {
            float scrollInput = _scrollAction.ReadValue<Vector2>().y;
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
            if (_target != null)
            {
                Vector3 targetPosition = _target.position + new Vector3(_offset.x, _offset.y, 0f);
                Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, _originalZ);
                transform.position = Vector3.Lerp(transform.position, desiredPosition, _smoothSpeed * Time.deltaTime);
            }
        }
        public void SetTarget(Transform newTarget) => _target = newTarget;
        public void SetZoom(float zoomLevel) => _targetZoom = Mathf.Clamp(zoomLevel, _minZoom, _maxZoom);
        public float GetCurrentZoom() => _currentZoom;
    }
}