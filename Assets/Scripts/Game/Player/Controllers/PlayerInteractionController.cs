#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.Networking;
using Kern.Player.Logic;
using Kern.World;
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

                if (_mapManager.WorldWidth <= 0 || _mapManager.WorldHeight <= 0)
                {
                    return;
                }

                Vector2Int serverPosition;
                try
                {
                    serverPosition = CoordinateUtils.UnityToServerPos(worldPos, _mapManager.WorldHeight);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return;
                }

                if (!PlayerMovementController.IsWithinWorldBounds(
                        serverPosition,
                        _mapManager.WorldWidth,
                        _mapManager.WorldHeight))
                {
                    return;
                }

                _networkService.SendAction(new ClickCellPacket(
                    (ushort)serverPosition.x,
                    (ushort)serverPosition.y));
            }
        }

        // Клик по миру шлётся только если под указателем нет видимого интерфейса.
        private bool IsPointerOverUI(Vector2 mousePos) =>
            Kern.Player.Input.UIPointerHitTest.IsOverUI(_injectedUIDoc, mousePos);

        private void HandleKeyboardInput()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (_inputBlocker == null || _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (!keyboard.anyKey.wasPressedThisFrame)
            {
                return;
            }

            // Send unmapped keys to the server, excluding locally handled gameplay and UI hotkeys.
            var allKeys = keyboard.allKeys;
            for (int i = 0; i < allKeys.Count; i++)
            {
                KeyControl keyControl = allKeys[i];
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
                        bool ctrl = keyboard.ctrlKey.isPressed;
                        bool alt = keyboard.altKey.isPressed;
                        bool shift = keyboard.shiftKey.isPressed;

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
