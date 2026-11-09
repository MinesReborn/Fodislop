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

    internal MenuServerStatusPresenter(ILocalizationService localization,
        IAsyncOperationSupervisor operations, IClientConfigManager clientConfig)
    {
        _loc = localization;
        _operations = operations;
        _clientConfig = clientConfig;
    }

    internal void Bind(VisualElement tree)
    {
        _onlineBadge = tree.Q<Label>("MainMenuOnlineBadge");
    }


        // Живой счётчик онлайна в подвале меню («В сети»): опрашивается
        // анонимный эндпоинт сервера GET /api/status/online раз в секунду,
        // пока открыто главное меню (во время спуска/загрузки — пауза).
        internal const float OnlineBadgeRefreshIntervalSeconds = 1f;
        internal const int OnlineBadgeRequestTimeoutSeconds = 2;
        internal const string UnavailableBadgeClass = "mm-server-badge--offline";

        /// <summary>
        /// Состояния бейджа сервера. «Нет интернета» и «сервер недоступен» — разные
        /// случаи: первый лечится сетью на устройстве, второй — только сервером.
        /// Dummy-песочница вообще не про сеть: у неё ни онлайна, ни пинга.
        /// </summary>
        internal enum ServerStatusKind
        {
            Online,
            NoInternet,
            ServerUnavailable,
            Dummy,
        }

        internal ServerStatusKind _status = ServerStatusKind.Online;
        internal Label? _onlineBadge;
        internal float _onlineBadgeRefreshTimer;
        internal bool _onlineBadgeRequestInFlight;
        internal bool _onlineBadgeFailureLogged;



        internal void TickOnlineBadge(bool loadingActive)
        {
            if (_onlineBadge == null || _loc == null || loadingActive)
            {
                return;
            }

            if (UsesDummyTransport())
            {
                ApplyBadge(ServerStatusKind.Dummy);
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
                using UnityWebRequest request = UnityWebRequest.Get(url);
                request.timeout = OnlineBadgeRequestTimeoutSeconds;
                await request.SendWebRequest().WithCancellation(cancellationToken);
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
                    ApplyBadge(ServerStatusKind.Online);
                    _onlineBadge.text = _loc.Get("mainmenu.online_count_fmt", status.Players);
                }
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
            // Различаем два отказа: устройство без сети и живой сервер, который
            // не ответил. Раньше оба показывались как «офлайн», хотя действия
            // игрока в них разные.
            ServerStatusKind kind = Application.internetReachability == NetworkReachability.NotReachable
                ? ServerStatusKind.NoInternet
                : ServerStatusKind.ServerUnavailable;
            ApplyBadge(kind);
            if (_onlineBadgeFailureLogged)
            {
                return;
            }

            _onlineBadgeFailureLogged = true;
            Debug.Log($"[MainMenu] Online badge unavailable ({reason}); status={kind}.");
        }

        internal void ApplyBadge(ServerStatusKind kind)
        {
            _status = kind;
            if (_onlineBadge == null || _loc == null)
            {
                return;
            }

            switch (kind)
            {
                case ServerStatusKind.NoInternet:
                    _onlineBadge.AddToClassList(UnavailableBadgeClass);
                    _onlineBadge.text = _loc.Get("mainmenu.no_internet");
                    break;
                case ServerStatusKind.ServerUnavailable:
                    _onlineBadge.AddToClassList(UnavailableBadgeClass);
                    _onlineBadge.text = _loc.Get("mainmenu.server_unavailable");
                    break;
                case ServerStatusKind.Dummy:
                    _onlineBadge.RemoveFromClassList(UnavailableBadgeClass);
                    _onlineBadge.text = _loc.Get("mainmenu.dummy");
                    break;
                default:
                    _onlineBadge.RemoveFromClassList(UnavailableBadgeClass);
                    break;
            }
        }

        private bool UsesDummyTransport()
        {
            ClientConfig? config = _clientConfig != null ? _clientConfig.Config : null;
            return config == null || config.Connection.UseDummyConnection;
        }

        [Serializable]
        internal sealed class OnlineStatusPayload
        {
            public int Players;
        }

}
