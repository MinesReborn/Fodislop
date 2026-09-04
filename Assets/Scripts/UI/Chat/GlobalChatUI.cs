#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Game.Managers;
using Fodinae.Networking;
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
        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IInputBlocker _inputBlocker = null!;
        [Inject]
        private UIInputManager _uiInput = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        private ChatEventGateway _chatEvents = null!;
        private ChatViewElements? _view;
        private ChatColorController? _colorController;
        private bool _isOpen;
        private CancellationTokenSource? _idleCts;
        private bool _initialized;
        private ChatChannel _activeChannel;
        private readonly ChatMessageHistory _history = new();
        private readonly ChatMuteTracker _muteTracker = new();

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

        /// <summary>Переприменяет статические ключи UXML после смены языка.</summary>
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
                _chatEvents.LocalMessageReceived -= AddLocalMessage;
                _chatEvents.MuteReceived -= ApplyMute;
            }

            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _view?.Blinker?.StopBlink();
            _view?.Tree.RemoveFromHierarchy();
            _view = null;
            _colorController = null;
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

                if ((Keyboard.current.enterKey.wasPressedThisFrame ||
                     Keyboard.current.numpadEnterKey.wasPressedThisFrame) && !inputBlocked)
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
                    StartBlink();
                    _uiInput.IsChatFocused = true;
                });
                _view.InputField.RegisterCallback<BlurEvent>(_ =>
                {
                    StopBlink();
                    _uiInput.IsChatFocused = false;
                });
                _view.InputField.RegisterValueChangedCallback(_ => OnInputChanged());
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

        private void StartBlink()
        {
            _view?.Blinker?.StartBlink();
        }

        private void StopBlink()
        {
            _view?.Blinker?.StopBlink();
            _idleCts?.Cancel();
        }

        private void OnInputChanged()
        {
            _view?.Blinker?.StopBlink();
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

        public void AddMessage(ChatMessagePacket msg)
        {
            if (_view?.ScrollView == null)
            {
                return;
            }

            AppendMessage(ChatChannel.Global, ChatMessageFormatter.FormatGlobal(msg, DateTime.Now));
        }

        private void AddLocalMessage(LocalChatMessagePacket packet)
        {
            AppendMessage(ChatChannel.Local, ChatMessageFormatter.FormatLocal(packet, DateTime.Now, _loc));
        }

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
            _view?.InputField?.SetEnabled(!muted);
            _view?.SendButton?.SetEnabled(!muted);
            _view?.ColorButton?.SetEnabled(!muted);
        }

        private void AddSystemMessage(string message)
        {
            AppendMessage(ChatChannel.Global, message);
        }
    }
}
