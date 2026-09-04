#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.UI.HUD.Inventory.Model;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class GlobalChatUI : MonoBehaviour, ILocalizableUI
    {
        private enum ChatChannel
        {
            Global,
            Local,
        }

        [Inject]
        private UIDocument _doc = null!;
        private VisualElement? _tree;
        private VisualElement? _panel;
        private ScrollView? _scrollView;
        private Label? _muteStatus;
        private TextField? _inputField;
        private VisualElement? _internalInput;
        private Button? _sendButton;
        private Button? _colorButton;
        private Button? _globalChannelButton;
        private Button? _localChannelButton;
        private Label? _chatHeader;
        private VisualElement? _colorGrid;
        private System.Drawing.Color _currentColor = System.Drawing.Color.FromArgb(255, 200, 180, 100);
        private bool _isOpen = false;
        private static bool _invalidMuteExpiryLogged;
        private long _mutedUntilUnixMilliseconds = -1;
        private const int MAX_MESSAGES = 20;
        private Controls.ChatInputBlinker? _blinker;
        private CancellationTokenSource? _idleCts;
        private bool _initialized;
        private ChatChannel _activeChannel;
        private readonly List<string> _globalMessages = new();
        private readonly List<string> _localMessages = new();

        private static readonly System.Drawing.Color[] PresetColors =
        {
            System.Drawing.Color.White,
            System.Drawing.Color.FromArgb(255, 60, 60),
            System.Drawing.Color.FromArgb(60, 255, 60),
            System.Drawing.Color.FromArgb(60, 130, 255),
            System.Drawing.Color.FromArgb(255, 220, 60),
            System.Drawing.Color.FromArgb(60, 255, 255),
            System.Drawing.Color.FromArgb(255, 60, 255),
            System.Drawing.Color.FromArgb(255, 160, 60),
        };

        [Inject]
        private INetworkService _networkService = null!;

        [Inject]
        private IInputBlocker _inputBlocker = null!;

        [Inject]
        private InventoryModel _inventory = null!;

        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private ChatEventGateway _chatEvents = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        protected void Start()
        {
            // Школа (одна дорога): зарегистрированные вьюхи инжектятся при
            // сборке scope (фаза Awake), панель UIDocument создаётся в OnEnable —
            // к Start и зависимости, и панель гарантированы. Один вызов, без
            // ретраев из Update. Серверный конфиг приходит по сети — событие
            // OnInitialized ниже.
            TryInitialize();
        }

        [Inject]
        private void Construct(ChatEventGateway chatEvents)
        {
            _chatEvents = chatEvents;
            _chatEvents.MessageReceived += AddMessage;
            _chatEvents.LocalMessageReceived += AddLocalMessage;
            _chatEvents.MuteReceived += ApplyMute;
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            // [Inject]-метод гарантирует зависимости и панель UIDocument к
            // моменту вызова; null здесь — дефект проводки, а не гонка.
            // Молчаливый пропуск оставил бы чат вечно нерабочим без ошибки.
            if (_doc == null || _doc.rootVisualElement == null || _networkService == null ||
                _inputBlocker == null || _operations == null)
            {
                throw new InvalidOperationException(
                    "[GlobalChatUI] Required injection missing: " +
                    $"{(_doc == null ? "UIDocument" : _networkService == null ? "INetworkService" : _inputBlocker == null ? "IInputBlocker" : "UIDocument root")}. " +
                    "GlobalChatUI must be registered in the Game scope before Start.");
            }

            _initialized = true;
            ILocalizationService loc = _loc!;
            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            loc.RegisterLocalizable(this);
            CreateUI();
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.None;
            }

            ApplyChatConfig();

            if (Application.isPlaying)
            {
                try
                {
                    _networkService.Send(new QueryChatHistoryPacket("global", 0));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GlobalChatUI] Не удалось запросить историю чата: {ex}");
                }
            }
        }

        /// <summary>Переприменяет статические ключи UXML после смены языка.</summary>
        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(GlobalChatUI));
            if (_tree == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_tree, _loc);
            UILocalizer.AssertLocalized(_tree, _loc);
            UpdateChannelPresentation();
        }

        protected void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_chatEvents != null)
            {
                _chatEvents.MessageReceived -= AddMessage;
                _chatEvents.LocalMessageReceived -= AddLocalMessage;
                _chatEvents.MuteReceived -= ApplyMute;
            }

            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _blinker?.StopBlink();
            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void ApplyChatConfig()
        {
            if (_inputField != null)
            {
                _inputField.maxLength = ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
            }
        }

        protected void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            RefreshMuteState();

            bool inputBlocked = _inputBlocker != null && _inputBlocker.IsInputBlocked;

            if (!_isOpen)
            {
                if (Keyboard.current.tKey.wasPressedThisFrame && !inputBlocked)
                {
                    SelectChannel(ChatChannel.Local);
                    Show();
                    return;
                }

                // Enter открывает чат только если ввод не заблокирован системно
                // И не выбран предмет инвентаря: когда слот выбран, Enter применяет
                // предмет (InventoryView.Update), и чат не должен перехватывать
                // клавишу и красть фокус.
                if ((Keyboard.current.enterKey.wasPressedThisFrame ||
                     Keyboard.current.numpadEnterKey.wasPressedThisFrame) && !inputBlocked &&
                    (_inventory == null || !_inventory.HasSelectedItem))
                {
                    SelectChannel(ChatChannel.Global);
                    Show();
                }

                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                // IsInputBlocked теперь включает ChatInput.IsFocused, поэтому «не
                // заблокировано» или «печатаем в чате» — разрешаем отправку.
                if (!inputBlocked || ChatInput.IsFocused)
                {
                    OnSendClicked();
                }

                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        private void CreateUI()
        {
            var uiUxml = Resources.Load<VisualTreeAsset>("UI/GlobalChat");
            if (uiUxml != null)
            {
            VisualElement tree = uiUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            _tree = tree;

            // Статические ключи UXML резолвятся сразу при сборке (у чата их
            // почти нет, но контракт един для всех экранов).
            UILocalizer.Apply(tree, _loc);

            _panel = tree.Q<VisualElement>("ChatPanel");
                if (_panel != null)
                {
                    _panel.style.display = DisplayStyle.None;
                }

                _muteStatus = tree.Q<Label>("ChatMuteStatus");
                _scrollView = tree.Q<ScrollView>("ChatScroll");
                _inputField = tree.Q<TextField>("ChatInput");
                _chatHeader = tree.Q<Label>("ChatHeader");
                _globalChannelButton = tree.Q<Button>("GlobalChannelButton");
                _localChannelButton = tree.Q<Button>("LocalChannelButton");

                _sendButton = tree.Q<Button>("SendButton");
                _colorButton = tree.Q<Button>("ColorButton");
                _colorGrid = tree.Q<VisualElement>("ColorGrid");

                if (_doc != null && _panel != null)
                {
                    _doc.rootVisualElement.Add(tree);
                }

                if (_inputField != null)
                {
                    _inputField.selectAllOnFocus = false;
                    _inputField.selectAllOnMouseUp = false;
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
                }

                if (_sendButton != null)
                {
                    _sendButton.clicked += OnSendClicked;
                }

                if (_globalChannelButton != null)
                {
                    _globalChannelButton.clicked += () => SelectChannel(ChatChannel.Global);
                }

                if (_localChannelButton != null)
                {
                    _localChannelButton.clicked += () => SelectChannel(ChatChannel.Local);
                }

                if (_colorButton != null)
                {
                    _colorButton.clicked += ToggleColorGrid;
                    _colorButton.style.backgroundColor = new Color(_currentColor.R / 255f, _currentColor.G / 255f, _currentColor.B / 255f);
                }

                if (_colorGrid != null)
                {
                    foreach (var c in PresetColors)
                    {
                        var swatch = new Button(() => SelectColor(c));
                        swatch.AddToClassList("gchat-swatch");
                        swatch.style.backgroundColor = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                        _colorGrid.Add(swatch);
                    }
                }

                _internalInput = _inputField != null
                    ? _inputField.Q<VisualElement>(className: "unity-text-field__input")
                    : null;
                if (_internalInput != null)
                {
                    _internalInput.AddToClassList("gchat-internal-input");
                }

                if (_inputField != null && _internalInput != null)
                {
                    _blinker = new Controls.ChatInputBlinker(_inputField, _internalInput);
                }

                SelectChannel(ChatChannel.Global);
            }
        }

        private void OnSendClicked()
        {
            if (_inputField == null || IsMuted())
            {
                return;
            }

            string text = _inputField.value.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int chatMaxLen = _activeChannel == ChatChannel.Local
                ? ProjectRuntimeContracts.Chat.MaximumLocalChatLength
                : ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
            if (text.Length > chatMaxLen)
            {
                text = text.Substring(0, chatMaxLen);
            }

            try
            {
                if (_activeChannel == ChatChannel.Local)
                {
                    _networkService.Send(new SendLocalChatMessagePacket(text));
                }
                else
                {
                    _networkService.Send(new SendChatMessagePacket("global", text));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GlobalChatUI] Не удалось отправить сообщение в чат: {ex}");
            }

            _inputField.value = string.Empty;
            _inputField.Focus();
        }

        public void Toggle()
        {
            if (_isOpen)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Show()
        {
            _isOpen = true;
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.Flex;
            }

            _inputField?.Focus();
        }

        public void Hide()
        {
            _isOpen = false;
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.None;
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
            _idleCts?.Dispose();
            _idleCts = new CancellationTokenSource();
            CancellationToken idleToken = _idleCts.Token;
            _operations.Run(
                "global_chat_blink_delay",
                supervisorToken => DelayedStartBlink(idleToken, supervisorToken));
        }

        private async UniTask DelayedStartBlink(
            CancellationToken idleToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                idleToken,
                supervisorToken,
                destroyCancellationToken);
            CancellationToken cancellationToken = linkedCancellation.Token;
            bool canceled = await UniTask.Delay(
                500,
                cancellationToken: cancellationToken).SuppressCancellationThrow();
            if (!canceled && !cancellationToken.IsCancellationRequested)
            {
                StartBlink();
            }
        }

        private readonly System.Text.StringBuilder _msgBuilder = new(128);

        public void AddMessage(ChatMessagePacket msg)
        {
            if (_scrollView == null)
            {
                return;
            }

            DateTime now = DateTime.Now;
            _msgBuilder.Clear();
            _msgBuilder.Append("<color=#888888>[");
            _msgBuilder.Append(now.Hour.ToString("D2"));
            _msgBuilder.Append(':');
            _msgBuilder.Append(now.Minute.ToString("D2"));
            _msgBuilder.Append("]</color> <color=#");
            _msgBuilder.Append(msg.NicknameColor.R.ToString("X2"));
            _msgBuilder.Append(msg.NicknameColor.G.ToString("X2"));
            _msgBuilder.Append(msg.NicknameColor.B.ToString("X2"));
            _msgBuilder.Append('>');
            _msgBuilder.Append(msg.PlayerName);
            _msgBuilder.Append("</color>: <color=#");
            _msgBuilder.Append(msg.MessageColor.R.ToString("X2"));
            _msgBuilder.Append(msg.MessageColor.G.ToString("X2"));
            _msgBuilder.Append(msg.MessageColor.B.ToString("X2"));
            _msgBuilder.Append('>');
            _msgBuilder.Append(msg.Message);
            _msgBuilder.Append("</color>");

            AppendMessage(ChatChannel.Global, _msgBuilder.ToString());
        }

        private void AddLocalMessage(LocalChatMessagePacket packet)
        {
            DateTime now = DateTime.Now;
            _msgBuilder.Clear();
            _msgBuilder.Append("<color=#888888>[");
            _msgBuilder.Append(now.Hour.ToString("D2"));
            _msgBuilder.Append(':');
            _msgBuilder.Append(now.Minute.ToString("D2"));
            _msgBuilder.Append("]</color> <color=#B2A680>");
            _msgBuilder.Append(_loc.Get("chat.local.sender", packet.BotId));
            _msgBuilder.Append("</color>: ");
            _msgBuilder.Append(packet.Text);
            AppendMessage(ChatChannel.Local, _msgBuilder.ToString());
        }

        private void AppendMessage(ChatChannel channel, string formattedMessage)
        {
            List<string> messages = channel == ChatChannel.Local
                ? _localMessages
                : _globalMessages;
            messages.Add(formattedMessage);
            while (messages.Count > MAX_MESSAGES)
            {
                messages.RemoveAt(0);
            }

            if (_activeChannel == channel)
            {
                AppendVisibleMessage(formattedMessage);
            }
        }

        private void AppendVisibleMessage(string formattedMessage)
        {
            if (_scrollView == null)
            {
                return;
            }

            var label = new Label(formattedMessage);
            label.AddToClassList("gchat-message");
            _scrollView.Add(label);
            while (_scrollView.childCount > MAX_MESSAGES)
            {
                _scrollView.RemoveAt(0);
            }

            _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private void SelectChannel(ChatChannel channel)
        {
            _activeChannel = channel;
            if (_inputField != null)
            {
                _inputField.maxLength = channel == ChatChannel.Local
                    ? ProjectRuntimeContracts.Chat.MaximumLocalChatLength
                    : ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
            }

            UpdateChannelPresentation();
            RenderActiveMessages();
        }

        private void UpdateChannelPresentation()
        {
            bool local = _activeChannel == ChatChannel.Local;
            if (_chatHeader != null && _loc != null)
            {
                _chatHeader.text = _loc.Get(
                    local ? "chat.channel.local" : "chat.channel.global");
            }

            _globalChannelButton?.EnableInClassList(
                "gchat-channel-button--active",
                !local);
            _localChannelButton?.EnableInClassList(
                "gchat-channel-button--active",
                local);
            if (_colorButton != null)
            {
                _colorButton.style.display = local
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (local && _colorGrid != null)
            {
                _colorGrid.style.display = DisplayStyle.None;
            }
        }

        private void RenderActiveMessages()
        {
            if (_scrollView == null)
            {
                return;
            }

            _scrollView.Clear();
            List<string> messages = _activeChannel == ChatChannel.Local
                ? _localMessages
                : _globalMessages;
            foreach (string message in messages)
            {
                AppendVisibleMessage(message);
            }
        }

        public void ApplyMute(ChatMutePacket packet)
        {
            _mutedUntilUnixMilliseconds = packet.EndsAt;
            string reason = string.IsNullOrWhiteSpace(packet.Reason)
                ? _loc.Get("chat.mute.no_reason")
                : packet.Reason.Trim();
            string moderator = string.IsNullOrWhiteSpace(packet.ModeratorName)
                ? _loc.Get("chat.mute.by_server")
                : packet.ModeratorName.Trim();
            string duration = packet.EndsAt <= 0
                ? _loc.Get("chat.mute.forever")
                : _loc.Get("chat.mute.until", FormatMuteEnd(packet.EndsAt));
            SetMuteStatus(_loc.Get("chat.mute.blocked", moderator, reason, duration));
            RefreshMuteState();
            AddSystemMessage(_loc.Get("chat.mute.received"));
        }

        private bool IsMuted()
        {
            if (_mutedUntilUnixMilliseconds == -1)
            {
                return false;
            }

            return _mutedUntilUnixMilliseconds == 0 ||
                _mutedUntilUnixMilliseconds > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void RefreshMuteState()
        {
            if (_mutedUntilUnixMilliseconds == -1)
            {
                return;
            }

            bool muted = IsMuted();
            if (!muted && _mutedUntilUnixMilliseconds > 0)
            {
                _mutedUntilUnixMilliseconds = -1;
                SetMuteStatus(string.Empty);
            }

            _inputField?.SetEnabled(!muted);
            _sendButton?.SetEnabled(!muted);
            _colorButton?.SetEnabled(!muted);
        }

        private void SetMuteStatus(string message)
        {
            if (_muteStatus == null)
            {
                return;
            }

            _muteStatus.text = message;
            _muteStatus.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void AddSystemMessage(string message)
        {
            AppendMessage(ChatChannel.Global, message);
        }

        private static string FormatMuteEnd(long unixMilliseconds)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                    .ToLocalTime()
                    .ToString("g");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Серверный ввод не должен ронять клиент: битый timestamp в пакете
                // мута — это данные, а не контрактная ошибка. Отображаем как есть.
                if (_invalidMuteExpiryLogged)
                {
                    return unixMilliseconds.ToString();
                }

                _invalidMuteExpiryLogged = true;
                Debug.LogWarning(
                    $"[GlobalChat] Mute packet contains invalid expiry timestamp: {unixMilliseconds}");
                return unixMilliseconds.ToString();
            }
        }

        private void ToggleColorGrid()
        {
            if (_colorGrid != null)
            {
                _colorGrid.style.display = _colorGrid.style.display == DisplayStyle.None
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void SelectColor(System.Drawing.Color color)
        {
            _currentColor = color;
            if (_colorButton != null)
            {
                _colorButton.style.backgroundColor = new Color(color.R / 255f, color.G / 255f, color.B / 255f);
            }

            if (_colorGrid != null)
            {
                _colorGrid.style.display = DisplayStyle.None;
            }

            try
            {
                _networkService.Send(new ChangeChatColorPacket(color));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GlobalChatUI] Не удалось отправить изменение цвета чата: {ex}");
            }
        }
    }
}
