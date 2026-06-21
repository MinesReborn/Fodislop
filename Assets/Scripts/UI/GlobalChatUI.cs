using System;
using System.Collections.Generic;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class GlobalChatUI : MonoBehaviour
    {
        private static GlobalChatUI _instance;
        public static GlobalChatUI Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<GlobalChatUI>();
                return _instance;
            }
        }

        private UIDocument _doc;
        private VisualElement _panel;
        private ScrollView _scrollView;
        private TextField _inputField;
        private Button _sendButton;
        private bool _isOpen = false;
        private const int MAX_MESSAGES = 20;
        private bool _wasTabBlocked = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            _doc = FindObjectOfType<UIDocument>();
            if (_doc == null) return;
            CreateUI();
            _panel.style.display = DisplayStyle.None;
        }

        void Update()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                if (_isOpen || !ChatInput.IsFocused)
                    Toggle();
                return;
            }

            if (!_isOpen) return;

            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                OnSendClicked();
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _inputField.value = "";
                _inputField.Blur();
                Hide();
            }
        }

        private void CreateUI()
        {
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.right = 10;
            _panel.style.top = new Length(50, LengthUnit.Percent);
            _panel.style.translate = new Translate(0, new Length(-50, LengthUnit.Percent));
            _panel.style.width = 340;
            _panel.style.height = 420;
            _panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
            _panel.style.borderTopWidth = 2;
            _panel.style.borderBottomWidth = 2;
            _panel.style.borderLeftWidth = 2;
            _panel.style.borderRightWidth = 2;
            _panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.paddingLeft = 8;
            _panel.style.paddingRight = 8;
            _panel.style.flexDirection = FlexDirection.Column;

            var header = new Label("Глобальный чат");
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            header.style.marginBottom = 4;
            _panel.Add(header);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.marginBottom = 6;
            _scrollView.style.backgroundColor = new Color(0, 0, 0, 0.3f);
            _scrollView.style.borderTopWidth = 1;
            _scrollView.style.borderBottomWidth = 1;
            _scrollView.style.borderLeftWidth = 1;
            _scrollView.style.borderRightWidth = 1;
            _scrollView.style.borderTopColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _scrollView.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _scrollView.style.borderLeftColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _scrollView.style.borderRightColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _panel.Add(_scrollView);

            var bottomRow = new VisualElement();
            bottomRow.style.flexDirection = FlexDirection.Row;
            bottomRow.style.height = 28;
            bottomRow.style.height = 28;
            bottomRow.style.flexShrink = 0;
            bottomRow.style.flexGrow = 0;

            _inputField = new TextField();
            _inputField.style.flexGrow = 1;
            _inputField.style.fontSize = 12;
            _inputField.style.color = Color.white;
            _inputField.style.backgroundColor = new Color(0, 0, 0, 0);
            _inputField.style.borderTopWidth = 1;
            _inputField.style.borderBottomWidth = 1;
            _inputField.style.borderLeftWidth = 1;
            _inputField.style.borderRightWidth = 1;
            _inputField.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _inputField.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _inputField.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _inputField.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _inputField.style.paddingTop = 2;
            _inputField.style.paddingBottom = 2;
            _inputField.style.paddingLeft = 4;
            _inputField.style.paddingRight = 4;
            _inputField.style.flexShrink = 1;
            _inputField.style.minWidth = 0;
            _inputField.maxLength = ServerConfig.Instance.MaxGlobalChatLength;
            bottomRow.Add(_inputField);

            _inputField.RegisterCallback<FocusEvent>(_ => ChatInput.OnFocus());
            _inputField.RegisterCallback<BlurEvent>(_ => ChatInput.OnBlur());

            _sendButton = new Button(OnSendClicked);
            _sendButton.text = ">";
            _sendButton.style.width = 32;
            _sendButton.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _sendButton.style.color = Color.white;
            _sendButton.style.fontSize = 14;
            _sendButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _sendButton.style.paddingTop = 0;
            _sendButton.style.flexShrink = 0;
            _sendButton.style.paddingBottom = 0;
            _sendButton.style.paddingLeft = 0;
            _sendButton.style.paddingRight = 0;
            _sendButton.style.borderTopWidth = 1;
            _sendButton.style.borderBottomWidth = 1;
            _sendButton.style.borderLeftWidth = 0;
            _sendButton.style.borderRightWidth = 1;
            _sendButton.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _sendButton.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            _sendButton.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            bottomRow.Add(_sendButton);

            _panel.Add(bottomRow);

            _doc.rootVisualElement.Add(_panel);

            var internalInput = _inputField.Q<VisualElement>(className: "unity-text-field__input");

            if (internalInput != null)
            {
                internalInput.style.backgroundColor = new Color(0, 0, 0, 0);
                internalInput.style.borderTopWidth = 0;
                internalInput.style.borderBottomWidth = 0;
                internalInput.style.borderLeftWidth = 0;
                internalInput.style.borderRightWidth = 0;

                internalInput.style.paddingTop = 0;
                internalInput.style.paddingBottom = 0;
                internalInput.style.marginTop = 0;
                internalInput.style.marginBottom = 0;

                internalInput.style.fontSize = 14;
                internalInput.style.color = Color.white;
            }

            var uss = Resources.Load<StyleSheet>("chat-input");
            if (uss != null)
            {
                _panel.styleSheets.Add(uss);
            }
        }

        private void OnSendClicked()
        {
            string text = _inputField.value.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (text.Length > ServerConfig.Instance.MaxGlobalChatLength)
                text = text.Substring(0, ServerConfig.Instance.MaxGlobalChatLength);

            var ns = Networking.NetworkService.Instance;
            ns.Send(new MinesServer.Networking.Client.Packets.Chat.SendChatMessagePacket("global", text));

            _inputField.value = "";
            _inputField.Focus();
        }

        public void Toggle()
        {
            if (_isOpen) Hide();
            else Show();
        }

        public void Show()
        {
            _isOpen = true;
            _panel.style.display = DisplayStyle.Flex;
            _inputField.Focus();
        }

        public void Hide()
        {
            _isOpen = false;
            _panel.style.display = DisplayStyle.None;
            _inputField.value = "";
            _inputField.Blur();
        }

        public void AddMessage(ChatMessagePacket msg)
        {
            var time = DateTime.Now.ToString("HH:mm");
            var nickHex = $"#{msg.NicknameColor.R:X2}{msg.NicknameColor.G:X2}{msg.NicknameColor.B:X2}";
            var msgHex = $"#{msg.MessageColor.R:X2}{msg.MessageColor.G:X2}{msg.MessageColor.B:X2}";

            var label = new Label($"<color=#888888>[{time}]</color> <color={nickHex}>{msg.PlayerName}</color>: <color={msgHex}>{msg.Message}</color>");
            label.style.fontSize = 12;
            label.style.color = Color.white;
            label.style.marginBottom = 2;
            label.style.paddingLeft = 2;
            label.style.paddingRight = 2;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.overflow = Overflow.Hidden;
            label.style.unityFontStyleAndWeight = FontStyle.Normal;

            _scrollView.Add(label);

            while (_scrollView.childCount > MAX_MESSAGES)
                _scrollView.RemoveAt(0);

            _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
        }
    }
}
