using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Client.Packets.Chat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class LocalChatPopup : MonoBehaviour
    {
        private static LocalChatPopup _instance;
        public static LocalChatPopup Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<LocalChatPopup>();
                }

                return _instance;
            }
        }

        private UIDocument _doc;
        private VisualElement _overlay;
        private TextField _inputField;
        private VisualElement _internalInput;
        private bool _isOpen = false;
        private Controls.ChatInputBlinker _blinker;
        private CancellationTokenSource _idleCts;

        protected void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        protected void OnDestroy()
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
        }

        protected void Start()
        {
            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                return;
            }

            CreateUI();
            _overlay.style.display = DisplayStyle.None;
        }

        private void CreateUI()
        {
            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = new Length(50, LengthUnit.Percent);
            _overlay.style.top = new Length(50, LengthUnit.Percent);
            _overlay.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _overlay.style.width = 400;
            _overlay.style.height = 36;
            _overlay.style.backgroundColor = new Color(0.06f, 0.06f, 0.06f, 0.92f);
            _overlay.style.borderTopWidth = 2;
            _overlay.style.borderBottomWidth = 2;
            _overlay.style.borderLeftWidth = 2;
            _overlay.style.borderRightWidth = 2;
            _overlay.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _overlay.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _overlay.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _overlay.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _overlay.style.paddingTop = 2;
            _overlay.style.paddingBottom = 2;
            _overlay.style.paddingLeft = 4;
            _overlay.style.paddingRight = 4;
            _overlay.style.flexDirection = FlexDirection.Row;
            _overlay.style.alignItems = Align.Center;

            var prompt = new Label(">");
            prompt.style.fontSize = 14;
            prompt.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            prompt.style.marginRight = 4;
            prompt.style.unityFontStyleAndWeight = FontStyle.Bold;
            _overlay.Add(prompt);

            _inputField = new TextField();
            _inputField.selectAllOnFocus = false;
            _inputField.selectAllOnMouseUp = false;
            _inputField.style.flexGrow = 1;
            _inputField.style.fontSize = 14;
            _inputField.style.color = Color.white;
            _inputField.style.backgroundColor = new Color(0, 0, 0, 0);
            _inputField.style.borderTopWidth = 0;
            _inputField.style.borderBottomWidth = 0;
            _inputField.style.borderLeftWidth = 0;
            _inputField.style.borderRightWidth = 0;
            _inputField.style.paddingTop = 0;
            _inputField.style.paddingBottom = 0;
            _inputField.style.paddingLeft = 0;
            _inputField.style.paddingRight = 0;
            _inputField.style.unityFontStyleAndWeight = FontStyle.Normal;
            _inputField.maxLength = ServerConfig.Instance.MaxLocalChatLength;
            _overlay.Add(_inputField);

            _inputField.RegisterCallback<FocusEvent>(_ =>
            {
                StartBlink();
                ChatInput.OnFocus();
            });
            _inputField.RegisterCallback<BlurEvent>(_ =>
            {
                StopBlink();
                ChatInput.OnBlur();
            });
            _inputField.RegisterValueChangedCallback(_ => OnInputChanged());

            _doc.rootVisualElement.Add(_overlay);

            _internalInput = _inputField.Q<VisualElement>(className: "unity-text-field__input");

            if (_internalInput != null)
            {
                _internalInput.style.backgroundColor = new Color(0, 0, 0, 0);
                _internalInput.style.borderTopWidth = 0;
                _internalInput.style.borderBottomWidth = 0;
                _internalInput.style.borderLeftWidth = 0;
                _internalInput.style.borderRightWidth = 0;
                _internalInput.style.paddingTop = 0;
                _internalInput.style.paddingBottom = 0;
                _internalInput.style.color = Color.white;
            }

            _blinker = new Controls.ChatInputBlinker(_inputField, _internalInput);
            var uss = Resources.Load<StyleSheet>("chat-input");
            if (uss != null)
            {
                _inputField.styleSheets.Add(uss);
            }
        }

        protected void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.tKey.wasPressedThisFrame && !_isOpen && !ChatInput.IsFocused)
            {
                Show();
                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                SendMessage();
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        private void SendMessage()
        {
            string text = _inputField.value.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                if (text.Length > ServerConfig.Instance.MaxLocalChatLength)
                {
                    text = text.Substring(0, ServerConfig.Instance.MaxLocalChatLength);
                }

                Networking.NetworkService.Send(new SendLocalChatMessagePacket(text));
            }

            Hide();
        }

        public void Show()
        {
            _isOpen = true;
            _overlay.style.display = DisplayStyle.Flex;
            _inputField.value = string.Empty;
            FocusAfterDelay().Forget();
        }

        private async UniTaskVoid FocusAfterDelay()
        {
            await UniTask.DelayFrame(1);
            _inputField.Focus();
        }

        public void Hide()
        {
            _isOpen = false;
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }

            if (_inputField != null)
            {
                _inputField.value = string.Empty;
                _inputField.Blur();
            }
        }

        private void StartBlink()
        {
            _blinker?.StartBlink();
        }

        private void StopBlink()
        {
            _blinker?.StopBlink();
            _idleCts?.Cancel();
        }

        private void OnInputChanged()
        {
            _blinker?.StopBlink();
            _idleCts?.Cancel();
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;
            DelayedStartBlink(token).Forget();
        }

        private async UniTaskVoid DelayedStartBlink(CancellationToken token)
        {
            await UniTask.Delay(500, cancellationToken: token);
            if (!token.IsCancellationRequested)
            {
                StartBlink();
            }
        }
    }
}
