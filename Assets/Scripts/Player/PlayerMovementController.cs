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
        [SerializeField] private float _rotationSpeed = 1080f;
        
        private float _targetAngle = 0f;

        [Header("Input Dependencies")]
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField] private InputActionReference _moveActionReference;

        private Vector2 _moveInput;

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
        }

        private void Start()
        {
            _targetAngle = transform.eulerAngles.z;
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
            if (_moveInput != Vector2.zero)
            {
                // Move the transform directly for testing
                Vector3 movement = new Vector3(_moveInput.x, _moveInput.y, 0f) * (_moveSpeed * Time.deltaTime);
                transform.position += movement;

                // Determine cardinal direction (0: Right, 90: Up, 180: Left, 270: Down)
                if (Mathf.Abs(_moveInput.x) > Mathf.Abs(_moveInput.y))
                {
                    _targetAngle = _moveInput.x > 0 ? 0f : 180f; // Right or Left
                }
                else
                {
                    _targetAngle = _moveInput.y > 0 ? 90f : 270f; // Up or Down
                }
            }

            // Smoothly rotate towards the target angle
            float currentAngle = transform.eulerAngles.z;
            if (!Mathf.Approximately(currentAngle, _targetAngle))
            {
                float newAngle = Mathf.MoveTowardsAngle(currentAngle, _targetAngle, _rotationSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0, 0, newAngle);
            }

            // Align to grid if not moving (or always align to nearest center for simplicity now)
            // But user asked "make the player align to the terrain grid"
            // If we want it to snap, we could do it here.
            // However, smooth movement usually doesn't snap every frame unless it's tile-based.
            // Let's at least make sure it stays on the grid logically or snaps when close to zero input.
            if (_moveInput == Vector2.zero)
            {
                Vector3 pos = transform.position;
                pos.x = Mathf.Floor(pos.x) + 0.5f;
                pos.y = Mathf.Floor(pos.y) + 0.5f;
                transform.position = pos;
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
