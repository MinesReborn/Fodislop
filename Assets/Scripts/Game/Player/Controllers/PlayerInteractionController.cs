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

        // ─── Блок клика в мир, когда ЛКМ съел интерфейс ───
        // Ручной пересчёт координат (ScreenToPanel + Pick) расходится с
        // фактической раскладкой панели, а hover-трекинг залипает над пустым
        // миром (PointerMove туда не приходит). Поэтому нажатие ЛКМ
        // запоминается, а его судьба решается через пару кадров: к этому
        // моменту диспетчер UI гарантированно раздал PointerDown, и простое
        // сравнение кадров точно говорит, съел ли интерфейс этот клик.
        private VisualElement? _uiEventRoot;
        private int _uiPointerDownFrame = -1;
        private bool _hasPendingClick;
        private Vector2 _pendingClickPos;
        private int _pendingClickFrame = -1;

        // Нажатие досталось реальному элементу интерфейса? Диспетчер честно
        // находит верхний пикабельный элемент, но среди них встречаются
        // невидимые слои на весь экран (подписи мира, слои чата) - они не
        // рисуются и не интерактивны, клик обязан проходить сквозь них в мир.
        // Поэтому UI - это интерактивный контрол или элемент с отрисовкой.
        private bool IsEventTargetUI(object? target)
        {
            if (target is not VisualElement element ||
                element == _uiEventRoot ||
                element is TemplateContainer)
            {
                return false;
            }

            if (element is Button)
            {
                return true;
            }

            IResolvedStyle style = element.resolvedStyle;
            return style.backgroundColor.a > 0f ||
                   style.backgroundImage.texture != null ||
                   style.backgroundImage.sprite != null ||
                   style.backgroundImage.vectorImage != null ||
                   (element is Image image && (image.image != null || image.sprite != null || image.vectorImage != null)) ||
                   (element is TextElement text && !string.IsNullOrEmpty(text.text));
        }

        private void OnPanelPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || !IsEventTargetUI(evt.target))
            {
                return;
            }

            _uiPointerDownFrame = Time.frameCount;
        }

        // UIDocument может быть пересоздан (выключение/включение), поэтому
        // подписка держится актуальной: перезакрепляемся при смене корня.
        private void EnsureUIEventSubscription()
        {
            VisualElement? root = _injectedUIDoc != null && _injectedUIDoc.isActiveAndEnabled
                ? _injectedUIDoc.rootVisualElement
                : null;
            if (root == _uiEventRoot)
            {
                return;
            }

            if (_uiEventRoot != null)
            {
                _uiEventRoot.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);
                _uiEventRoot = null;
            }

            if (root != null)
            {
                _uiEventRoot = root;
                root.RegisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);
            }
        }

        protected void Update()
        {
            EnsureUIEventSubscription();

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

            // Карта — оверлей: клик-маршруты и действия работают и при ней.
            // Клик над самой картой съедает ClickGuard (PointerDown над UI).
            if (_inputBlocker == null || _inputBlocker.IsInputBlockedExcludingMapMode)
            {
                _hasPendingClick = false;
                return;
            }

            // Нажатие ЛКМ запоминаем и решаем его судьбу парой кадров позже:
            // к этому моменту диспетчер UI гарантированно раздал PointerDown по
            // собственным координатам, и совпадение кадров нажатия точно
            // говорит, съел ли интерфейс именно этот клик. Ручной пересчёт
            // координат не используется - он расходится с раскладкой панели.
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                _pendingClickPos = Mouse.current.position.ReadValue();
                _pendingClickFrame = Time.frameCount;
                _hasPendingClick = true;
                return;
            }

            if (!_hasPendingClick || Time.frameCount <= _pendingClickFrame + 1)
            {
                return;
            }

            _hasPendingClick = false;

            // Кадр нажатия совпал с кадром PointerDown над интерфейсом:
            // клик принадлежит кнопке/панели, миру он не достаётся.
            if (_uiPointerDownFrame == _pendingClickFrame)
            {
                // Клик принадлежит кнопке/панели, миру он не достаётся.
                return;
            }

            Vector2 mousePos = _pendingClickPos;
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

            // ЛКМ по миру: строим клик-маршрут и ведём робота до цели
            // (пунктирная линия рисуется рендерером маршрута).
            if (_localPlayer.Current is IClickPathWalker walker)
            {
                walker.TryStartPath(serverPosition);
            }
        }

        // Защита «клик по UI не должен проваливаться в мир» переехала на
        // PointerDown-маркер диспетчера UI Toolkit (см. OnPanelPointerDown):
        // ручной пересчёт ScreenToPanel/Pick расходился с фактической
        // раскладкой панели и промахивался мимо кнопок.

        private void HandleKeyboardInput()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (_inputBlocker == null || _inputBlocker.IsInputBlockedExcludingMapMode)
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
