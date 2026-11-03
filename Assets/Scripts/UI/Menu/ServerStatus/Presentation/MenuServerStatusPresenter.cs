#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using VContainer;
using Stopwatch = System.Diagnostics.Stopwatch;
using Kern.Networking.Gateways;
using Kern.Networking.Status;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Menu;

internal sealed class MenuServerStatusPresenter
{
    private readonly ILocalizationService _loc;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IClientConfigManager _clientConfig;
    private readonly MenuModalManager _modalManager;

    internal MenuServerStatusPresenter(ILocalizationService localization,
        IAsyncOperationSupervisor operations, IClientConfigManager clientConfig, MenuModalManager modalManager)
    {
        _loc = localization;
        _operations = operations;
        _clientConfig = clientConfig;
        _modalManager = modalManager;
    }

    internal void Bind(VisualElement tree)
    {
        var serverItem = tree.Q<Button>("ServerItemHades");
        _onlineBadge = tree.Q<Label>("MainMenuOnlineBadge");
        _serverRowTitle = serverItem?.Q<Label>(className: "mm-sc-title");
        _serverRowPing = serverItem?.Q<Label>(className: "mm-sc-ping");
        _serverRowOnline = serverItem?.Q<Label>(className: "mm-sc-online");
        _serverDetailTitle = tree.Q<Label>("ServerDetailTitle");
        _serverDetailDepth = tree.Q<Label>("ServerDetailDepth");
        _serverDetailPing = tree.Q<Label>("ServerDetailPing");
    }


        // Живой счётчик онлайна в бейдже кнопки выбора сервера: опрашивается
        // анонимный эндпоинт сервера GET /api/status/online раз в секунду,
        // пока открыто главное меню (во время спуска/загрузки — пауза).
        // Тот же ответ питает карточку сервера: имя мира, реальная глубина и
        // реальное число игроков; пинг карточки обновляется реже — раз в минуту.
        internal const float OnlineBadgeRefreshIntervalSeconds = 1f;
        internal const int OnlineBadgeRequestTimeoutSeconds = 2;
        internal const float ServerCardPingRefreshSeconds = 60f;
        internal const string OfflineBadgeClass = "mm-server-badge--offline";
        internal Label? _onlineBadge;
        internal float _onlineBadgeRefreshTimer;
        internal bool _onlineBadgeRequestInFlight;
        internal bool _onlineBadgeFailureLogged;
        internal Label? _serverRowTitle;
        internal Label? _serverRowPing;
        internal Label? _serverRowOnline;
        internal Label? _serverDetailTitle;
        internal Label? _serverDetailDepth;
        internal Label? _serverDetailPing;
        internal float _serverCardPingAppliedAt = -ServerCardPingRefreshSeconds;



        internal void TickOnlineBadge(bool loadingActive)
        {
            if (_onlineBadge == null || _loc == null || loadingActive)
            {
                return;
            }

            _onlineBadgeRefreshTimer -= Time.deltaTime;
            if (_onlineBadgeRefreshTimer > 0f || _onlineBadgeRequestInFlight)
            {
                return;
            }

            _onlineBadgeRefreshTimer = OnlineBadgeRefreshIntervalSeconds;
            _operations.Run("menu_online_status", RequestOnlineCountAsync);
        }

