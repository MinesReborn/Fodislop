#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Core.Models;
using Fodinae.Networking;
using Fodinae.Player.Logic;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI.HUD.Player.View
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
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private Fodinae.Core.Interfaces.ILocalPlayerState _localPlayer = null!;
        private readonly PlayerHUDSkeletonPulse _skeletonPulse = new();
        private PlayerHUDModeController? _modeController;
        private TemplateContainer? _hudRoot;

        private Label? _nicknameLabel;
        private Label? _levelLabel;
        private Label? _hpLabel;
        private Label? _hpPercentLabel;
        private VisualElement? _hpBarFill;
        private Label? _moneyLabel;
        private Label? _credsLabel;
        private Label? _geologyLabel;
        private Label? _basketPercentLabel;
        private VisualElement? _basketContainer;
        private VisualElement? _skillContainer;

        private ProgrammatorGrid? _programmatorGrid;
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
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private UIInputManager _uiInput = null!;
        [Inject]
        private IProgrammatorTextureCatalog _programmatorTextures = null!;

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

        public void InitializeEditorPreview(UIDocument doc)
        {
            _doc = doc;
            _model = new PlayerStatsModel();
            InitializeHUD();
        }

        private void InitializeHUD()
        {
            _programmatorData ??= new ProgrammatorData();
            _programmatorTextures ??= new ProgrammatorTextureRegistry();
            _programmatorGrid ??= new ProgrammatorGrid(
                _doc,
                _loc,
                _programmatorData,
                _uiInput,
                _programmatorTextures);
            _programmatorGrid?.Initialize();
            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);

            UILayoutTier.Attach(_doc.rootVisualElement);

            LoadTemplate(_doc.rootVisualElement);

            if (_model != null)
            {
                _model.OnSkillProgress += OnSkillProgress;
                _model.OnStatusLinesChanged += OnStatusLinesChanged;
                _model.OnMissionChanged += OnMissionChanged;
            }

            if (_model != null)
            {
                _model.OnDailyBonusChanged += OnDailyBonusChanged;
            }

            _bonusController.UpdateDailyBonusPanel(_model);

            _basketView.RebuildRows();
            if (_model != null)
            {
                _model.OnStatsChanged += RefreshAll;
                _isLoaded = _model.Health > 0 || _model.Level > 0;
            }

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
                    "[PlayerHUD] Resources/UI/PlayerHUD.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
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

            _hpLabel = tree.Q<Label>("HPLabel") ??
                throw new InvalidOperationException("[PlayerHUD] HPLabel is missing from PlayerHUD.uxml.");
            _hpPercentLabel = tree.Q<Label>("HPPercentLabel") ??
                throw new InvalidOperationException("[PlayerHUD] HPPercentLabel is missing from PlayerHUD.uxml.");
            _hpBarFill = tree.Q<VisualElement>("HPBarFill") ??
                throw new InvalidOperationException("[PlayerHUD] HPBarFill is missing from PlayerHUD.uxml.");

            _moneyLabel = tree.Q<Label>("MoneyLabel") ??
                throw new InvalidOperationException("[PlayerHUD] MoneyLabel is missing from PlayerHUD.uxml.");
            _credsLabel = tree.Q<Label>("CredsLabel") ??
                throw new InvalidOperationException("[PlayerHUD] CredsLabel is missing from PlayerHUD.uxml.");
            _basketPercentLabel = tree.Q<Label>("BasketPercentLabel") ??
                throw new InvalidOperationException("[PlayerHUD] BasketPercentLabel is missing from PlayerHUD.uxml.");
            _geologyLabel = tree.Q<Label>("GeologyLabel") ??
                throw new InvalidOperationException("[PlayerHUD] GeologyLabel is missing from PlayerHUD.uxml.");

            _skeletonPulse.Register(_nicknameLabel);
            _skeletonPulse.Register(_levelLabel);
            _skeletonPulse.Register(_hpLabel);
            _skeletonPulse.Register(_hpPercentLabel);
            _skeletonPulse.Register(_hpBarFill);
            _skeletonPulse.Register(_moneyLabel);
            _skeletonPulse.Register(_credsLabel);
            _skeletonPulse.Register(_geologyLabel);
            _skeletonPulse.Register(_basketPercentLabel);

            _basketContainer = tree.Q<VisualElement>("BasketContainer") ??
                throw new InvalidOperationException("[PlayerHUD] BasketContainer is missing from PlayerHUD.uxml.");
            _basketView.Initialize(_basketContainer);

            _skillContainer = tree.Q<VisualElement>("SkillContainer") ??
                throw new InvalidOperationException("[PlayerHUD] SkillContainer is missing from PlayerHUD.uxml.");
            _skillGrid.Initialize(_skillContainer);

            _modeController = new PlayerHUDModeController(_localPlayer, _loc);
            _modeController.Initialize(tree, _tooltip!);

            Button chatButton = tree.Q<Button>("ChatButton") ??
                throw new InvalidOperationException("[PlayerHUD] ChatButton is missing from PlayerHUD.uxml.");
            chatButton.clicked += () => _globalChatUI.Toggle();
            Tooltip.AttachTo(chatButton, () => _loc.Get("hud.tooltip.chat"), _tooltip!);

            _bonusController = new PlayerHUDBonusController(packet => _networkService?.Send(packet), _loc);
            _bonusController.Initialize(tree);
            var bonusButton = tree.Q<Button>("BonusButton");
            if (bonusButton != null)
            {
                Tooltip.AttachTo(bonusButton, () => _loc.Get("hud.tooltip.bonus"), _tooltip!);
            }

            _popups = new PlayerHUDPopups(packet => _networkService?.SendAction(packet));
            _popups.Initialize(tree);

            _statusPanel.Initialize(tree);
            _missionPanel = new PlayerHUDMissionPanel(_loc);
            _missionPanel.Initialize(tree);

            Button programmatorButton = tree.Q<Button>("ProgrammatorButton") ??
                throw new InvalidOperationException("[PlayerHUD] ProgrammatorButton is missing from PlayerHUD.uxml.");
            programmatorButton.text = _loc.Get("hud.programmator");
            programmatorButton.clicked += () => _programmatorGrid?.Show();
        }

        private void RefreshAll()
        {
            if (this == null)
            {
                return;
            }

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
                _levelLabel.text = _isLoaded ? _loc.Get("hud.level", stats.Level) : _loc.Get("hud.level_unknown");
            }

            if (_hpLabel != null)
            {
                string hpPrefix = _loc.Get("hud.health");
                _hpLabel.text = _isLoaded ? $"{hpPrefix}: {stats.Health:N0} / {stats.MaxHealth:N0}" : $"{hpPrefix}: -- / --";
                _hpLabel.style.opacity = 1;
            }

            float pct = stats.HealthPercent;
            if (_hpPercentLabel != null)
            {
                _hpPercentLabel.text = $"{pct * 100f:F0}%";
            }

            if (_hpBarFill != null)
            {
                _hpBarFill.style.width = new Length(pct * 100, LengthUnit.Percent);
                _hpBarFill.style.backgroundColor = pct < 0.25f ? _hpBarLowColor : _hpBarFillColor;
            }

            if (_moneyLabel != null)
            {
                _moneyLabel.text = _isLoaded ? $"{stats.Money:N0}" : "---";
            }

            if (_credsLabel != null)
            {
                _credsLabel.text = _isLoaded ? $"{stats.Creds:N0}" : "---";
            }

            if (_geologyLabel != null)
            {
                _geologyLabel.text = string.IsNullOrEmpty(stats.GeologyText) || !_isLoaded
                    ? _loc.Get("hud.geology_zero")
                    : _loc.Get("hud.geology", stats.GeologyCurrent, stats.GeologyMax, stats.GeologyText);
            }

            if (_basketPercentLabel != null)
            {
                _basketPercentLabel.text = _isLoaded ? $"{stats.BasketMaxPercent}%" : "--%";
            }

            _basketView.Refresh(stats);
        }

        private void OnSkillProgress(SkillType skill, long current, long max)
        {
            _skillGrid.UpdateSkillProgress(skill, current, max);
        }
    }
}
