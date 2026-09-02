#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Game.Managers;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
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

        private VisualElement? _reconnectOverlay;
        private VisualElement? _disconnectOverlay;
        private Label? _reconnectLabel;
        private Label? _disconnectLabel;
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
                ProjectRuntimeContracts.ResourcePaths.ReconnectUxml) ??
                throw new InvalidOperationException(
                    "[ReconnectUI] Resources/UI/Reconnect.uxml is required.");
            TemplateContainer tree = template.Instantiate();

            // Статические ключи UXML резолвятся сразу при сборке (контракт
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

            _reconnectOverlay.SetEnabled(false);
            _disconnectOverlay.SetEnabled(false);

            _doc.rootVisualElement.Add(_reconnectOverlay);
            _doc.rootVisualElement.Add(_disconnectOverlay);
        }

        /// <summary>
        /// Причины дисконнекта приходят от сервера как свободный текст — его
        /// клиент переводить не может. Известные клиентские причины передаются
        /// ключами словаря: если строка совпадает с ключом, резолвим перевод,
        /// иначе показываем как есть.
        /// </summary>
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

            HideOverlay(_disconnectOverlay);

            _reconnectLabel.text = Resolve(status);

            _reconnectStatusSet = true;
            _reconnectOverlay.style.display = DisplayStyle.Flex;
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

            HideOverlay(_reconnectOverlay);

            _disconnectLabel.text = Resolve(reason);

            _disconnectOverlay.style.display = DisplayStyle.Flex;
            _disconnectOverlay.SetEnabled(true);
            _disconnectOverlay.pickingMode = PickingMode.Position;
        }

        public void SetStatus(string status)
        {
            if (!_gameManager.IsUIAuthorized)
            {
                return;
            }

            if (_disconnectOverlay?.style.display == DisplayStyle.Flex)
            {
                return;
            }

            if (_reconnectLabel != null)
            {
                _reconnectLabel.text = Resolve(status);
            }

            if (!_reconnectStatusSet && _doc != null && _reconnectOverlay != null)
            {
                _reconnectOverlay.style.display = DisplayStyle.Flex;
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

            overlay.style.display = DisplayStyle.None;
            overlay.SetEnabled(false);
            overlay.pickingMode = PickingMode.Ignore;
        }
    }
}
