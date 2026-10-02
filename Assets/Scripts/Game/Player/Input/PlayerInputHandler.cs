#nullable enable

using System;
using Kern.Player.Interfaces;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kern.Player.Input
{
    public class PlayerInputHandler : MonoBehaviour, IPlayerInput
    {
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField]
        private InputActionReference? _moveActionReference = null;

        private Vector2 _moveInput;
        private bool _isGamepadActive;

        public Vector2 MoveInput => _moveInput;
        public bool IsGamepadActive => _isGamepadActive;

        public bool WantsToToggleAutoDig =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyAutoDig, Key.E)) ||
             (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame));

        // Агрессия - в старом клиенте физическая клавиша L (на русской
        // раскладке на ней "Д"): "Врубите агрессию [L]".
        public bool WantsToToggleAggression =>
            !InputIntercepted &&
            WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyAggression, Key.L));

        public bool WantsToGeo =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyGeo, Key.G)) ||
             (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame));

        public bool WantsToHeal =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyHeal, Key.V)) ||
             (Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame));

        public bool IsHealHeld =>
            !InputIntercepted &&
            (IsHeld(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyHeal, Key.V)) ||
             (Gamepad.current != null && Gamepad.current.dpad.right.isPressed));

        public bool WantsToBuildCyan =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyBuildCyan, Key.Y)) ||
             (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame));

        public bool WantsToBuildGray =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyBuildGray, Key.H)) ||
             (Gamepad.current != null && Gamepad.current.dpad.down.wasPressedThisFrame));

        public bool WantsToBuildGreen =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyBuildGreen, Key.F)) ||
             (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame));

        public bool WantsToBuildWhite =>
            !InputIntercepted &&
            (WasPressedThisFrame(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyBuildWhite, Key.J)) ||
             (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame));

        public bool WantsToDig =>
            !InputIntercepted &&
            (IsHeld(ConfiguredKey(
                _clientConfig?.Config.Interface.KeyDig, Key.Space)) ||
             (Gamepad.current != null && (Gamepad.current.rightTrigger.isPressed || Gamepad.current.buttonSouth.isPressed)));

        public bool IsShiftPressed =>
            (Keyboard.current != null && Keyboard.current.shiftKey.isPressed) ||
            (Gamepad.current != null && (Gamepad.current.rightShoulder.isPressed || Gamepad.current.leftStickButton.isPressed));

        public bool IsCtrlPressed =>
            (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed);

        [VContainer.Inject]
        private Kern.Core.Interfaces.IClientConfigManager? _clientConfig = null;

        [VContainer.Inject]
        private Kern.Core.Interfaces.IInputBlocker? _inputBlocker = null;

        // Перехват новой клавиши на вкладке «Управление»: пока он идёт,
        // игровые действия по клавишам молчат — нажатие означает выбор
        // бинда, а не запуск действия. Флаг живёт в IInputBlocker
        // (InputBlockState агрегирует состояние UI), чтобы слою Game не
        // приходилось ссылаться на UI-сборку.
        private bool InputIntercepted =>
            _inputBlocker != null && _inputBlocker.IsKeyCaptureInProgress;

        // Клавиша действия из живого конфига (InterfaceSettings). Пустое или
        // неизвестное имя (правка конфига руками, поле из будущей схемы)
        // откатывается к дефолту действия.
        private Key ConfiguredKey(string? configured, Key fallback)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return fallback;
            }

            return Enum.TryParse(configured.Trim(), ignoreCase: true, out Key key) &&
                key != Key.None
                ? key
                : fallback;
        }

        private static bool WasPressedThisFrame(Key key) =>
            key != Key.None &&
            Keyboard.current != null &&
            Keyboard.current[key].wasPressedThisFrame;

        private static bool IsHeld(Key key) =>
            key != Key.None &&
            Keyboard.current != null &&
            Keyboard.current[key].isPressed;

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
            // Карта мира — оверлей, а не модальное окно: смотреть в неё можно
            // не переставая идти. Движение и действия блокирует только реальный
            // UI (окна сервера, фокус чата, пауза, инструменты) — поэтому здесь
            // ExcludingMapMode, а не полный IsInputBlocked.
            if (_inputBlocker != null && _inputBlocker.IsInputBlockedExcludingMapMode)
            {
                _moveInput = Vector2.zero;
                return;
            }

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

                ReadMousePointerMovement();
            }

            if (_moveInput.sqrMagnitude > 1f)
            {
                _moveInput.Normalize();
            }
        }

        // Схема «Указатель мыши» (InterfaceSettings.ControlScheme == 1): пока
        // зажата ПКМ, дрон идёт к указателю от центра экрана — то, что
        // обещает вкладка «Управление» («ПКМ (удержание)»). ЛКМ остаётся за
        // клик-маршрутом. Мёртвая зона 20 px гасит дрожь у центра.
        private void ReadMousePointerMovement()
        {
            if (Mouse.current == null ||
                _clientConfig?.Config.Interface.ControlScheme != 1 ||
                !Mouse.current.rightButton.isPressed)
            {
                return;
            }

            Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 direction = Mouse.current.position.ReadValue() - center;
            if (direction.sqrMagnitude > 400f)
            {
                _moveInput = direction.normalized;
                _isGamepadActive = false;
            }
        }
    }
}
