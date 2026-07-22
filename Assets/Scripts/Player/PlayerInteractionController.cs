using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.UI;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.Player
{
    public class PlayerInteractionController : MonoBehaviour
    {
        private const string TAG = "[PlayerInteraction]";
        private Camera _mainCamera;

        protected void Awake()
        {
            _mainCamera = Camera.main;
        }

        protected void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera == null)
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

            if (PacketHandler.IsInputBlocked)
            {
                return;
            }

            if (ChatInput.IsFocused)
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -_mainCamera.transform.position.z));

                int unityX = Mathf.FloorToInt(worldPos.x);
                int unityY = Mathf.FloorToInt(worldPos.y);

                if (MapManager.Instance != null && NetworkService.Instance != null)
                {
                    ushort serverX = (ushort)Mathf.Clamp(unityX, 0, ushort.MaxValue);
                    ushort serverY = (ushort)Mathf.Clamp(MapManager.Instance.WorldHeight - 1 - unityY, 0, ushort.MaxValue);

                    NetworkService.Instance.SendAction(new ClickCellPacket(serverX, serverY));
                }
            }
        }

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (PacketHandler.IsInputBlocked)
            {
                return;
            }

            if (ChatInput.IsFocused)
            {
                return;
            }

            // This is a bit expensive but since it's for "unmapped" keys,
            // we might want to check all keys if they were pressed this frame.
            foreach (var keyControl in Keyboard.current.allKeys)
            {
                if (keyControl.wasPressedThisFrame)
                {
                    byte code = MapKeyToByte(keyControl.keyCode);
                    if (code != 0)
                    {
                        bool ctrl = Keyboard.current.ctrlKey.isPressed;
                        bool alt = Keyboard.current.altKey.isPressed;
                        bool shift = Keyboard.current.shiftKey.isPressed;

                        if (NetworkService.Instance != null)
                        {
                            NetworkService.Instance.SendAction(new UnmappedKeyPacket(code, ctrl, alt, shift));
                        }
                    }
                }
            }
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

                _ => 0
            };
        }
    }
}
