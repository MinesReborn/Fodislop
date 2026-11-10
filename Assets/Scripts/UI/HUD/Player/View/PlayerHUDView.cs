#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.Models;
using Kern.AssetPipeline.Loading;
using Kern.Networking;
using Kern.Player.Interfaces;
using Kern.Game.Player.Stats;
using Kern.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.Programmator;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

using Kern.Game.Player.Input;
using Kern.Networking.Processing;
using Kern.UI.Chat;
using Kern.UI.Input;
using Kern.UI.Localization;
using Kern.UI.Map;
using Kern.UI.Tooltips;

namespace Kern.UI.HUD
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI.HUD.Player.View", sourceAssembly: null, sourceClassName: "PlayerHUDView")]
    public class PlayerHUDView : MonoBehaviour, ILocalizableUI
    {
        private readonly PlayerHUDStatusPanel _statusPanel = new();
        private readonly PlayerHUDSkillGrid _skillGrid = new();
        private readonly PlayerHUDBasketView _basketView = new();
        private PlayerHUDMissionPanel _missionPanel = null!;
        private PlayerHUDPopups _popups = null!;

        [Inject]
        private UIDocument _doc = null!;
        private Tooltip? _tooltip;
        private PlayerHUDStatsPresenter _statsPresenter = null!;
        [Inject]
        private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private Kern.Core.Interfaces.ILocalPlayerState _localPlayer = null!;
        private PlayerHUDModeController? _modeController;
        private VisualElement? _hudContent;
        private TemplateContainer? _hudRoot;
        private bool _isVisible = true;

        private VisualElement? _clanIcon;
        private int _clanIconId;
        private VisualElement? _skillContainer;

        private ProgrammatorGrid? _programmatorGrid;
        private readonly PlayerHUDProgramControls _programControls = new();
        [Inject]
        private ProgrammatorData? _programmatorData;
        private bool _initializationStarted;

        [Inject]
        private IPlayerStats _model = null!;
        [Inject]
        private GlobalChatUI _globalChatUI = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private IWebAssetLoader _webAssetLoader = null!;
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
        private PlayerInputHandler _playerInput = null!;

        [Inject]
        private TouchCommands _touchCommands = null!;
        [Inject]
        private MapModeState _mapModeState = null!;
        private PlayerHUDTouchControls? _touchControls;

        protected void Start()
        {
            TryStartInitialization();
        }

        public void EnsureInitialized()
        {
            TryStartInitialization();
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (!visible)
            {
                CancelMobileInput();
            }

            if (_hudContent != null)
            {
                SetDisplayed(_hudContent, visible);
            }
            else if (_hudRoot != null)
            {
                SetDisplayed(_hudRoot, visible);
            }
        }

        protected void Update()
        {
            if (_doc != null && !_doc.enabled)
            {
                CancelMobileInput();
                return;
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlockedExcludingMapMode)
            {
                CancelMobileInput();
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
                _globalChatUI == null || _assetLoader == null || _webAssetLoader == null || _networkService == null ||
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

            // Значки кристаллов догружаются после первых данных груза: ряд уже стоит, ставим им картинки
            _basketView.ApplyTextures();
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
            _statsPresenter.Refresh();
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
            _touchControls?.Dispose();
            _touchControls = null;

            _programControls.Dispose();

            _programmatorGrid?.Dispose();
            _programmatorGrid = null;
            _skillGrid.ClearSchedules();
            _statusPanel.ClearSchedules();

            if (_model != null)
            {
                _model.OnStatsChanged -= OnStatsChanged;
                _model.OnSkillProgress -= OnSkillProgress;
                _model.OnStatusLinesChanged -= OnStatusLinesChanged;
                _model.OnMissionChanged -= OnMissionChanged;
            }

            if (_globalChatUI != null)
            {
                _globalChatUI.Hide();
            }
        }

        private void OnStatusLinesChanged() => _statusPanel.Rebuild(_model);

        private void OnStatsChanged()
        {
            _statsPresenter.Refresh();
            RefreshClanIcon();
        }

        // Знак клана — картинка Visual/Clan/{id}.png, как над роботами (RobotAssetLoader.ClanPath); без клана рамка пустая
        private void RefreshClanIcon()
        {
            if (_clanIcon == null || _model == null || _model.ClanId == _clanIconId)
            {
                return;
            }

            int clanId = _model.ClanId;
            _clanIconId = clanId;
            _clanIcon.style.backgroundImage = StyleKeyword.Null;
            if (clanId <= 0 || clanId > byte.MaxValue)
            {
                return;
            }

            _operations.Run("player_hud_clan_icon", async token =>
            {
                Texture2D? texture = await _assetLoader.GetTextureAsync(
                    Kern.Game.Entities.Robots.RobotAssetLoader.ClanPath((byte)clanId), token);
                if (_clanIcon != null && _clanIconId == clanId && texture != null)
                {
                    _clanIcon.style.backgroundImage = new StyleBackground(texture);
                }
            });
        }
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
                _model.OnStatsChanged += OnStatsChanged;
            }

            OnStatsChanged();

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
                ResourcePaths.PlayerHudUxml) ??
                throw new InvalidOperationException(
                    "[PlayerHUD] Resources/UI/HUD/Player/Templates/PlayerHUD.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            _hudRoot = tree;
            root.Add(tree);

            InitializeTouchControls(tree);

            _hudContent = tree.Q<VisualElement>("HUDContent") ?? _hudRoot;
            SetDisplayed(_hudContent, _isVisible);

            UILocalizer.Apply(tree, _loc);

            Button clanButton = tree.Q<Button>("ClanButton") ??
                throw new InvalidOperationException("[PlayerHUD] ClanButton is missing from PlayerHUD.uxml.");
            clanButton.clicked += () => _networkService?.Send(new OpenClanClickPacket());
            _clanIcon = tree.Q<VisualElement>("ClanIcon") ??
                throw new InvalidOperationException("[PlayerHUD] ClanIcon is missing from PlayerHUD.uxml.");

            Button dropBoxButton = tree.Q<Button>("DropBoxButton") ??
                throw new InvalidOperationException("[PlayerHUD] DropBoxButton is missing from PlayerHUD.uxml.");
            dropBoxButton.clicked += () => _networkService?.Send(new DropBoxClickPacket());
            Button donateButton = tree.Q<Button>("DonateButton") ??
                throw new InvalidOperationException("[PlayerHUD] DonateButton is missing from PlayerHUD.uxml.");
            donateButton.clicked += () => _networkService?.Send(
                new ElementClickPacket("donate", 0, Array.Empty<StringPairPacket>()));

            // «Меню» — пауза: тот же запрос, что у клавиши Esc и кнопки меню на пальце (PauseMenu)
            Button menuButton = tree.Q<Button>("SysMenuButton") ??
                throw new InvalidOperationException("[PlayerHUD] SysMenuButton is missing from PlayerHUD.uxml.");
            menuButton.clicked += () => _touchCommands?.RequestPause();

            VisualElement basketContainer = tree.Q<VisualElement>("BasketContainer") ??
                throw new InvalidOperationException("[PlayerHUD] BasketContainer is missing from PlayerHUD.uxml.");
            _basketView.Initialize(basketContainer);

            _skillContainer = tree.Q<VisualElement>("SkillContainer") ??
                throw new InvalidOperationException("[PlayerHUD] SkillContainer is missing from PlayerHUD.uxml.");
            _skillGrid.Initialize(_skillContainer);

            _modeController = new PlayerHUDModeController(_localPlayer, _loc, _networkService);
            _modeController.Initialize(tree, _tooltip!);

            // Кнопки чата в HUD нет: глобальный чат открывается по TAB
            // внутри GlobalChatUI.Update.

            _popups = new PlayerHUDPopups(
                packet => _networkService?.SendAction(packet),
                packet => _networkService?.Send(packet),
                packet => _networkService?.Send(packet),
                packet => _networkService?.Send(packet));
            _popups.Initialize(tree);

            _statusPanel.Initialize(tree);
            _missionPanel = new PlayerHUDMissionPanel(_loc, _assetLoader, _webAssetLoader, _operations);
            _missionPanel.Initialize(tree);

            _statsPresenter = new PlayerHUDStatsPresenter(_model, _basketView, tree);

            _programControls.Initialize(tree, _programmatorGrid, _programmatorProtocol, _loc, _tooltip!);
        }

        private void InitializeTouchControls(VisualElement tree)
        {
            _touchControls = new PlayerHUDTouchControls(_playerInput, _inputBlocker, _localPlayer, _touchCommands, _mapModeState);
            _touchControls.Bind(tree);
        }

        private static void SetDisplayed(VisualElement? element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void CancelMobileInput()
        {
            _touchControls?.Cancel();
        }

        private void OnSkillProgress(SkillType skill, long current, long max)
        {
            _skillGrid.UpdateSkillProgress(skill, current, max);
        }
    }
}
