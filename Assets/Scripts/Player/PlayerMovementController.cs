using Fodinae.Assets.Scripts.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Assets.Scripts.Player
{
    /// <summary>
    /// Foundation for player movement.
    /// Currently moves the transform directly to test map loading and rendering.
    /// Structured so it can be easily extended to use Rigidbody2D by modifying the ApplyMovement method.
    /// </summary>
    public class PlayerMovementController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 15f;
        
        private Robot _robot;

        [Header("Input Dependencies")]
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField] private InputActionReference _moveActionReference;

        private Vector2 _moveInput;
        private bool _isMoving = false;

        private void OnEnable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Disable();
            }
        }

        private void Awake()
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.freezeRotation = true;
            }
            _robot = GetComponent<Robot>();
            if (_robot != null)
            {
                _robot.MoveSpeed = _moveSpeed;
            }
        }

        private void Start()
        {
            // Align to grid center on start
            Vector3 targetGridPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z
            );
            transform.position = targetGridPos;
            if (_robot != null)
            {
                _robot.TargetPosition = targetGridPos;
            }
        }

        private void Update()
        {
            ReadInput();
            ApplyMovement();
        }

        private void ReadInput()
        {
            // Prefer the Input Action Reference if assigned in the inspector
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveInput = _moveActionReference.action.ReadValue<Vector2>();
            }
            else
            {
                // Fallback direct polling of Keyboard for immediate testing without needing inspector setup
                _moveInput = Vector2.zero;
                
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) _moveInput.y += 1f;
                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) _moveInput.y -= 1f;
                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) _moveInput.x -= 1f;
                    if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) _moveInput.x += 1f;
                }
            }

            // Normalize to prevent faster diagonal movement
            if (_moveInput.sqrMagnitude > 1f)
            {
                _moveInput.Normalize();
            }
        }

        private void ApplyMovement()
        {
            if (_robot == null) return;

            if (_isMoving)
            {
                if (Vector3.Distance(transform.position, _robot.TargetPosition) < 0.001f)
                {
                    _isMoving = false;
                }
            }

            if (!_isMoving)
            {
                if (_moveInput != Vector2.zero)
                {
                    Vector2Int direction = Vector2Int.zero;
                    if (Mathf.Abs(_moveInput.x) > Mathf.Abs(_moveInput.y))
                    {
                        direction.x = _moveInput.x > 0 ? 1 : -1;
                    }
                    else
                    {
                        direction.y = _moveInput.y > 0 ? 1 : -1;
                    }

                    if (direction != Vector2Int.zero)
                    {
                        _robot.TargetPosition = transform.position + new Vector3(direction.x, direction.y, 0f);
                        _isMoving = true;

                        // Determine cardinal direction (0: Right, 90: Up, 180: Left, 270: Down)
                        if (direction.x != 0)
                            _robot.TargetAngle = direction.x > 0 ? 0f : 180f;
                        else
                            _robot.TargetAngle = direction.y > 0 ? 90f : 270f;
                    }
                }
            }
        }

        /// <summary>
        /// Allows injecting movement input externally (e.g. from an on-screen joystick or UI buttons)
        /// </summary>
        public void SetMovementInput(Vector2 input)
        {
            _moveInput = input;
        }
    }
}
