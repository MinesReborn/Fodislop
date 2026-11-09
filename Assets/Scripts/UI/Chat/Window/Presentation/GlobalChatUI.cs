#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

using Kern.AssetPipeline.Loading;
using Kern.Networking.Gateways;
using Kern.UI.Input;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Chat
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "GlobalChatUI")]
    public class GlobalChatUI : MonoBehaviour, ILocalizableUI
    {
        [Inject] private UIDocument _doc = null!;
        [Inject] private INetworkService _networkService = null!;
        [Inject] private IInputBlocker _inputBlocker = null!;
        [Inject] private UIInputManager _uiInput = null!;
        [Inject] private ILocalizationService _loc = null!;
        [Inject] private IAsyncOperationSupervisor _operations = null!;
        [Inject] private IClientConfigManager _clientConfig = null!;

        private ChatEventGateway _chatEvents = null!;
        private ChatViewElements? _view;
        private ChatBlinkController? _blink;

        private GlobalChatVisibilityController _visibility = null!;
        private bool _initialized;
        private TouchCommands? _touchCommands;
        private readonly GlobalChatMessagePresenter _messages = new();
        private readonly ChatMuteTracker _muteTracker = new();
        private GlobalChatMutePresenter _mutePresenter = null!;
        // Активный чат: тег маршрутизации (SendChatMessagePacket/история) и
        // серверное имя для заголовка. Дефолт — глобальный канал (FED).
        // Список чатов: сервер объявляет каналы на входе (pSenders.SendChatList)
        // и присылает сводный список по кнопке «Чаты». Если список не пришёл ни
        // разу — доверяем контракту (FED). Попап перерисовывается только по
        // явному запросу (_chatListRequested), входное объявление его не открывает.
        private GlobalChatChannelPresenter _channels = null!;

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
        private void Construct(
            ChatEventGateway chatEvents,
            TouchCommands touchCommands)
        {
            // Кнопка «Чат» HUD — тач-аналог TAB.
            _touchCommands = touchCommands;
            _touchCommands.GlobalChatRequested += OnTouchChatRequested;
            _chatEvents = chatEvents;
            _visibility = new GlobalChatVisibilityController(
                _chatEvents,
                _doc,
                _messages,
                () => _view,
                () => _channels.ActiveTag,
                () => _channels.CloseChatListPopup());
            _channels = new GlobalChatChannelPresenter(
                _chatEvents,
                _networkService,
                _loc,
                _messages,
                () => _visibility.IsOpen,
                muted => _mutePresenter.RefreshControls(muted),
                () => _muteTracker.IsMuted);
            _mutePresenter = new GlobalChatMutePresenter(
                _muteTracker,
                _loc,
                _channels,
                AddSystemMessage);
            _chatEvents.MessageReceived += AddMessage;
            _chatEvents.HistoryReceived += AddHistory;
            _chatEvents.MuteReceived += _mutePresenter.ApplyMute;
            _chatEvents.ChatListReceived += _channels.ApplyChatList;
            _chatEvents.UnreadChanged += _channels.RefreshUnread;
            if (_chatEvents.LastChatList is ChatListPacket lastChatList)
            {
                _channels.ApplyChatList(lastChatList);
            }
        }

        // Кнопка «Чат» HUD — тач-аналог TAB, с теми же правилами.
        private void OnTouchChatRequested()
        {
            if (!_initialized)
            {
                return;
            }

            bool inputBlocked = _inputBlocker != null && _inputBlocker.IsInputBlocked;
            if (!_visibility.IsOpen || !inputBlocked || _uiInput.IsChatFocused)
            {
                _visibility.ToggleOpen();
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
                _inputBlocker == null || _operations == null || _clientConfig == null)
            {
                throw new InvalidOperationException(
                    "[GlobalChatUI] Required injection missing: " +
                    $"{(_doc == null ? "UIDocument"
                        : _doc.rootVisualElement == null ? "UIDocument root"
                        : _networkService == null ? "INetworkService"
                        : _inputBlocker == null ? "IInputBlocker"
                        : _clientConfig == null ? "IClientConfigManager"
                        : "IAsyncOperationSupervisor")}. " +
                    "GlobalChatUI must be registered in the Game scope before Start.");
            }

            _initialized = true;
            ILocalizationService loc = _loc!;

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            loc.RegisterLocalizable(this);
            CreateUI();
            _blink = new ChatBlinkController(_operations, destroyCancellationToken, () => _view?.Blinker);
            _visibility.Set(GlobalChatVisibilityController.Mode.Peek);
            _channels.RefreshUnread();

            ApplyChatConfig();

            if (Application.isPlaying)
            {
                try
                {
                    _networkService.Send(new QueryChatHistoryPacket(_channels.ActiveTag, 0));

                    // Имя канала в шапке — из списка каналов сервера; список ещё не приходил — запросить
                    if (_chatEvents.LastChatList == null)
                    {
                        _networkService.Send(new OpenChatListPacket());
                    }
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
            _channels.UpdateChannelHeader();
        }

        protected void OnDestroy()
        {
            if (_uiInput != null)
            {
                _uiInput.IsChatFocused = false;
            }

            if (_touchCommands != null)
            {
                _touchCommands.GlobalChatRequested -= OnTouchChatRequested;
            }

            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_chatEvents != null)
            {
                _chatEvents.MessageReceived -= AddMessage;
                _chatEvents.HistoryReceived -= AddHistory;
                _chatEvents.MuteReceived -= _mutePresenter.ApplyMute;
                _chatEvents.ChatListReceived -= _channels.ApplyChatList;
                _chatEvents.UnreadChanged -= _channels.RefreshUnread;
                _chatEvents.SetFocusedTag(null);
            }

            _blink?.Dispose();
            _blink = null;

            _view?.Tree.RemoveFromHierarchy();
            _messages.Detach();
            _mutePresenter.UnbindView();
            _view = null;
        }

        private void ApplyChatConfig()
        {
            if (_view?.InputField != null)
            {
                _view.InputField.maxLength = ChatProtocolLimits.MaximumGlobalChatLength;
            }
        }

        protected void Update()
        {
            if (!_initialized)
            {
                return;
            }

            _mutePresenter.Refresh();

            // Без клавиатуры (телефон) остальное — клавиши; открытие и
            // отправка приходят кнопками «Чат» и «>».
            if (!Application.isFocused || _uiInput.IsKeyCaptureInProgress || Keyboard.current == null)
            {
                return;
            }

            bool inputBlocked = _inputBlocker != null && _inputBlocker.IsInputBlocked;

            // T не открывает этот канал: он принадлежит локальному чату
            // (FloatingChatManager → LocalChatInput). Режимы глобального чата листает TAB.
            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                // TAB работает всегда, независимо от блокировки ввода: открытые поверх
                // окна, инвентарь или пауза не мешают сменить режим чата.
                _visibility.Cycle();

                return;
            }

            if (!_visibility.IsOpen)
            {
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

            // Escape этот чат не закрывает: TAB — единственная клавиша
            // открытия/закрытия. Пока поле в фокусе, ESC ничего не делает:
            // меню паузы не открывается, потому что ввод занят чатом
            // (IsChatFocused проверяется в PauseMenu.Update).
        }

        private void CreateUI()
        {
            var uiUxml = Resources.Load<VisualTreeAsset>(
                ResourcePaths.GlobalChatUxml);
            if (uiUxml == null)
            {
                return;
            }

            VisualElement tree = uiUxml.CloneTree();
            UILocalizer.Apply(tree, _loc);
            _view = new ChatViewElements(tree, _loc);
            _channels.BindView(_view);
            _mutePresenter.BindView(_view);

            if (_doc != null && _view.Panel != null)
            {
                _doc.rootVisualElement.Add(tree);

                // Колесо над окном чата прокручивает лог, а не зумит камеру:
                // клеймим кадр в UIInputManager. TrickleDown — раньше нативного
                // обработчика ScrollView, который гасит событие до bubbling.
                _view.Panel.RegisterCallback<WheelEvent>(
                    _ => _uiInput.NotifyWheelClaimedByUI(),
                    TrickleDown.TrickleDown);
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
                _channels.ToggleChatList,
                () => _visibility.Set(GlobalChatVisibilityController.Mode.Peek),
                () => _visibility.Set(GlobalChatVisibilityController.Mode.Open));

            // Устанавливаем фолбэк-шрифт смайлов для текста сообщений и поля ввода.
            ChatEmojiCatalog.EnsureInstalled();
            _messages.Attach(
                _view.ScrollView,
                _channels.OnMessageClicked);
            _messages.AttachPeek(_view.PeekLines);
            _channels.UpdateChannelHeader();
        }

        private void OnSendClicked()
        {
            if (_view?.InputField == null || _muteTracker.IsMuted)
            {
                return;
            }

            if (!_channels.CanSendToActiveChannel)
            {
                return;
            }

            string text = _view.InputField.value.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int chatMaxLen = ChatProtocolLimits.MaximumGlobalChatLength;
            if (text.Length > chatMaxLen)
            {
                text = text.Substring(0, chatMaxLen);
            }

            try
            {
                _networkService.Send(new SendChatMessagePacket(
                    _channels.ActiveTag,
                    text));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GlobalChatUI] Не удалось отправить сообщение в чат: {ex}");
            }

            _view.InputField.value = string.Empty;
            _view.InputField.Focus();
        }

        public void Toggle() => _visibility.Cycle();

        public void Show() => _visibility.Show();

        public void Hide() => _visibility.Hide();

        public void AddMessage(ChatMessagePacket msg)
        {
            if (_view?.ScrollView == null)
            {
                return;
            }

            _messages.Add(ChatMessageFormatter.FormatGlobal(msg), msg.Id, ChatMessageFormatter.FormatPeek(msg), ChatMessageFormatter.FormatTime(msg));
        }

        // Живые сообщения приходят только участникам текущего чата (роутинг по
        // CurrentChat на сервере) — тег учитывать не требуется. История (E5-C)
        // отдаётся по тегу: чужая лента отбрасывается.

        private void AppendMessage(string formattedMessage, long messageId = 0)
        {
            _messages.Add(formattedMessage, messageId);
        }

        private void AddHistory(ChatMessageListPacket packet)
        {
            if (!string.Equals(packet.Tag, _channels.ActiveTag, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[GlobalChatUI] Ignoring history for inactive chat '{packet.Tag}'.");
                return;
            }

            foreach (ChatMessagePacket message in packet.Messages)
            {
                _messages.Add(ChatMessageFormatter.FormatGlobal(message), message.Id, ChatMessageFormatter.FormatPeek(message), ChatMessageFormatter.FormatTime(message));
            }

            // Свежая загрузка истории - показываем последние сообщения,
            // независимо от того, где был скролл до этого.
            _messages.ScrollToBottom();
        }

        private void AddSystemMessage(string message) => AppendMessage(message);
    }
}
