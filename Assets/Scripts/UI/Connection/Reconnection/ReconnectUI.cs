#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

using Kern.Game.Session;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Connection
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "ReconnectUI")]
    public class ReconnectUI : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        private IConnectionService _connection = null!;

        // Reconnect overlays must never float over the game scene before the
        // world is presented. ConnectionManager fires "connecting" status the
        // moment Connect() runs (GameBootstrap step before MarkPresentationReady);
        // without this gate that transient event would light up the overlay over
        // the scrub before the game is shown. IsUIAuthorized is set to true only
        // after the world is loaded (AuthorizeUI), so it is the exact
        // presentation-readiness signal for this Game-tier overlay.
        [Inject]
        private GameManager _gameManager = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        private VisualElement? _reconnectOverlay;
        private VisualElement? _disconnectOverlay;
        private Label? _reconnectLabel;
        private Label? _disconnectLabel;
        private Button? _disconnectOkButton;
        private Label? _disconnectTitle;
        private Label? _disconnectServerReason;
        private string? _disconnectOkTextKey;
        private string? _reconnectStatus;
        private string? _disconnectReason;
        private bool _reconnectStatusSet;
        private bool _initialized;

        private void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_connection != null)
            {
                _connection.OnReconnectStatusChanged -= ShowReconnecting;
                _connection.OnDisconnectReason -= ShowDisconnectReason;
                _connection.OnReconnectHidden -= Hide;
            }

            _reconnectOverlay?.RemoveFromHierarchy();
            _disconnectOverlay?.RemoveFromHierarchy();
            _reconnectOverlay = null;
            _disconnectOverlay = null;
            _disconnectOkButton = null;
        }

        protected void Start()
        {
            TryInitialize();
        }

        [Inject]
        private void Construct(IConnectionService connection)
        {
            _connection = connection;
            _connection.OnReconnectStatusChanged += ShowReconnecting;
            _connection.OnDisconnectReason += ShowDisconnectReason;
            _connection.OnReconnectHidden += Hide;
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            // [Inject]-метод гарантирует панель UIDocument к моменту вызова;
            // null здесь — дефект проводки, а не гонка. Молчаливый пропуск
            // оставил бы оверлей реконнекта вечно мёртвым без какой-либо
            // ошибки.
            if (_doc == null || _doc.rootVisualElement == null)
            {
                throw new InvalidOperationException(
                    "[ReconnectUI] Required UIDocument injection is missing or has no root; " +
                    "ReconnectUI must be registered in the Game scope before Start.");
            }

            CreateUI();
            _loc.RegisterLocalizable(this);
            _initialized = true;
        }

        private void CreateUI()
        {
            // Статическая структура (два оверлея с лейблами) живёт в Reconnect.uxml;
            // здесь только клон и биндинги. Видимость и enabled — рантайм-состояние.
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                ResourcePaths.ReconnectUxml) ??
                throw new InvalidOperationException(
                    "[ReconnectUI] Resources/UI/Connection/Reconnection/Templates/Reconnect.uxml is required.");
            TemplateContainer tree = template.Instantiate();

            _reconnectOverlay = tree.Q<VisualElement>("ReconnectOverlay") ??
                throw new InvalidOperationException("[ReconnectUI] ReconnectOverlay is missing from Reconnect.uxml.");
            _disconnectOverlay = tree.Q<VisualElement>("DisconnectOverlay") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectOverlay is missing from Reconnect.uxml.");
            _reconnectLabel = tree.Q<Label>("ReconnectLabel") ??
                throw new InvalidOperationException("[ReconnectUI] ReconnectLabel is missing from Reconnect.uxml.");
            _disconnectLabel = tree.Q<Label>("DisconnectLabel") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectLabel is missing from Reconnect.uxml.");
            _disconnectOkButton = tree.Q<Button>("DisconnectOkButton") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectOkButton is missing from Reconnect.uxml.");
            _disconnectOkTextKey = _disconnectOkButton.text;
            _disconnectOkButton.clicked += ExitToMainMenu;
            _disconnectTitle = tree.Q<Label>("DisconnectTitle") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectTitle is missing from Reconnect.uxml.");
            _disconnectServerReason = tree.Q<Label>("DisconnectReason") ??
                throw new InvalidOperationException("[ReconnectUI] DisconnectReason is missing from Reconnect.uxml.");
            Button reconnectMenu = tree.Q<Button>("ReconnectMenuButton") ??
                throw new InvalidOperationException("[ReconnectUI] ReconnectMenuButton is missing from Reconnect.uxml.");
            // Пока идёт переподключение, выход в меню остаётся (макет: #netMenu)
            reconnectMenu.clicked += ExitToMainMenu;

            UILocalizer.Apply(tree, _loc);

            _reconnectOverlay.SetEnabled(false);
            _disconnectOverlay.SetEnabled(false);

            _doc.rootVisualElement.Add(_reconnectOverlay);
            _doc.rootVisualElement.Add(_disconnectOverlay);
        }

        private string Resolve(string text)
        {
            return _loc != null && _loc.HasKey(text) ? _loc.Get(text) : text;
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(ReconnectUI));
            if (_reconnectOverlay == null || _disconnectOverlay == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_reconnectOverlay, _loc);
            UILocalizer.Apply(_disconnectOverlay, _loc);

            if (_reconnectLabel != null && _reconnectStatus != null)
            {
                _reconnectLabel.text = Resolve(_reconnectStatus);
            }

            if (_disconnectLabel != null && _disconnectReason != null)
            {
                ApplyDisconnectText();
            }

            if (_disconnectOkButton != null && _disconnectOkTextKey != null)
            {
                _disconnectOkButton.text = _loc.Get(_disconnectOkTextKey);
            }
        }

        public void ShowReconnecting(string status)
        {
            if (!_gameManager.IsUIAuthorized)
            {
                return;
            }

            if (_doc == null || _reconnectOverlay == null || _reconnectLabel == null)
            {
                return;
            }

            if (_doc.rootVisualElement != null && !_doc.rootVisualElement.Contains(_reconnectOverlay))
            {
                _doc.rootVisualElement.Add(_reconnectOverlay);
            }

            HideOverlay(_disconnectOverlay);

            _reconnectStatus = status;
            _reconnectLabel.text = Resolve(_reconnectStatus);

            _reconnectStatusSet = true;
            UIState.Show(_reconnectOverlay);
            _reconnectOverlay.SetEnabled(true);
            _reconnectOverlay.pickingMode = PickingMode.Position;
        }

        public void ShowDisconnectReason(string reason)
        {
            if (!_gameManager.IsUIAuthorized)
            {
                return;
            }

            if (_doc == null || _disconnectOverlay == null || _disconnectLabel == null)
            {
                return;
            }

            if (_doc.rootVisualElement != null && !_doc.rootVisualElement.Contains(_disconnectOverlay))
            {
                _doc.rootVisualElement.Add(_disconnectOverlay);
            }

            HideOverlay(_reconnectOverlay);

            _disconnectReason = reason;
            ApplyDisconnectText();

            UIState.Show(_disconnectOverlay);
            _disconnectOverlay.SetEnabled(true);
            _disconnectOverlay.pickingMode = PickingMode.Position;
        }

        public void SetStatus(string status)
        {
            if (!_gameManager.IsUIAuthorized)
            {
                return;
            }

            if (!UIState.IsHidden(_disconnectOverlay))
            {
                return;
            }

            if (_reconnectLabel != null)
            {
                _reconnectStatus = status;
                _reconnectLabel.text = Resolve(_reconnectStatus);
            }

            if (!_reconnectStatusSet && _doc != null && _reconnectOverlay != null)
            {
                UIState.Show(_reconnectOverlay);
                _reconnectOverlay.SetEnabled(true);
                _reconnectOverlay.pickingMode = PickingMode.Position;
            }
        }

        // Обрыв связи — «Связь потеряна» и сообщение клиента; причина от сервера (DisconnectPacket) —
        // «Отключено сервером», «Сервер закрыл соединение.» и сама причина плашкой (макет: js/events.js showNet)
        private void ApplyDisconnectText()
        {
            if (_disconnectLabel == null || _disconnectTitle == null || _disconnectServerReason == null || _disconnectReason == null)
            {
                return;
            }

            bool lost = _loc == null ||
                string.Equals(_disconnectReason, _loc.Get("network.error.connection_lost"), StringComparison.Ordinal) ||
                string.Equals(_disconnectReason, "network.error.connection_lost", StringComparison.Ordinal);
            if (lost)
            {
                _disconnectTitle.text = Caps(Resolve("network.lost_title"));
                _disconnectLabel.text = Resolve(_disconnectReason);
                UIState.Hide(_disconnectServerReason);
                return;
            }

            _disconnectTitle.text = Caps(Resolve("network.kicked_title"));
            _disconnectLabel.text = Resolve("network.kicked_text");
            _disconnectServerReason.text = _loc!.Get("network.kicked_reason", Resolve(_disconnectReason));
            UIState.Show(_disconnectServerReason);
        }

        private static string Caps(string text) => text.ToUpperInvariant();

        private void ExitToMainMenu()
        {
            Hide();
            _operations.Run(
                "reconnect_exit_to_main_menu",
                cancellationToken => _sceneNavigator.TransitionAsync(SceneNames.MainMenu, cancellationToken));
        }

        public void Hide()
        {
            if (_doc == null)
            {
                return;
            }

            if (_reconnectOverlay != null)
            {
                HideOverlay(_reconnectOverlay);
            }

            if (_disconnectOverlay != null)
            {
                HideOverlay(_disconnectOverlay);
            }

            _reconnectStatusSet = false;
        }

        private static void HideOverlay(VisualElement? overlay)
        {
            if (overlay == null)
            {
                return;
            }

            UIState.Hide(overlay);
            overlay.SetEnabled(false);
            overlay.pickingMode = PickingMode.Ignore;
        }
    }
}
