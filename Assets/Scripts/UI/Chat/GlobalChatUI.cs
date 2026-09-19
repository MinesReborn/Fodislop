#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Game.Managers;
using Kern.Networking;
using Kern.Game.Inventory;
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
        [Inject] private InventoryModel _inventory = null!;
        [Inject] private UIInputManager _uiInput = null!;
        [Inject] private ILocalizationService _loc = null!;
        [Inject] private IAsyncOperationSupervisor _operations = null!;

        private ChatEventGateway _chatEvents = null!;
        private ChatViewElements? _view;
        private ChatColorController? _colorController;
        private ChatBlinkController? _blink;
        private bool _isOpen;
        private bool _initialized;
        private ChatChannel _activeChannel;
        private readonly ChatMessageHistory _history = new();
        private readonly ChatMuteTracker _muteTracker = new();
        private bool _lastMutedState;
        private bool _hasCachedMuteState;

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
            UpdateChannelPresentation();
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
                _chatEvents.MuteReceived -= ApplyMute;
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
                    !_inventory.HasSelectedItem)
                {
                    SelectChannel(ChatChannel.Global);
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
                () => SelectChannel(ChatChannel.Global),
                () => SelectChannel(ChatChannel.Local));

            _colorController = new ChatColorController(_networkService, _view.ColorButton, _view.ColorGrid);
            SelectChannel(ChatChannel.Global);
        }

        private void OnSendClicked()
        {
            if (_view?.InputField == null || _muteTracker.IsMuted)
            {
                return;
            }

            string text = _view.InputField.value.Trim();
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

            AppendMessage(ChatChannel.Global, ChatMessageFormatter.FormatGlobal(msg, DateTime.Now));
        }

        // Локальные сообщения в окне не показываются и потому здесь не
        // выписываются вовсе. Локальный чат — это облако над роботом, а не строка
        // в журнале; пока он был и там, и там, сообщение появлялось дважды, причём
        // в журнале без всякой привязки к тому, кто и где его сказал. Показ —
        // у FloatingChatManager, вкладка осталась только режимом ввода.

        private void AppendMessage(ChatChannel channel, string formattedMessage)
        {
            _history.Add(channel, formattedMessage);
            if (_activeChannel == channel)
            {
                AppendVisibleMessage(formattedMessage);
            }
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

        private void SelectChannel(ChatChannel channel)
        {
            _activeChannel = channel;
            if (_view?.InputField != null)
            {
                _view.InputField.maxLength = channel == ChatChannel.Local
                    ? ProjectRuntimeContracts.Chat.MaximumLocalChatLength
                    : ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
            }

            UpdateChannelPresentation();
            RenderActiveMessages();
        }

        private void UpdateChannelPresentation()
        {
            ChatChannelPresenter.UpdatePresentation(
                _activeChannel,
                _view?.ChatHeader,
                _view?.GlobalChannelButton,
                _view?.LocalChannelButton,
                _view?.ColorButton,
                _loc);

            if (_activeChannel == ChatChannel.Local)
            {
                _colorController?.CloseColorGrid();
            }
        }

        private void RenderActiveMessages()
        {
            if (_view?.ScrollView == null)
            {
                return;
            }

            _view.ScrollView.Clear();
            var messages = _history.GetMessages(_activeChannel);
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

            if (_hasCachedMuteState && _lastMutedState == muted)
            {
                return;
            }

            _lastMutedState = muted;
            _hasCachedMuteState = true;
            _view?.InputField?.SetEnabled(!muted);
            _view?.SendButton?.SetEnabled(!muted);
            _view?.ColorButton?.SetEnabled(!muted);
        }

        private void AddSystemMessage(string message) => AppendMessage(ChatChannel.Global, message);
    }
}
