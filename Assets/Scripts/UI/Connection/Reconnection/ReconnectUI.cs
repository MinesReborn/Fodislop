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
    public class ReconnectUI : MonoBehaviour
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
        private bool _reconnectStatusSet;
        private bool _initialized;

        private void OnDestroy()
        {
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

            // Статические ключи Uxml резолвятся сразу при сборке (контракт
            // един для всех экранов; здесь их нет — тексты ставит код).
            UILocalizer.Apply(tree, _loc);

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
            // A4-S: после кика мультилогином/разрыва игрок возвращается на экран
            // входа (токен сохранён — повторный вход по кнопке Play на Gateway).
            _disconnectOkButton.clicked += ExitToGateway;

            _reconnectOverlay.SetEnabled(false);
            _disconnectOverlay.SetEnabled(false);

            _doc.rootVisualElement.Add(_reconnectOverlay);
            _doc.rootVisualElement.Add(_disconnectOverlay);
        }

        private string Resolve(string text)
        {
            return _loc != null && _loc.HasKey(text) ? _loc.Get(text) : text;
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

            _reconnectLabel.text = Resolve(status);

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

            _disconnectLabel.text = Resolve(reason);

            UIState.Show(_disconnectOverlay);
            _disconnectOverlay.SetEnabled(true);
            _disconnectOverlay.pickingMode = PickingMode.Position;
        }

        // Кик/разрыв — соединение уже мертво: единственный выход из блокирующего
        // оверлея это экран входа. Токен не трогаем (сохранён — повторный вход
        // по кнопке Play на Gateway).
        private void ExitToGateway()
        {
            if (_sceneNavigator == null || _operations == null)
            {
                return;
            }

            Hide();
            _operations.Run(
                "reconnect_exit_to_gateway",
                cancellationToken => _sceneNavigator.TransitionAsync(SceneNames.Gateway, cancellationToken));
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
                _reconnectLabel.text = Resolve(status);
            }

            if (!_reconnectStatusSet && _doc != null && _reconnectOverlay != null)
            {
                UIState.Show(_reconnectOverlay);
                _reconnectOverlay.SetEnabled(true);
                _reconnectOverlay.pickingMode = PickingMode.Position;
            }
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
