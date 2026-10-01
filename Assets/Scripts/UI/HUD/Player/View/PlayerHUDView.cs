#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.Models;
using Kern.Networking;
using Kern.Networking.Processors;
using Kern.Player.Logic;
using Kern.UI.HUD.Player.Model;
using Kern.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.Programmator;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI.HUD.Player.View
{
    public class PlayerHUDView : MonoBehaviour, ILocalizableUI
    {
        private Color _hpBarFillColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        private Color _hpBarLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);

        private readonly PlayerHUDStatusPanel _statusPanel = new();
        private readonly PlayerHUDSkillGrid _skillGrid = new();
        private readonly PlayerHUDBasketView _basketView = new();
        private PlayerHUDMissionPanel _missionPanel = null!;
        private PlayerHUDBonusController _bonusController = null!;
        private PlayerHUDPopups _popups = null!;

        [Inject]
        private UIDocument _doc = null!;
        private Tooltip? _tooltip;
        private bool _isLoaded;
        [Inject]
        private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private Kern.Core.Interfaces.ILocalPlayerState _localPlayer = null!;
        private readonly PlayerHUDSkeletonPulse _skeletonPulse = new();
        private PlayerHUDModeController? _modeController;
        private TemplateContainer? _hudRoot;

        private Label? _nicknameLabel;
        private Label? _levelLabel;
        private Label? _hpCountLabel;
        private VisualElement? _hpBarFill;
        private Label? _moneyLabel;
        private Label? _credsLabel;
        private Label? _geologyLabel;
        private Label? _basketPercentLabel;
        private VisualElement? _basketSeparator;
        private VisualElement? _basketContainer;
        private VisualElement? _skillContainer;

        // Перетаскивание панели за ручку (правый нижний угол).
        private VisualElement? _panelRoot;
        private VisualElement? _dragHandle;
        private bool _hudDragging;
        private int _dragPointerId = -1;
        private Vector2 _dragPointerStart;
        private Vector2 _panelStartPosition;
        private Vector2 _panelSize;

        // Сохранение позиции панели между запусками клиента.

        private ProgrammatorGrid? _programmatorGrid;
        private Button? _programmatorButton;
        private Button? _programRunButton;
        private bool _programRunning;
        [Inject]
        private ProgrammatorData? _programmatorData;
        private bool _initializationStarted;

        [Inject]
        private PlayerStatsModel _model = null!;
        [Inject]
        private GlobalChatUI _globalChatUI = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private ProgrammatorProcessor _programmatorProtocol = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private UIInputManager _uiInput = null!;
        [Inject]
        private IProgrammatorTextureCatalog _programmatorTextures = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;

        protected void Start()
        {
            TryStartInitialization();
        }

        public void EnsureInitialized()
        {
            TryStartInitialization();
        }

        protected void Update()
        {
            if (_doc != null && !_doc.enabled)
            {
                return;
            }

            _programmatorGrid?.Tick();
        }

        private void TryStartInitialization()
        {
            if (_initializationStarted)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null || _model == null ||
                _globalChatUI == null || _assetLoader == null || _networkService == null ||
                _inputBlocker == null || _loc == null || _operations == null)
            {
                return;
            }

            _initializationStarted = true;
            _operations.Run("player_hud_startup", StartAsync);
        }

        private async UniTask StartAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                destroyCancellationToken);
            CancellationToken cancellationToken = linkedCancellation.Token;

            try
            {
                InitializeHUD();
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[PlayerHUD] HUD unavailable: {exception.Message}");
                return;
            }

            _loc.RegisterLocalizable(this);

            try
            {
                await _basketView.LoadCrystalTextures(_assetLoader, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerHUD] Optional crystal textures unavailable: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested || this == null)
            {
                return;
            }

            _basketView.RebuildRows();

            // Гонка первого захода: пакет корзины приходит с сервера раньше,
            // чем асинхронно загрузятся текстуры кристаллов. RebuildRows
            // пересоздаёт строки скрытыми, а Refresh по ним ещё не бегал -
            // без перерисовки корзина пуста до первого обновления статов
            // (первого копания). Перерисовываем сразу после построения строк.
            RefreshAll();
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(PlayerHUDView));
            if (_doc == null || _doc.rootVisualElement == null || _loc == null)
            {
                // Тихий возврат безопасен: ApplyLocalizedText идемпотентен и будет
                // вызван снова (реестр / RegisterLocalizable), когда панель и
                // сервис будут готовы.
                return;
            }

            UILocalizer.Apply(_doc.rootVisualElement, _loc);
            RefreshAll();
            _programmatorGrid?.RefreshLocalization();
            UILocalizer.AssertLocalized(_doc.rootVisualElement, _loc);
        }

        protected void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _modeController?.Dispose();
            _modeController = null;

            if (_programmatorGrid?.Programs is { } closingPrograms)
            {
                closingPrograms.ActiveProgramChanged -= UpdateProgrammatorButtonLabel;
                closingPrograms.RunStateChanged -= ApplyProgramRunState;
            }

            if (_programmatorProtocol != null)
            {
                _programmatorProtocol.StateChanged -= OnProgramStateChanged;
            }

            _programmatorGrid?.Dispose();
            _programmatorGrid = null;
            _skeletonPulse.Stop();
            _skillGrid.ClearSchedules();
            _statusPanel.ClearSchedules();

            if (_model != null)
            {
                _model.OnStatsChanged -= RefreshAll;
                _model.OnSkillProgress -= OnSkillProgress;
                _model.OnDailyBonusChanged -= OnDailyBonusChanged;
                _model.OnStatusLinesChanged -= OnStatusLinesChanged;
                _model.OnMissionChanged -= OnMissionChanged;
            }

            if (_globalChatUI != null)
            {
                _globalChatUI.Hide();
            }
        }

        private void OnDailyBonusChanged() => _bonusController.UpdateDailyBonusPanel(_model);
        private void OnStatusLinesChanged() => _statusPanel.Rebuild(_model);
        private void OnMissionChanged() => _missionPanel.Update(_model);
        private void InitializeHUD()
        {
            _programmatorData ??= new ProgrammatorData();
            _programmatorTextures ??= new ProgrammatorTextureRegistry();
            _programmatorGrid ??= new ProgrammatorGrid(
                _doc,
                _loc,
                _programmatorData,
                _uiInput,
                _programmatorTextures,
                _programmatorProtocol);
            _programmatorGrid?.Initialize();
            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);

            LoadTemplate(_doc.rootVisualElement);

            if (_model != null)
            {
                _model.OnSkillProgress += OnSkillProgress;
                _model.OnStatusLinesChanged += OnStatusLinesChanged;
                _model.OnMissionChanged += OnMissionChanged;
                _model.OnDailyBonusChanged += OnDailyBonusChanged;
                _model.OnStatsChanged += RefreshAll;
                _isLoaded = _model.Health > 0 || _model.Level > 0;
            }

            _bonusController.UpdateDailyBonusPanel(_model);

            _basketView.RebuildRows();

            if (!_isLoaded && _hudRoot != null)
            {
                _skeletonPulse.Start(_hudRoot);
            }

            RefreshAll();

            var root = _doc.rootVisualElement;

            root.RegisterCallback<NavigationMoveEvent>(
                evt => evt.StopPropagation(), TrickleDown.TrickleDown);

            root.RegisterCallback<NavigationSubmitEvent>(
                evt => evt.StopPropagation(), TrickleDown.TrickleDown);

            root.RegisterCallback<KeyDownEvent>(
                evt =>
            {
                if (evt.keyCode == KeyCode.Tab)
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        private void LoadTemplate(VisualElement root)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.PlayerHudUxml) ??
                throw new InvalidOperationException(
                    "[PlayerHUD] Resources/UI/Gameplay/PlayerHUD.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            UILayoutTier.Attach(tree);
            _hudRoot = tree;
            root.Add(tree);

            UILocalizer.Apply(tree, _loc);

            _nicknameLabel = tree.Q<Label>("NicknameLabel") ??
                throw new InvalidOperationException("[PlayerHUD] NicknameLabel is missing from PlayerHUD.uxml.");
            _levelLabel = tree.Q<Label>("LevelLabel") ??
                throw new InvalidOperationException("[PlayerHUD] LevelLabel is missing from PlayerHUD.uxml.");
            Button clanButton = tree.Q<Button>("ClanButton") ??
                throw new InvalidOperationException("[PlayerHUD] ClanButton is missing from PlayerHUD.uxml.");
            clanButton.clicked += () => _networkService?.Send(new OpenClanClickPacket());

            _hpCountLabel = tree.Q<Label>("HPCountLabel") ??
                throw new InvalidOperationException("[PlayerHUD] HPCountLabel is missing from PlayerHUD.uxml.");
            _hpBarFill = tree.Q<VisualElement>("HPBarFill") ??
                throw new InvalidOperationException("[PlayerHUD] HPBarFill is missing from PlayerHUD.uxml.");

            _moneyLabel = tree.Q<Label>("MoneyLabel") ??
                throw new InvalidOperationException("[PlayerHUD] MoneyLabel is missing from PlayerHUD.uxml.");
            _credsLabel = tree.Q<Label>("CredsLabel") ??
                throw new InvalidOperationException("[PlayerHUD] CredsLabel is missing from PlayerHUD.uxml.");
            _basketPercentLabel = tree.Q<Label>("BasketPercentLabel") ??
                throw new InvalidOperationException("[PlayerHUD] BasketPercentLabel is missing from PlayerHUD.uxml.");
            _basketSeparator = tree.Q<VisualElement>("BasketSeparator") ??
                throw new InvalidOperationException("[PlayerHUD] BasketSeparator is missing from PlayerHUD.uxml.");
            _geologyLabel = tree.Q<Label>("GeologyLabel") ??
                throw new InvalidOperationException("[PlayerHUD] GeologyLabel is missing from PlayerHUD.uxml.");

            _skeletonPulse.Register(_nicknameLabel);
            _skeletonPulse.Register(_levelLabel);
            _skeletonPulse.Register(_hpCountLabel);
            _skeletonPulse.Register(_hpBarFill);
            _skeletonPulse.Register(_moneyLabel);
            _skeletonPulse.Register(_credsLabel);
            _skeletonPulse.Register(_geologyLabel);
            _skeletonPulse.Register(_basketPercentLabel);

            _basketContainer = tree.Q<VisualElement>("BasketContainer") ??
                throw new InvalidOperationException("[PlayerHUD] BasketContainer is missing from PlayerHUD.uxml.");
            _basketView.Initialize(_basketContainer);

            _panelRoot = tree.Q<VisualElement>("PlayerStatusPanel") ??
                throw new InvalidOperationException("[PlayerHUD] PlayerStatusPanel is missing from PlayerHUD.uxml.");
            _dragHandle = tree.Q<VisualElement>("HUDDragHandle") ??
                throw new InvalidOperationException("[PlayerHUD] HUDDragHandle is missing from PlayerHUD.uxml.");
            _dragHandle.RegisterCallback<PointerDownEvent>(OnDragHandlePointerDown);
            _dragHandle.RegisterCallback<PointerMoveEvent>(OnDragHandlePointerMove);
            _dragHandle.RegisterCallback<PointerUpEvent>(OnDragHandlePointerUp);
            _dragHandle.RegisterCallback<PointerCaptureOutEvent>(OnDragHandleCaptureLost);
            ApplySavedPanelPosition();

            _skillContainer = tree.Q<VisualElement>("SkillContainer") ??
                throw new InvalidOperationException("[PlayerHUD] SkillContainer is missing from PlayerHUD.uxml.");
            _skillGrid.Initialize(_skillContainer);

            _modeController = new PlayerHUDModeController(_localPlayer, _loc, _networkService);
            _modeController.Initialize(tree, _tooltip!);

            // Кнопки чата в HUD нет: глобальный чат открывается по TAB
            // внутри GlobalChatUI.Update.

            _bonusController = new PlayerHUDBonusController(packet => _networkService?.Send(packet), _loc);
            _bonusController.Initialize(tree);
            var bonusButton = tree.Q<Button>("BonusButton");
            if (bonusButton != null)
            {
                Tooltip.AttachTo(bonusButton, () => _loc.Get("hud.tooltip.bonus"), _tooltip!);
            }

            _popups = new PlayerHUDPopups(packet => _networkService?.SendAction(packet), packet => _networkService?.Send(packet));
            _popups.Initialize(tree);

            _statusPanel.Initialize(tree);
            _missionPanel = new PlayerHUDMissionPanel(_loc);
            _missionPanel.Initialize(tree);

            _programmatorButton = tree.Q<Button>("ProgrammatorButton") ??
                throw new InvalidOperationException("[PlayerHUD] ProgrammatorButton is missing from PlayerHUD.uxml.");
            _programmatorButton.clicked += () => _programmatorGrid?.Show();

            _programRunButton = tree.Q<Button>("ProgramRunButton") ??
                throw new InvalidOperationException("[PlayerHUD] ProgramRunButton is missing from PlayerHUD.uxml.");
            _programRunButton.clicked += OnProgramRunClicked;
            Tooltip.AttachTo(
                _programRunButton,
                () => _loc.Get(_programRunning ? "hud.tooltip.program_stop" : "hud.tooltip.program_start"),
                _tooltip!);

            // Кнопка программатора показывает имя активной программы (как в старом
            // клиенте); пока программа не выбрана - стандартная подпись.
            _programmatorProtocol.StateChanged += OnProgramStateChanged;
            if (_programmatorGrid?.Programs is { } hudPrograms)
            {
                hudPrograms.ActiveProgramChanged += UpdateProgrammatorButtonLabel;
                // Локальный запуск/остановка (кнопка в окне и ▶/■ в HUD) —
                // состояние ▶/■ обязано меняться сразу, не дожидаясь пакета
                // сервера (реальный MinesServer его не присылает).
                hudPrograms.RunStateChanged += ApplyProgramRunState;
            }

            UpdateProgrammatorButtonLabel();
        }

        // Подпись кнопки программатора: имя активной программы вместо "Программатор".
        private void UpdateProgrammatorButtonLabel()
        {
            if (_programmatorButton == null)
            {
                return;
            }

            string? activeName = _programmatorGrid?.Programs?.ActiveProgramName;
            _programmatorButton.text = string.IsNullOrWhiteSpace(activeName)
                ? _loc.Get("hud.programmator")
                : activeName;
        }

        // Одна кнопка запуска/остановки (как в старом клиенте): ▶ - старт, ■ - стоп.
        private void OnProgramRunClicked()
        {
            if (_programRunning)
            {
                _programmatorProtocol.StopProgram();
            }
            else
            {
                _programmatorProtocol.StartProgram();
            }
        }

        // Пауза тоже считается активным выполнением - в этом состоянии кнопка останавливает.
        private void OnProgramStateChanged(ProgramStatePacket packet)
        {
            ApplyProgramRunState(
                packet.State == ProgramState.Running || packet.State == ProgramState.Paused);
        }

        // Единая точка применения состояния ▶/■: сюда приходят и серверные
        // пакеты, и локальный запуск/остановка из программатора.
        private void ApplyProgramRunState(bool running)
        {
            _programRunning = running;
            if (_programRunButton != null)
            {
                _programRunButton.text = _programRunning ? "■" : "▶";
                _programRunButton.EnableInClassList("hud-program-run-btn--running", _programRunning);
            }
        }

        // Восстановление позиции панели с прошлого запуска: применяем после
        // первой раскладки (до неё размеры панели ещё не посчитаны).
        private void ApplySavedPanelPosition()
        {
            if (_panelRoot == null || !_clientConfig.Config.Interface.HasHudPanelPosition)
            {
                return;
            }

            _panelRoot.RegisterCallback<GeometryChangedEvent>(ApplySavedPanelPositionOnce);
        }

        private void ApplySavedPanelPositionOnce(GeometryChangedEvent evt)
        {
            if (_panelRoot == null)
            {
                return;
            }

            // Первый GeometryChangedEvent может прийти до настоящей раскладки -
            // размер панели ещё нулевой. Остаёмся подписанными и ждём следующий.
            if (_panelRoot.layout.width <= 0f || _panelRoot.layout.height <= 0f)
            {
                return;
            }

            _panelRoot.UnregisterCallback<GeometryChangedEvent>(ApplySavedPanelPositionOnce);

            // ВАЖНО: размеры панели (width/height/right/bottom) здесь не трогаем -
            // сброс их через StyleKeyword.Undefined схлопывает бокс панели
            // (фон перестаёт оборачивать контент). Задаём только позицию.
            float x = _clientConfig.Config.Interface.HudPanelX;
            float y = _clientConfig.Config.Interface.HudPanelY;

            // Мусор в сохранении (NaN/бесконечность) - сбрасываем на позицию
            // по умолчанию из стилей.
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
            {
                _clientConfig.UpdateSection(
                    config => config.Interface,
                    settings => settings.HasHudPanelPosition = false);
                return;
            }

            // Разрешение могло смениться с прошлого запуска - не даём панели
            // уйти за пределы экрана (размер экрана - через корень панели).
            Rect screen = _panelRoot.panel.visualTree.layout;
            float maxX = Mathf.Max(0f, screen.width - _panelRoot.layout.width);
            float maxY = Mathf.Max(0f, screen.height - _panelRoot.layout.height);

            _panelRoot.style.left = Mathf.Clamp(x, 0f, maxX);
            _panelRoot.style.top = Mathf.Clamp(y, 0f, maxY);
        }

        private void RefreshAll()
        {
            var stats = _model;
            if (stats == null)
            {
                return;
            }

            if (!_isLoaded && (stats.Health > 0 || stats.Level > 0 || stats.Money > 0 || !string.IsNullOrEmpty(stats.Nickname)))
            {
                _isLoaded = true;
                _skeletonPulse.Stop();
            }

            if (_nicknameLabel != null)
            {
                _nicknameLabel.text = string.IsNullOrEmpty(stats.Nickname) ? "---" : stats.Nickname;
            }

            if (_levelLabel != null)
            {
                // "Уровень: X" - белым, компактно; до загрузки данных строка пустая.
                _levelLabel.text = _isLoaded ? $"Уровень: {stats.Level:N0}" : string.Empty;
                _levelLabel.style.color = Color.white;
            }

            float pct = stats.HealthPercent;

            if (_hpBarFill != null)
            {
                _hpBarFill.style.width = new Length(pct * 100, LengthUnit.Percent);
                _hpBarFill.style.backgroundColor = pct < 0.25f ? _hpBarLowColor : _hpBarFillColor;
            }

            if (_hpCountLabel != null)
            {
                // Счётчик внутри полосы: белый жирный текст с тёмной обводкой -
                // читается на любом цвете заливки (стиль в .hud-hp-count).
                _hpCountLabel.text = _isLoaded
                    ? $"{stats.Health:N0} / {stats.MaxHealth:N0}"
                    : "-- / --";
            }

            if (_moneyLabel != null)
            {
                _moneyLabel.text = _isLoaded ? $"<color=lime>${stats.Money:N0}</color>" : "---";
            }

            if (_credsLabel != null)
            {
                _credsLabel.text = _isLoaded ? $"<color=yellow>C {stats.Creds:N0}</color>" : "---";
            }

            if (_geologyLabel != null)
            {
                // Геология == 0 - строка скрывается полностью, чтобы не оставлять
                // пустое пространство в панели.
                bool showGeology = _isLoaded && stats.GeologyCurrent > 0;
                _geologyLabel.text = showGeology
                    ? _loc.Get("hud.geology", stats.GeologyCurrent, stats.GeologyMax, stats.GeologyText)
                    : string.Empty;
                _geologyLabel.style.display = showGeology ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_basketPercentLabel != null)
            {
                // Общий груз == 0 - груз и разделитель скрываются.
                bool showBasket = _isLoaded && stats.BasketMaxPercent > 0;
                _basketPercentLabel.text = showBasket ? $"Груз: {stats.BasketMaxPercent}%" : string.Empty;
                _basketPercentLabel.style.display = showBasket ? DisplayStyle.Flex : DisplayStyle.None;
                SetDisplayed(_basketSeparator, showBasket);
            }

            _basketView.Refresh(stats);
        }

        private static void SetDisplayed(VisualElement? element, bool visible)
        {
            if (element == null)
            {
                return;
            }

            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Перетаскивание панели за ручку: pointer capture держит события, даже
        // когда курсор уходит за пределы ручки; позиция панели - inline left/top
        // в пикселях панели ( USS-класс .hud-panel уже position: absolute).
        private void OnDragHandlePointerDown(PointerDownEvent evt)
        {
            if (_panelRoot == null || _dragHandle == null || _hudDragging)
            {
                return;
            }

            _dragPointerId = evt.pointerId;
            _dragPointerStart = new Vector2(evt.position.x, evt.position.y);
            _panelStartPosition = new Vector2(
                _panelRoot.resolvedStyle.left,
                _panelRoot.resolvedStyle.top);
            // Размер берём из готовой раскладки - нужен только для клампа.
            // Если раскладка ещё не готова (0) - перетаскивание не начинаем,
            // чтобы не зафиксировать мусорные значения.
            _panelSize = new Vector2(
                _panelRoot.layout.width,
                _panelRoot.layout.height);
            if (_panelSize.x <= 0f || _panelSize.y <= 0f)
            {
                return;
            }

            // Размеры панели задаёт только .hud-panel - здесь трогать их нельзя
            // (inline-высота/ширина "залипают" и ломают бокс панели до конца
            // сессии). Ведём панель исключительно через left/top.
            _panelRoot.style.left = _panelStartPosition.x;
            _panelRoot.style.top = _panelStartPosition.y;

            _dragHandle.CapturePointer(evt.pointerId);
            _hudDragging = true;
        }

        private void OnDragHandlePointerMove(PointerMoveEvent evt)
        {
            if (!_hudDragging || evt.pointerId != _dragPointerId || _panelRoot == null || _panelRoot.panel == null)
            {
                return;
            }

            var next = _panelStartPosition + (new Vector2(evt.position.x, evt.position.y) - _dragPointerStart);

            // Панель не выходит за пределы экрана (размер панели - через её
            // корневой визуальный элемент, растянутый на весь экран).
            Rect screen = _panelRoot.panel.visualTree.layout;
            next.x = Mathf.Clamp(next.x, 0f, Mathf.Max(0f, screen.width - _panelSize.x));
            next.y = Mathf.Clamp(next.y, 0f, Mathf.Max(0f, screen.height - _panelSize.y));

            _panelRoot.style.left = next.x;
            _panelRoot.style.top = next.y;
        }

        private void OnDragHandlePointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _dragPointerId)
            {
                return;
            }

            _hudDragging = false;
            _dragPointerId = -1;
            _dragHandle?.ReleasePointer(evt.pointerId);

            // Позиция панели переживает перезапуск клиента.
            if (_panelRoot != null)
            {
                float left = _panelRoot.resolvedStyle.left;
                float top = _panelRoot.resolvedStyle.top;
                _clientConfig.UpdateSection(
                    config => config.Interface,
                    settings =>
                    {
                        settings.HasHudPanelPosition = true;
                        settings.HudPanelX = left;
                        settings.HudPanelY = top;
                    });
            }
        }

        private void OnDragHandleCaptureLost(PointerCaptureOutEvent evt)
        {
            _hudDragging = false;
        }

        private void OnSkillProgress(SkillType skill, long current, long max)
        {
            _skillGrid.UpdateSkillProgress(skill, current, max);
        }
    }
}
