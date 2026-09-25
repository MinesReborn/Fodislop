#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Game.Managers;
using Kern.Networking;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class GlobalChatUI : MonoBehaviour, ILocalizableUI
    {
        [Inject] private UIDocument _doc = null!;
        [Inject] private INetworkService _networkService = null!;
        [Inject] private IInputBlocker _inputBlocker = null!;
        [Inject] private UIInputManager _uiInput = null!;
        [Inject] private ILocalizationService _loc = null!;
        [Inject] private IAsyncOperationSupervisor _operations = null!;

        private ChatEventGateway _chatEvents = null!;
        private ChatViewElements? _view;
        private ChatColorController? _colorController;
        private ChatBlinkController? _blink;
        private bool _isOpen;
        private bool _initialized;
        private readonly ChatMessageHistory _history = new();
        private readonly ChatMuteTracker _muteTracker = new();
        private bool _lastMutedState;
        private bool _hasCachedMuteState;
        private bool _hasServerChatList;
        private bool _hasGlobalChannel;

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
            _chatEvents.HistoryReceived += AddHistory;
            _chatEvents.MuteReceived += ApplyMute;
            _chatEvents.ChatListReceived += ApplyChatList;
            if (_chatEvents.LastChatList is ChatListPacket lastChatList)
            {
                ApplyChatList(lastChatList);
            }
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
            _blink = new ChatBlinkController(_operations, destroyCancellationToken, () => _view?.Blinker);
            if (_view?.Panel != null)
            {
                _view.Panel.style.display = DisplayStyle.None;
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

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(GlobalChatUI));
            if (_view?.Tree == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_view.Tree, _loc);
            UILocalizer.AssertLocalized(_view.Tree, _loc);
            ChatChannelPresenter.UpdatePresentation(
                _view.ChatHeader,
                _view.GlobalChannelButton,
                _loc);
        }

        protected void OnDestroy()
        {
            if (_uiInput != null)
            {
                _uiInput.IsChatFocused = false;
            }

            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_chatEvents != null)
            {
                _chatEvents.MessageReceived -= AddMessage;
                _chatEvents.HistoryReceived -= AddHistory;
                _chatEvents.MuteReceived -= ApplyMute;
                _chatEvents.ChatListReceived -= ApplyChatList;
            }

            _blink?.Dispose();
            _blink = null;
            _view?.Tree.RemoveFromHierarchy();
            _view = null;
            _colorController = null;
            _hasCachedMuteState = false;
        }

        private void ApplyChatConfig()
        {
            if (_view?.InputField != null)
            {
                _view.InputField.maxLength = ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
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
                    SelectGlobalChannel();
                    Show();
                }

                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                // Input blocking includes chat focus, but Enter must remain
                // available to the chat that currently owns keyboard focus.
                if (!inputBlocked || _uiInput.IsChatFocused)
                {
                    OnSendClicked();
                }

                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _uiInput.ConsumeEscape();
                Hide();
            }
        }

        private void CreateUI()
        {
            var uiUxml = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.GlobalChatUxml);
            if (uiUxml == null)
            {
                return;
            }

            VisualElement tree = uiUxml.CloneTree();
            UILocalizer.Apply(tree, _loc);
            _view = new ChatViewElements(tree, _loc);

            if (_doc != null && _view.Panel != null)
            {
                _doc.rootVisualElement.Add(tree);
            }

            if (_view.InputField != null)
            {
                _view.InputField.selectAllOnFocus = false;
                _view.InputField.selectAllOnMouseUp = false;
                _view.InputField.RegisterCallback<FocusEvent>(_ =>
                {
                    _blink?.StartBlink();
                    _uiInput.IsChatFocused = true;
                });
                _view.InputField.RegisterCallback<BlurEvent>(_ =>
                {
                    _blink?.StopBlink();
                    _uiInput.IsChatFocused = false;
                });
                _view.InputField.RegisterValueChangedCallback(_ => _blink?.NotifyInput());
            }

            _view.BindActions(
                OnSendClicked,
                SelectGlobalChannel);

            _colorController = new ChatColorController(_networkService, _view.ColorButton, _view.ColorGrid);
            SelectGlobalChannel();
        }

        private void OnSendClicked()
        {
            if (_view?.InputField == null || _muteTracker.IsMuted)
            {
                return;
            }

            if (!_hasGlobalChannel)
            {
                return;
            }

            string text = _view.InputField.value.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int chatMaxLen = ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
            if (text.Length > chatMaxLen)
            {
                text = text.Substring(0, chatMaxLen);
            }

            try
            {
                _networkService.Send(new SendChatMessagePacket("global", text));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GlobalChatUI] Не удалось отправить сообщение в чат: {ex}");
            }

            _view.InputField.value = string.Empty;
            _view.InputField.Focus();
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
            if (_view?.Panel != null)
            {
                _view.Panel.style.display = DisplayStyle.Flex;
            }

            _view?.InputField?.Focus();
        }

        public void Hide()
        {
            _isOpen = false;
            if (_view?.Panel != null)
            {
                _view.Panel.style.display = DisplayStyle.None;
            }

            if (_view?.InputField != null)
            {
                _view.InputField.value = string.Empty;
                _view.InputField.Blur();
            }
        }

        public void AddMessage(ChatMessagePacket msg)
        {
            if (_view?.ScrollView == null)
            {
                return;
            }

            AppendMessage(ChatMessageFormatter.FormatGlobal(msg, DateTime.Now));
        }

        private void AddHistory(ChatMessageListPacket packet)
        {
            if (!string.Equals(packet.Tag, "global", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[GlobalChatUI] Ignoring history for unsupported channel '{packet.Tag}'.");
                return;
            }

            foreach (ChatMessagePacket message in packet.Messages)
            {
                AppendMessage(ChatMessageFormatter.FormatGlobal(message, DateTime.Now));
            }
        }

        // Локальные сообщения выводит FloatingChatManager над роботом; это окно
        // принимает только global-сообщения и global-историю.

        private void AppendMessage(string formattedMessage)
        {
            _history.Add(formattedMessage);
            AppendVisibleMessage(formattedMessage);
        }

        private void AppendVisibleMessage(string formattedMessage)
        {
            if (_view?.ScrollView == null)
            {
                return;
            }

            var label = new Label(formattedMessage);
            label.AddToClassList("gchat-message");
            _view.ScrollView.Add(label);
            while (_view.ScrollView.childCount > ChatMessageHistory.MaxMessages)
            {
                _view.ScrollView.RemoveAt(0);
            }

            _view.ScrollView.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private void SelectGlobalChannel()
        {
            if (_view?.InputField != null)
            {
                _view.InputField.maxLength = ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
            }

            ChatChannelPresenter.UpdatePresentation(
                _view?.ChatHeader,
                _view?.GlobalChannelButton,
                _loc);
            RefreshChatControls(_muteTracker.IsMuted);
            RenderActiveMessages();
        }

        private void ApplyChatList(ChatListPacket packet)
        {
            _hasServerChatList = true;
            _hasGlobalChannel = false;
            foreach (var (tag, _, _) in packet.Chats)
            {
                if (string.Equals(tag, "global", StringComparison.OrdinalIgnoreCase))
                {
                    _hasGlobalChannel = true;
                    break;
                }
            }

            RefreshChatControls(_muteTracker.IsMuted);
        }

        private void RenderActiveMessages()
        {
            if (_view?.ScrollView == null)
            {
                return;
            }

            _view.ScrollView.Clear();
            var messages = _history.GetMessages();
            for (int i = 0; i < messages.Count; i++)
            {
                AppendVisibleMessage(messages[i]);
            }
        }

        public void ApplyMute(ChatMutePacket packet)
        {
            _muteTracker.ApplyMute(packet, _loc, out string statusMessage, out string notificationMessage);
            _view?.SetMuteStatus(statusMessage);
            RefreshMuteState();
            AddSystemMessage(notificationMessage);
        }

        private void RefreshMuteState()
        {
            if (_muteTracker.CheckExpiration())
            {
                _view?.SetMuteStatus(string.Empty);
            }

            bool muted = _muteTracker.IsMuted;

            // Кэш переживал вид, который он и защищал.
            //
            // RefreshMuteState вызывается из Update, то есть и тогда, когда чат
            // закрыт и _view равен null: первый же такой вызов запоминал
            // «немой = нет» и глушил все последующие. Дальше чат открывался,
            // элементы создавались заново — и SetEnabled к ним не применялся уже
            // никогда, потому что состояние немоты с тех пор не менялось. Поле
            // ввода оставалось в том виде, в каком его собрал Uxml.
            //
            // Сбрасывается вместе с видом: сравнивать состояние можно только с
            // тем, кому его в самом деле выставили.
            if (_view == null)
            {
                _hasCachedMuteState = false;
                return;
            }

            bool muteChanged = !_hasCachedMuteState || _lastMutedState != muted;
            _lastMutedState = muted;
            _hasCachedMuteState = true;
            if (muteChanged)
            {
                RefreshChatControls(muted);
            }
        }

        private void RefreshChatControls(bool muted)
        {
            if (_view == null)
            {
                return;
            }

            bool globalAvailable = _hasServerChatList && _hasGlobalChannel;
            bool activeChannelAvailable = globalAvailable;
            _view.InputField?.SetEnabled(!muted);
            _view.SendButton?.SetEnabled(!muted && activeChannelAvailable);
            _view.ColorButton?.SetEnabled(!muted && globalAvailable);
            _view.GlobalChannelButton?.SetEnabled(globalAvailable);
        }

        private void AddSystemMessage(string message) => AppendMessage(message);
    }
}
