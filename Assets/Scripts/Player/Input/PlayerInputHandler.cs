#nullable enable

using Fodinae.Player.Interfaces;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Player.Input
{
    public class PlayerInputHandler : MonoBehaviour, IPlayerInput
    {
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField]
        private InputActionReference? _moveActionReference;

        private Vector2 _moveInput;
        private bool _isGamepadActive;

        public Vector2 MoveInput => _moveInput;
        public bool IsGamepadActive => _isGamepadActive;

        public bool WantsToToggleAutoDig =>
            (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);

        public bool WantsToToggleAggression =>
            (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame);

        public bool WantsToGeo =>
            (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);

        public bool WantsToHeal =>
            (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame);

        public bool WantsToBuildCyan =>
            (Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame);

        public bool WantsToBuildGray =>
            (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.dpad.down.wasPressedThisFrame);

        public bool WantsToBuildGreen =>
            (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        public bool WantsToBuildWhite =>
            (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);

        public bool WantsToDig =>
            (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) ||
            (Gamepad.current != null && (Gamepad.current.rightTrigger.isPressed || Gamepad.current.buttonSouth.isPressed));

        public bool IsShiftPressed =>
            (Keyboard.current != null && Keyboard.current.shiftKey.isPressed) ||
            (Gamepad.current != null && (Gamepad.current.rightShoulder.isPressed || Gamepad.current.leftStickButton.isPressed));

        public bool IsCtrlPressed =>
            (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed);

        [VContainer.Inject]
        private Fodinae.Core.Interfaces.IClientConfigManager? _clientConfig;

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
                        _isGamepadActive = false;
                    }

                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                    {
                        _moveInput.y -= 1f;
                        _isGamepadActive = false;
                    }

                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                    {
                        _moveInput.x -= 1f;
                        _isGamepadActive = false;
                    }

                    if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                    {
                        _moveInput.x += 1f;
                        _isGamepadActive = false;
                    }
                }

                // Mouse pointer scheme: if enabled or right button held, move toward screen center offset
                var cfg = _clientConfig != null ? _clientConfig.Config : null;
                bool isMouseScheme = cfg != null && cfg.Interface.ControlScheme == 1;
                bool useMousePointer = isMouseScheme || (Mouse.current != null && Mouse.current.rightButton.isPressed);
                if (useMousePointer && Mouse.current != null)
                {
                    if (Mouse.current.rightButton.isPressed || (isMouseScheme && Mouse.current.leftButton.isPressed))
                    {
                        Vector2 mousePos = Mouse.current.position.ReadValue();
                        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                        Vector2 dir = mousePos - center;
                        if (dir.sqrMagnitude > 400f) // deadzone 20px
                        {
                            _moveInput = dir.normalized;
                        }
                    }
                }

                if (Gamepad.current != null)
                {
                    Vector2 stick = Gamepad.current.leftStick.ReadValue();
                    if (stick.sqrMagnitude > 0.04f)
                    {
                        _moveInput = stick;
                        _isGamepadActive = true;
                    }
                    else
                    {
                        Vector2 dpad = Gamepad.current.dpad.ReadValue();
                        if (dpad.sqrMagnitude > 0.04f)
                        {
                            _moveInput = dpad;
                            _isGamepadActive = true;
                        }
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
