using Fodinae.Scripts.Player.Interfaces;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.Player.Input
{
    public class PlayerInputHandler : MonoBehaviour, IPlayerInput
    {
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField]
        private InputActionReference _moveActionReference;

        private Vector2 _moveInput;

        public Vector2 MoveInput => _moveInput;
        public bool WantsToToggleAutoDig => Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        public bool WantsToToggleAggression => Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame;
        public bool WantsToDig => Keyboard.current != null && Keyboard.current.zKey.isPressed;
        public bool IsShiftPressed => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        protected void OnEnable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Enable();
            }
        }

        protected void OnDisable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Disable();
            }
        }

        protected void Update()
        {
            ReadInput();
        }

        public void SetMovementInput(Vector2 input)
        {
            _moveInput = input;
        }

        private void ReadInput()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveInput = _moveActionReference.action.ReadValue<Vector2>();
            }
            else
            {
                _moveInput = Vector2.zero;

                if (Keyboard.current != null)
                {
                    if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                    {
                        _moveInput.y += 1f;
                    }

                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                    {
                        _moveInput.y -= 1f;
                    }

                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                    {
                        _moveInput.x -= 1f;
                    }

                    if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                    {
                        _moveInput.x += 1f;
                    }
                }
            }

            if (_moveInput.sqrMagnitude > 1f)
            {
                _moveInput.Normalize();
            }
        }
    }
}