        internal async UniTask RequestOnlineCountAsync(CancellationToken cancellationToken)
        {
            _onlineBadgeRequestInFlight = true;
            try
            {
                ClientConfig? config = _clientConfig != null ? _clientConfig.Config : null;
                if (config == null)
                {
                    return;
                }

                ConnectionSettings connection = config.Connection;
                string url = Kern.Networking.Status.NetworkStatusEndpoint.FromSettings(connection);
                // Круговая задержка запроса и есть пинг карточки: точнее
                // отдельной TCP-пробы ничего мерить не нужно — тот же хост.
                float requestStarted = Time.realtimeSinceStartup;
                using UnityWebRequest request = UnityWebRequest.Get(url);
                request.timeout = OnlineBadgeRequestTimeoutSeconds;
                await request.SendWebRequest().WithCancellation(cancellationToken);
                int roundTripMs = Mathf.Max(1, (int)((Time.realtimeSinceStartup - requestStarted) * 1000f));
                if (request.result != UnityWebRequest.Result.Success)
                {
                    LogOnlineBadgeFailureOnce($"HTTP {request.responseCode}: {request.error}");
                    return;
                }

                OnlineStatusPayload status = JsonUtility.FromJson<OnlineStatusPayload>(request.downloadHandler.text);
                if (status == null)
                {
                    LogOnlineBadgeFailureOnce("malformed payload");
                    return;
                }

                _onlineBadgeFailureLogged = false;
                if (_onlineBadge != null && _loc != null)
                {
                    _onlineBadge.text = _loc.Get("mainmenu.online_count_fmt", status.Players);
                    _onlineBadge.RemoveFromClassList(OfflineBadgeClass);
                }

                ApplyServerCardStatus(status, roundTripMs);
            }
            catch (OperationCanceledException)
            {
                // Меню закрывается/сцена разрушается — тихо выходим.
            }
            catch (Exception exception)
            {
                LogOnlineBadgeFailureOnce(exception.Message);
            }
            finally
            {
                _onlineBadgeRequestInFlight = false;
            }
        }

        internal void LogOnlineBadgeFailureOnce(string reason)
        {
            // Любая неудача запроса — сервер считается недоступным: бейдж
            // переводится в офлайн (красный) вместо остатка прошлых данных.
            SetOfflineBadge();
            if (_onlineBadgeFailureLogged)
            {
                return;
            }

            _onlineBadgeFailureLogged = true;
            Debug.Log($"[MainMenu] Online badge unavailable ({reason}); showing offline state.");
        }

        internal void SetOfflineBadge()
        {
            if (_onlineBadge == null)
            {
                return;
            }

            _onlineBadge.AddToClassList(OfflineBadgeClass);
            if (_loc != null)
            {
                _onlineBadge.text = _loc.Get("mainmenu.offline");
            }
        }

        /// <summary>
        /// Накатывает живой статус на строку и карточку реального сервера:
        /// имя мира, реальную глубину и реальное число игроков — на каждом
        /// успешном опросе, а пинг — не чаще раза в минуту (репорт: чаще
        /// дёргается значение и рябит). Dummy-песочница не трогается: у неё
        /// живых данных нет, свои подписи ей ставит MenuModalManager.
        /// </summary>
        internal void ApplyServerCardStatus(OnlineStatusPayload status, int roundTripMs)
        {
            if (!_modalManager.IsRealServerSelected)
            {
                return;
            }

            if (!string.IsNullOrEmpty(status.Name))
            {
                string name = status.Name.ToUpperInvariant();
                if (_serverRowTitle != null) _serverRowTitle.text = name;
                if (_serverDetailTitle != null) _serverDetailTitle.text = name;
            }

            if (_serverDetailDepth != null && status.Depth > 0)
            {
                _serverDetailDepth.text = _loc.Get("mainmenu.server_depth_fmt", FormatGrouped(status.Depth));
            }

            if (_serverRowOnline != null && _loc != null)
            {
                _serverRowOnline.text = _loc.Get("mainmenu.server_online_fmt", status.Players);
            }

            if (Time.unscaledTime - _serverCardPingAppliedAt >= ServerCardPingRefreshSeconds)
            {
                _serverCardPingAppliedAt = Time.unscaledTime;
                ILocalizationService localization = _loc ?? throw new InvalidOperationException(
                    "MenuServerStatusPresenter requires a localization service.");
                Label? rowPing = _serverRowPing;
                Label? detailPing = _serverDetailPing;
                if (rowPing is not null) rowPing.text = localization.Get("mainmenu.server_ping_fmt", roundTripMs);
                if (detailPing is not null) detailPing.text = localization.Get("mainmenu.server_ping_fmt", roundTripMs);
            }
        }

        /// <summary>«16000» → «16 000»: разряды через пробел, как на карточке было «-2 480 м».</summary>
        internal static string FormatGrouped(int value)
        {
            string digits = value.ToString();
            var grouped = new System.Text.StringBuilder(digits.Length + digits.Length / 3);
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0)
                {
                    grouped.Append(' ');
                }

                grouped.Append(digits[i]);
            }

            return grouped.ToString();
        }

        [Serializable]
        internal sealed class OnlineStatusPayload
        {
            public int Players;
            public string Name = "";
            public int Depth;
        }

}
