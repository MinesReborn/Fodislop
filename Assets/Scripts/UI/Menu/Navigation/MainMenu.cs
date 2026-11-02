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

namespace Kern.UI.Menu
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "MainMenu")]
    public class MainMenu : MonoBehaviour, ILocalizableUI
    {
        private const string GameSceneName = SceneNames.MainGame;

        [SerializeField]
        private Texture2D? _shadeTexture;
        [SerializeField]
        private Texture2D? _spaceBgTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private readonly MenuLoaderPanel _loaderPanel = new();
        private readonly MenuModalManager _modalManager = new();
        private readonly MenuNavigationPresenter _navigationPresenter = new();

        private bool _loadingActive;
        private bool _built;
        private bool _subscribed;
        private bool _windowVisibilitySubscribed;
        private bool _teardownStarted;
        private CancellationTokenSource? _descentCancellation;

        // Живой счётчик онлайна в бейдже кнопки выбора сервера: опрашивается
        // анонимный эндпоинт сервера GET /api/status/online раз в секунду,
        // пока открыто главное меню (во время спуска/загрузки — пауза).
        // Тот же ответ питает карточку сервера: имя мира, реальная глубина и
        // реальное число игроков; пинг карточки обновляется реже — раз в минуту.
        private const float OnlineBadgeRefreshIntervalSeconds = 1f;
        private const int OnlineBadgeRequestTimeoutSeconds = 2;
        private const float ServerCardPingRefreshSeconds = 60f;
        private const string OfflineBadgeClass = "mm-server-badge--offline";
        private Label? _onlineBadge;
        private float _onlineBadgeRefreshTimer;
        private bool _onlineBadgeRequestInFlight;
        private bool _onlineBadgeFailureLogged;
        private Label? _serverRowTitle;
        private Label? _serverRowPing;
        private Label? _serverRowOnline;
        private Label? _serverDetailTitle;
        private Label? _serverDetailDepth;
        private Label? _serverDetailPing;
        private float _serverCardPingAppliedAt = -ServerCardPingRefreshSeconds;

        // Живая проверка файлов в окне «Диагностика и целостность файлов»: при
        // открытии запускает IMenuFileDiagnosticsService, рисует статус каждой
        // проверки и общий прогресс; «Перепроверить кэш» прогоняет заново.
        private VisualElement? _repairProgressBlock;
        private VisualElement? _repairProgressFill;
        private Label? _repairProgressPercent;
        private VisualElement? _repairLogLines;
        private Label? _repairSummaryLine;
        private Button? _runFullRepairButton;
        private MenuFileDiagnosticsReport? _lastDiagnosticsReport;
        private bool _diagnosticsInFlight;
        private bool _repairProgressShown;

        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private IConnectionService _connection = null!;
        [Inject]
        private IWorldEntryPreparation _worldEntryPreparation = null!;
        [Inject]
        private IWorldLoadProgress _loadProgress = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IMenuFileDiagnosticsService _fileDiagnostics = null!;
        [Inject]
        private WindowCommandStream _windowCommands = null!;
        [Inject]
        private IPlayerStats _playerStats = null!;

        private Label? _userNameLabel;
        private Label? _userRankLabel;
        private Label? _profileNameLabel;
        private Label? _profileClanLabel;
        private bool _playerIdentitySubscribed;

        private MenuStarfield? _sceneStarfield;
        private MenuSceneryController? _sceneScenery;
        private MenuSceneryPresenter _sceneryPresenter = null!;

        [Inject]
        private void Construct(IRuntimeAssetPaths runtimeAssetPaths)
        {
            _sceneryPresenter = new MenuSceneryPresenter(runtimeAssetPaths);
        }

        protected void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _built = false;
            }
        }

        protected void OnEnable()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (_built && Application.isPlaying && _tree != null)
            {
                UIDocument doc = GetComponent<UIDocument>();
                if (doc == null || doc.rootVisualElement == null)
                {
                    // Реактивация — best-effort: панель может пересоздаться позже
                    // (повторный OnEnable документа); первичная сборка в Start
                    // уже прошла, поэтому тихий возврат не теряет экран.
                    return;
                }

                _root = doc.rootVisualElement;
                _root.pickingMode = PickingMode.Ignore;
                if (!_root.Contains(_tree))
                {
                    _root.Add(_tree);
                }
                SubscribeEvents();
                SubscribeWindowVisibility();
                _sceneryPresenter.Bind(_tree);
                _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

                if (_loc != null)
                {
                    _loc.RegisterLocalizable(this);
                    ApplyLocalizedText();
                }
            }
        }

        public void InitializeScene(MenuStarfield? starfield, MenuSceneryController? scenery)
        {
            _sceneStarfield = starfield;
            _sceneScenery = scenery;
            _sceneryPresenter.BindScene(starfield, scenery);

            if (_teardownStarted)
            {
                return;
            }

            if (_built && _tree != null)
            {
                return;
            }

            if (_built)
            {
                Debug.LogWarning("[MainMenu] _built was true but _tree is null (likely a hot-reload while in Play Mode) - rebuilding UI from scratch.");
                _built = false;
            }

            _doc = GetComponent<UIDocument>();
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_doc == null || _root == null)
            {
                throw new InvalidOperationException(
                    "[MainMenu] UIDocument panel is not available at Start (панель создаётся в OnEnable документа и к Start обязана существовать).");
            }

            PanelSettings panelSettings = _doc.panelSettings ??
                throw new InvalidOperationException(
                    "[MainMenu] UIDocument requires an authored PanelSettings asset.");

            var mainMenuUxml = Resources.Load<VisualTreeAsset>(ResourcePaths.MainMenuUxml);
            if (mainMenuUxml == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/Menu/Templates/MainMenu.uxml' was not found.");
            }

            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            VisualElement tree = mainMenuUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            _doc.panelSettings = panelSettings;
            UIScreen.Bind(_doc, _clientConfig?.Config.Interface.UIScale ?? 0f, _clientConfig?.Config.Interface.TextScale);
            _tree = tree;

            BindUIElements(tree);
            _modalManager.Bind(tree);
            _sceneryPresenter.Bind(tree);

            _subscribed = false;
            SubscribeEvents();
            SubscribeWindowVisibility();
            _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

            if (_loc != null)
            {
                _loc.RegisterLocalizable(this);
            }

            ApplyLocalizedText();
            _built = true;

            _sceneryPresenter.MarkUIBuilt();
            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        public async UniTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
        {
            float timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                _sceneryPresenter.Tick(ref _spaceBgTexture);
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "Сценка главного меню не готова за 3 секунды. " +
                $"Разметка построена={_built}; {_sceneryPresenter.DescribeReadiness()}.");
        }

        private void BindUIElements(VisualElement tree)
        {
            VisualElement searchRoot = _root ?? tree;
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer") ?? searchRoot.Q<VisualElement>("MainMenuContainer");
            _onlineBadge = tree.Q<Label>("MainMenuOnlineBadge");

            // Живое имя и уровень шахтёра (бейдж справа сверху и досье):
            // сервер присылает их по сокету ещё в меню, см. PlayerIdentityProcessor.
            _userNameLabel = tree.Q<Label>(className: "mm-user-name");
            _userRankLabel = tree.Q<Label>(className: "mm-user-rank");
            _profileNameLabel = tree.Q<Label>(className: "mm-profile-name");
            _profileClanLabel = tree.Q<Label>(className: "mm-profile-clan");
            SubscribePlayerIdentity();

            // Строка и карточка реального сервера в окне выбора: метки без
            // имён, ищутся классами внутри карточки ServerItemHades.
            var serverItem = tree.Q<Button>("ServerItemHades");
            _serverRowTitle = serverItem?.Q<Label>(className: "mm-sc-title");
            _serverRowPing = serverItem?.Q<Label>(className: "mm-sc-ping");
            _serverRowOnline = serverItem?.Q<Label>(className: "mm-sc-online");
            _serverDetailTitle = tree.Q<Label>("ServerDetailTitle");
            _serverDetailDepth = tree.Q<Label>("ServerDetailDepth");
            _serverDetailPing = tree.Q<Label>("ServerDetailPing");

            _repairProgressBlock = tree.Q<VisualElement>("RepairProgressBlock");
            _repairProgressFill = tree.Q<VisualElement>("RepairProgressFill");
            _repairProgressPercent = tree.Q<Label>("RepairProgressPercent");
            _repairLogLines = tree.Q<VisualElement>("RepairLogLines");
            _runFullRepairButton = tree.Q<Button>("RunFullRepairButton");
            _loaderPanel.Bind(tree, searchRoot, _loc);

            _navigationPresenter.Bind(
                tree,
                _modalManager,
                OnPlayButtonClicked,
                CancelDescent,
                _loc);

            _loaderPanel.Reset();
        }

        protected void Update()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (Application.isPlaying && !_built)
            {
                InitializeScene(_sceneStarfield, _sceneScenery);
                if (!_built)
                {
                    return;
                }
            }

            if (Application.isPlaying && _built && _doc != null && _tree != null)
            {
                var liveRoot = _doc.rootVisualElement;
                if (liveRoot == null || !ReferenceEquals(_tree.parent, liveRoot))
                {
                    _tree = null;
                    _built = false;
                    InitializeScene(_sceneStarfield, _sceneScenery);
                    return;
                }
            }

            // [ExecuteAlways]: вне Play Mode VContainer не вызывает Construct.
            if (!Application.isPlaying)
            {
                return;
            }

            if (_loadingActive)
            {
                UpdateLoaderProgress();
            }

            _sceneryPresenter.Tick(ref _spaceBgTexture);
            MenuKeyboardHandler.HandleInput(_modalManager, _loadingActive, OnPlayButtonClicked, CancelDescent);
            TickOnlineBadge();

        }




        // ── Окно «Диагностика и целостность файлов» ──

        private const string RepairStatusOkKey = "mainmenu.repair_status_ok";
        private const string RepairStatusWarningKey = "mainmenu.repair_status_warning";
        private const string RepairStatusFailedKey = "mainmenu.repair_status_failed";
        private const string RepairStatusRunningKey = "mainmenu.repair_status_running";
        private const string RepairStatusPendingKey = "mainmenu.repair_status_pending";

        private void OnRepairModalOpened()
        {
            if (_diagnosticsInFlight)
            {
                return;
            }

            ApplyRepairTexts();
            _operations.Run("menu_file_diagnostics", token => RunDiagnosticsAsync(false, token));
        }

        private void OnRunFullDiagnosticsClicked()
        {
            if (_diagnosticsInFlight)
            {
                return;
            }

            _operations.Run("menu_file_diagnostics_full", token => RunDiagnosticsAsync(true, token));
        }

        private async UniTask RunDiagnosticsAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (_diagnosticsInFlight || _loc == null || _repairLogLines == null)
            {
                return;
            }

            _diagnosticsInFlight = true;
            _repairProgressShown = false;
            _runFullRepairButton?.SetEnabled(false);
            ResetRepairLines();
            try
            {
                MenuFileDiagnosticsReport report = await _fileDiagnostics.RunAllAsync(
                    activeCheck: MarkRepairLineRunning,
                    fraction: UpdateRepairProgress,
                    forceRefresh,
                    cancellationToken);

                if (_teardownStarted)
                {
                    return;
                }

                _lastDiagnosticsReport = report;
                RenderRepairReport(report);
            }
            catch (OperationCanceledException)
            {
                // Меню закрывается — тихо выходим.
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MainMenu] File diagnostics failed: {exception}");
                if (!_teardownStarted && _repairSummaryLine != null)
                {
                    SetRepairLine(_repairSummaryLine, _loc.Get("mainmenu.repair_run_failed"), "mm-log-line--error");
                }
            }
            finally
            {
                _diagnosticsInFlight = false;
                _repairProgressShown = false;
                _runFullRepairButton?.SetEnabled(true);
                UIState.Hide(_repairProgressBlock);
            }
        }

        /// <summary>Строки проверок: создать по числу ключей сервиса плюс строка итога.</summary>
        private void EnsureRepairLines()
        {
            if (_repairLogLines == null || _fileDiagnostics == null)
            {
                return;
            }

            if (_repairLogLines.childCount == 0)
            {
                for (int index = 0; index < _fileDiagnostics.CheckLabelKeys.Count; index++)
                {
                    var line = new Label { name = $"RepairCheckLine{index}" };
                    line.AddToClassList("mm-log-line");
                    _repairLogLines.Add(line);
                }

                _repairSummaryLine = new Label { name = "RepairSummaryLine" };
                _repairSummaryLine.AddToClassList("mm-log-line");
                _repairSummaryLine.AddToClassList("mm-log-line--pending");
                _repairLogLines.Add(_repairSummaryLine);
            }
            else if (_repairSummaryLine == null)
            {
                _repairSummaryLine = (Label)_repairLogLines[_repairLogLines.childCount - 1];
            }
        }

        /// <summary>Все строки — в состояние ожидания: проверки ещё не дошли до них.</summary>
        private void ResetRepairLines()
        {
            if (_repairLogLines == null || _loc == null || _fileDiagnostics == null)
            {
                return;
            }

            EnsureRepairLines();
            int checkCount = Mathf.Min(_fileDiagnostics.CheckLabelKeys.Count, _repairLogLines.childCount - 1);
            for (int index = 0; index < checkCount; index++)
            {
                SetRepairLine(
                    (Label)_repairLogLines[index],
                    $"{_loc.Get(_fileDiagnostics.CheckLabelKeys[index])} — {_loc.Get(RepairStatusPendingKey)}",
                    "mm-log-line--pending");
            }

            if (_repairSummaryLine != null)
            {
                SetRepairLine(_repairSummaryLine, string.Empty, "mm-log-line--pending");
            }
        }

        private void MarkRepairLineRunning(int index)
        {
            ShowRepairProgressOnce();
            if (_repairLogLines == null || _loc == null ||
                index < 0 || index >= _repairLogLines.childCount - 1)
            {
                return;
            }

            SetRepairLine(
                (Label)_repairLogLines[index],
                $"{_loc.Get(_fileDiagnostics.CheckLabelKeys[index])} — {_loc.Get(RepairStatusRunningKey)}",
                "mm-log-line--running");
        }

        private void UpdateRepairProgress(float fraction)
        {
            ShowRepairProgressOnce();
            float clamped = Mathf.Clamp01(fraction);
            if (_repairProgressFill != null)
            {
                _repairProgressFill.style.width = new Length(clamped * 100f, LengthUnit.Percent);
            }

            if (_repairProgressPercent != null)
            {
                _repairProgressPercent.text = $"{Mathf.RoundToInt(clamped * 100f)}%";
            }
        }

        private void ShowRepairProgressOnce()
        {
            if (_repairProgressShown || _repairProgressBlock == null)
            {
                return;
            }

            _repairProgressShown = true;
            UIState.Show(_repairProgressBlock);
        }

        private void RenderRepairReport(MenuFileDiagnosticsReport report)
        {
            if (_repairLogLines == null || _loc == null || _fileDiagnostics == null)
            {
                return;
            }

            EnsureRepairLines();
            int checkCount = Mathf.Min(report.Checks.Count, _repairLogLines.childCount - 1);
            for (int index = 0; index < checkCount; index++)
            {
                MenuFileCheckResult check = report.Checks[index];
                SetRepairLine(
                    (Label)_repairLogLines[index],
                    BuildRepairLineText(check.LabelKey, check.Status, check.Detail),
                    RepairStatusClass(check.Status));
            }

            if (_repairSummaryLine == null)
            {
                return;
            }

            int failures = 0;
            int warnings = 0;
            foreach (MenuFileCheckResult check in report.Checks)
            {
                switch (check.Status)
                {
                    case MenuFileCheckStatus.Failed:
                        failures++;
                        break;
                    case MenuFileCheckStatus.Warning:
                        warnings++;
                        break;
                }
            }

            if (report.AllOk)
            {
                SetRepairLine(_repairSummaryLine, _loc.Get("mainmenu.repair_all_ok"), "mm-log-line--success");
            }
            else if (failures > 0)
            {
                SetRepairLine(
                    _repairSummaryLine,
                    _loc.Get("mainmenu.repair_summary_problems", failures),
                    "mm-log-line--error");
            }
            else
            {
                SetRepairLine(
                    _repairSummaryLine,
                    _loc.Get("mainmenu.repair_summary_warnings", warnings),
                    "mm-log-line--warning");
            }
        }

        /// <summary>Тексты окна после ApplyLocalizedText и при переоткрытии: отчёт или ожидание.</summary>
        private void ApplyRepairTexts()
        {
            if (_repairLogLines == null || _loc == null || _fileDiagnostics == null)
            {
                return;
            }

            if (_lastDiagnosticsReport is { } report)
            {
                RenderRepairReport(report);
            }
            else if (!_diagnosticsInFlight)
            {
                ResetRepairLines();
            }
        }

        private string BuildRepairLineText(string labelKey, MenuFileCheckStatus status, string detail)
        {
            if (_loc == null)
            {
                throw new InvalidOperationException("Menu localization must be injected before rendering diagnostics.");
            }

            string statusKey = status switch
            {
                MenuFileCheckStatus.Ok => RepairStatusOkKey,
                MenuFileCheckStatus.Warning => RepairStatusWarningKey,
                _ => RepairStatusFailedKey,
            };

            string text = $"{_loc.Get(labelKey)} — {_loc.Get(statusKey)}";
            return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
        }

        private static string RepairStatusClass(MenuFileCheckStatus status) => status switch
        {
            MenuFileCheckStatus.Ok => "mm-log-line--success",
            MenuFileCheckStatus.Warning => "mm-log-line--warning",
            _ => "mm-log-line--error",
        };

        private void SetRepairLine(Label line, string text, string modifierClass)
        {
            line.text = text;
            line.RemoveFromClassList("mm-log-line--success");
            line.RemoveFromClassList("mm-log-line--error");
            line.RemoveFromClassList("mm-log-line--warning");
            line.RemoveFromClassList("mm-log-line--running");
            line.RemoveFromClassList("mm-log-line--pending");
            line.AddToClassList(modifierClass);
        }



        private void TickOnlineBadge()
        {
            if (_onlineBadge == null || _loc == null || _loadingActive)
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

        private async UniTask RequestOnlineCountAsync(CancellationToken cancellationToken)
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

        private void LogOnlineBadgeFailureOnce(string reason)
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

        private void SetOfflineBadge()
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
        private void ApplyServerCardStatus(OnlineStatusPayload status, int roundTripMs)
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
                _serverDetailDepth.text = $"-{FormatGrouped(status.Depth)} м";
            }

            if (_serverRowOnline != null && _loc != null)
            {
                _serverRowOnline.text = _loc.Get("mainmenu.server_online_fmt", status.Players);
            }

            if (Time.unscaledTime - _serverCardPingAppliedAt >= ServerCardPingRefreshSeconds)
            {
                _serverCardPingAppliedAt = Time.unscaledTime;
                if (_serverRowPing != null) _serverRowPing.text = $"{roundTripMs} МС";
                if (_serverDetailPing != null) _serverDetailPing.text = $"{roundTripMs} ms";
            }
        }

        /// <summary>«16000» → «16 000»: разряды через пробел, как на карточке было «-2 480 м».</summary>
        private static string FormatGrouped(int value)
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
        private sealed class OnlineStatusPayload
        {
            public int Players;
            public string Name = "";
            public int Depth;
        }

        private void UpdateLoaderProgress()
        {
            WorldLoadPhase phase = _loadProgress != null
                ? _loadProgress.CurrentPhase
                : WorldLoadPhase.Handshake;
            _loaderPanel.UpdateProgress(phase, ReleaseInputToGameplay);
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            if (_tree != null)
            {
                _modalManager.SubscribeEvents(
                    _tree,
                    OnPlayButtonClicked,
                    _clientConfig,
                    _sceneNavigator,
                    _operations,
                    _loc);

                if (_runFullRepairButton != null)
                {
                    _runFullRepairButton.clicked += OnRunFullDiagnosticsClicked;
                }

                _modalManager.RepairModalOpened += OnRepairModalOpened;
            }

            _subscribed = true;
        }

        public void OpenModal(VisualElement? modal) => _modalManager.OpenModal(modal);

        public void CloseCurrentModal() => _modalManager.CloseCurrentModal();

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(MainMenu));
            if (_tree == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_tree, _loc);
            _navigationPresenter.ApplyLocalization(_loc);
            _loaderPanel.RefreshLocalization();
            // UILocalizer.Apply сбрасывает тексты на ключи разметки — возвращаем живые.
            ApplyRepairTexts();
            ApplyPlayerIdentity();
            UILocalizer.AssertLocalized(_tree, _loc);
        }

        /// <summary>
        /// Живые данные шахтёра в бейдже и досье: ник и уровень приходят по
        /// сокету (PlayerIdentityProcessor) ещё в меню. Пустые значения оставляют
        /// локализационные заглушки, чтобы не мигать пустотой до первого пакета.
        /// </summary>
        private void SubscribePlayerIdentity()
        {
            if (_playerStats == null || _playerIdentitySubscribed)
            {
                return;
            }

            _playerStats.OnNicknameChanged += ApplyPlayerIdentity;
            _playerStats.OnStatsChanged += ApplyPlayerIdentity;
            _playerIdentitySubscribed = true;
            ApplyPlayerIdentity();
        }

        private void ApplyPlayerIdentity()
        {
            if (_userNameLabel == null || _playerStats == null || _loc == null)
            {
                return;
            }

            string nickname = _playerStats.Nickname;
            if (!string.IsNullOrWhiteSpace(nickname))
            {
                _userNameLabel.text = nickname;
                if (_profileNameLabel != null)
                {
                    _profileNameLabel.text = nickname;
                }
            }

            // В строке ранга остаётся только уровень, без ранга и тира бура.
            long level = _playerStats.Level;
            if (level <= 0)
            {
                return;
            }

            if (_userRankLabel != null)
            {
                _userRankLabel.text = level.ToString();
            }

            // В досье то же самое: строка клана хвостит уровень после «//».
            if (_profileClanLabel != null)
            {
                string clan = _loc.Get("mainmenu.profile_clan");
                int separator = clan.IndexOf("//", StringComparison.Ordinal);
                _profileClanLabel.text = separator >= 0
                    ? clan[..separator] + "// " + level
                    : level.ToString();
            }
        }

        protected void OnDestroy()
        {
            _teardownStarted = true;
            if (_doc != null)
            {
                UIScreen.Unbind(_doc);
            }

            _modalManager.RepairModalOpened -= OnRepairModalOpened;
            if (_windowVisibilitySubscribed && _windowCommands != null)
            {
                _windowCommands.OpenWindowVisibilityChanged -= OnServerWindowVisibilityChanged;
                _windowVisibilitySubscribed = false;
            }

            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _descentCancellation?.Cancel();
            _descentCancellation?.Dispose();
            _descentCancellation = null;

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void HideMenu()
        {
            UIState.Hide(_mainMenuContainer);
        }

        private void ReleaseInputToGameplay()
        {
            _loadingActive = false;
            UIState.Hide(_tree);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }
        }

        private void SubscribeWindowVisibility()
        {
            if (_windowVisibilitySubscribed || _windowCommands == null)
            {
                return;
            }

            _windowCommands.OpenWindowVisibilityChanged += OnServerWindowVisibilityChanged;
            _windowVisibilitySubscribed = true;
            OnServerWindowVisibilityChanged(_windowCommands.HasOpenWindows);
        }

        private void OnServerWindowVisibilityChanged(bool visible)
        {
            if (_teardownStarted || !_loadingActive)
            {
                return;
            }

            UIState.SetHidden(_tree, visible);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }

            if (!visible)
            {
                _loaderPanel.Show();
                UpdateLoaderProgress();
            }
        }

        private void OnPlayButtonClicked()
        {
            if (_loadingActive || _teardownStarted)
            {
                return;
            }

            Debug.Log($"[Probe] T0 {UnityEngine.Time.realtimeSinceStartup:F3}");
            Debug.Log("[MainMenu] Play button clicked - initiating descent sequence");

            HideMenu();
            _modalManager.CloseCurrentModal();
            _loadingActive = true;
            _descentCancellation?.Dispose();
            _descentCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                destroyCancellationToken);

            _loaderPanel.Show();
            _loaderPanel.Reset();

            if (_windowCommands != null && _windowCommands.HasOpenWindows)
            {
                OnServerWindowVisibilityChanged(visible: true);
            }

            _navigationPresenter.SetDescentRouteActive();

            _sceneryPresenter.DescentTarget = 1f;
            UpdateLoaderProgress();

            _operations.Run("main_menu_descent", RunDescentAsync);
        }

        private async UniTask RunDescentAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                _descentCancellation?.Token ?? CancellationToken.None);
            CancellationToken transitionToken = linkedCancellation.Token;
            try
            {
                // Подготовка мира не входит в таймаут перехода: он меряет только сцену.
                var preparationTimer = Stopwatch.StartNew();
                await _worldEntryPreparation.EnsureReadyAsync(transitionToken);
                Debug.Log(
                    $"[MainMenu] World-entry preparation completed in {preparationTimer.Elapsed.TotalSeconds:F2} s; " +
                    "starting MainGame scene transition.");
                transitionToken.ThrowIfCancellationRequested();
                await _sceneNavigator.TransitionAsync(GameSceneName, transitionToken);
            }
            catch (OperationCanceledException) when (transitionToken.IsCancellationRequested)
            {
            }
            catch (ConnectionStartupInterruptedException exception)
            {
                if (!_teardownStarted)
                {
                    // Не warning: вход в мир оборвался, а по консоли это было
                    // неотличимо от штатного спуска. Причина обязана быть
                    // слышна — иначе отказ ищут по одному экрану.
                    Debug.LogError(
                        $"[MainMenu] MainGame startup was interrupted by a disconnected transport; " +
                        $"descent remains active. {exception}");
                    _loaderPanel.ShowFailure(exception, _loc);
                }
            }
            catch (TimeoutException exception) when (IsWaitingForWorldStartup())
            {
                if (!_teardownStarted)
                {
                    Debug.LogError(
                        $"[MainMenu] MainGame startup timed out; keeping the descent screen while " +
                        $"waiting for the server. {exception}");
                    _loaderPanel.ShowFailure(exception, _loc);
                }
            }
            catch (Exception exception)
            {
                if (_teardownStarted)
                {
                    return;
                }

                _loaderPanel.ShowFailure(exception, _loc);
            }
        }

        private bool IsWaitingForWorldStartup() =>
            _connection.IsAutoReconnecting || _connection.IsWorldStartupPending;

        private void CancelDescent()
        {
            if (!_loadingActive || _teardownStarted)
            {
                return;
            }

            Debug.Log("[MainMenu] Descent cancelled by user - returning to main menu");

            // Переход сцены отменяется токеном: навигатор сам откатит частично
            // загруженную сцену и разорвёт соединение. CTS сознательно не
            // освобождается: операция могла ещё не начать исполняться и
            // прочитает токен позже; освободит её следующий запуск спуска.
            _descentCancellation?.Cancel();

            _loadingActive = false;
            _sceneryPresenter.DescentTarget = 0f;
            _sceneryPresenter.ResumeRenderers();
            _navigationPresenter.ResetDescentRoute();

            _loaderPanel.Hide();
            _loaderPanel.Reset();
            UIState.Show(_tree);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }

            UIState.Show(_mainMenuContainer);
        }
    }
}
