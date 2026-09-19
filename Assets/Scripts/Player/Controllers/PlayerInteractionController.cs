#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.Networking;
using Kern.Player.Logic;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.Player
{
    public class PlayerInteractionController : MonoBehaviour
    {
        [Inject]
        private Camera _mainCamera = null!;
        private UnityEngine.InputSystem.Utilities.ReadOnlyArray<KeyControl> _cachedAllKeys;
        [Inject]
        private UIDocument _injectedUIDoc = null!;
        [Inject]
        private IMapDataProvider _mapManager = null!;
        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private Kern.Core.Interfaces.ILocalPlayerState _localPlayer = null!;

        protected void Awake()
        {
            if (Keyboard.current != null)
            {
                _cachedAllKeys = Keyboard.current.allKeys;
            }
        }

        protected void Update()
        {
            if (_localPlayer is not { Current: { IsGameplayVisible: true } })
            {
                return;
            }

            HandleMouseClick();
            HandleKeyboardInput();
        }

        private void HandleMouseClick()
        {
            if (Mouse.current == null)
            {
                return;
            }

            if (_inputBlocker == null || _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                if (IsPointerOverUI(mousePos))
                {
                    return;
                }

                Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -_mainCamera.transform.position.z));

                int unityX = Mathf.FloorToInt(worldPos.x);
                int unityY = Mathf.FloorToInt(worldPos.y);

                ushort serverX = (ushort)Mathf.Clamp(unityX, 0, ushort.MaxValue);
                ushort serverY = (ushort)Mathf.Clamp(_mapManager.WorldHeight - 1 - unityY, 0, ushort.MaxValue);

                _networkService.SendAction(new ClickCellPacket(serverX, serverY));
            }
        }

        // Клик по миру шлётся только если под указателем нет видимого интерфейса.
        //
        // Одного panel.Pick мало: корень HUD, карта мира, затемнения попапов и
        // подписи стоят в picking-mode="Ignore", Pick сквозь них не видит, и
        // клик по нарисованному интерфейсу уходил в мир как ClickCellPacket.
        // Поэтому интерфейсом считается и любой видимый элемент под курсором,
        // у которого что-то нарисовано: фон, картинка или текст. Пустые
        // контейнеры на весь экран (корень, TemplateContainer) клик пропускают.
        private bool IsPointerOverUI(Vector2 mousePos)
        {
            var doc = _injectedUIDoc;
            if (doc == null || !doc.isActiveAndEnabled)
            {
                return false;
            }

            var root = doc.rootVisualElement;
            if (root?.panel == null)
            {
                return false;
            }

            // ScreenToPanel учитывает масштаб панели (ScaleWithScreenSize).
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, mousePos);
            VisualElement? picked = root.panel.Pick(panelPos);
            if (picked != null && picked != root && picked is not TemplateContainer)
            {
                return true;
            }

            return HasPaintedElementAt(root, panelPos);
        }

        private static bool HasPaintedElementAt(VisualElement element, Vector2 panelPos)
        {
            IResolvedStyle style = element.resolvedStyle;
            if (style.display == DisplayStyle.None ||
                style.visibility == Visibility.Hidden ||
                style.opacity <= 0f)
            {
                return false;
            }

            // Границы родителя не отсекают детей: абсолютно спозиционированные
            // панели выходят за пределы своих контейнеров.
            if (element.worldBound.Contains(panelPos) && IsPainted(element, style))
            {
                return true;
            }

            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                if (HasPaintedElementAt(element.hierarchy[i], panelPos))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPainted(VisualElement element, IResolvedStyle style) =>
            style.backgroundColor.a > 0f ||
            style.backgroundImage.texture != null ||
            style.backgroundImage.sprite != null ||
            style.backgroundImage.vectorImage != null ||
            (element is Image image && (image.image != null || image.sprite != null || image.vectorImage != null)) ||
            (element is TextElement text && !string.IsNullOrEmpty(text.text));

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (_inputBlocker == null || _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (!Keyboard.current.anyKey.wasPressedThisFrame)
            {
                return;
            }

            // Send unmapped keys to the server, excluding locally handled gameplay and UI hotkeys.
            for (int i = 0; i < _cachedAllKeys.Count; i++)
            {
                var keyControl = _cachedAllKeys[i];
                if (keyControl.wasPressedThisFrame)
                {
                    Key key = keyControl.keyCode;
                    if (IsLocallyHandledKey(key))
                    {
                        continue;
                    }

                    byte code = MapKeyToByte(key);
                    if (code != 0)
                    {
                        bool ctrl = Keyboard.current.ctrlKey.isPressed;
                        bool alt = Keyboard.current.altKey.isPressed;
                        bool shift = Keyboard.current.shiftKey.isPressed;

                        _networkService.SendAction(new UnmappedKeyPacket(code, ctrl, alt, shift));
                    }
                }
            }
        }

        private static bool IsLocallyHandledKey(Key key)
        {
            return key is Key.W or Key.A or Key.S or Key.D or
                          Key.UpArrow or Key.DownArrow or Key.LeftArrow or Key.RightArrow or
                          Key.Space or Key.E or Key.L or Key.G or Key.V or
                          Key.Y or Key.H or Key.F or Key.J or
                          Key.M or Key.I or Key.Tab or Key.Escape or Key.Enter or Key.NumpadEnter or
                          Key.P or Key.R or Key.T or
                          Key.Digit1 or Key.Digit2 or Key.Digit3 or Key.Digit4 or Key.Digit5 or
                          Key.Digit6 or Key.Digit7 or Key.Digit8 or Key.Digit9;
        }

        private byte MapKeyToByte(Key key)
        {
            // Simple mapping to ASCII or custom codes
            return key switch
            {
                Key.Space => 32,
                Key.Enter => 13,
                Key.Escape => 27,
                Key.Tab => 9,
                Key.Backspace => 8,
                Key.Delete => 127,

                Key.A => (byte)'a',
                Key.B => (byte)'b',
                Key.C => (byte)'c',
                Key.D => (byte)'d',
                Key.E => (byte)'e',
                Key.F => (byte)'f',
                Key.G => (byte)'g',
                Key.H => (byte)'h',
                Key.I => (byte)'i',
                Key.J => (byte)'j',
                Key.K => (byte)'k',
                Key.L => (byte)'l',
                Key.M => (byte)'m',
                Key.N => (byte)'n',
                Key.O => (byte)'o',
                Key.P => (byte)'p',
                Key.Q => (byte)'q',
                Key.R => (byte)'r',
                Key.S => (byte)'s',
                Key.T => (byte)'t',
                Key.U => (byte)'u',
                Key.V => (byte)'v',
                Key.W => (byte)'w',
                Key.X => (byte)'x',
                Key.Y => (byte)'y',
                Key.Z => (byte)'z',

                Key.Digit0 => (byte)'0',
                Key.Digit1 => (byte)'1',
                Key.Digit2 => (byte)'2',
                Key.Digit3 => (byte)'3',
                Key.Digit4 => (byte)'4',
                Key.Digit5 => (byte)'5',
                Key.Digit6 => (byte)'6',
                Key.Digit7 => (byte)'7',
                Key.Digit8 => (byte)'8',
                Key.Digit9 => (byte)'9',

                _ => 0,
            };
        }
    }
}
