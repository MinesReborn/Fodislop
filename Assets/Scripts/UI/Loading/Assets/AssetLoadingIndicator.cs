#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.World.Terrain;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

using Kern.Game.Session;
using Kern.UI.Diagnostics;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Loading
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "AssetLoadingIndicator")]
    public sealed class AssetLoadingIndicator : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private FPSCounter _fpsCounter = null!;

        [Inject]
        private UIDocument _document = null!;

        [Inject]
        private TerrainRenderer _terrainRenderer = null!;

        [Inject]
        private ILocalizationService _loc = null!;

        [Inject]
        private GameManager _gameManager = null!;
        private VisualElement? _root;
        private VisualElement? _loadingOverlay;
        private Label? _loadingSpinnerLabel;
        private Label? _loadingStatusLabel;
        private Label? _loadingProgressLabel;
        private VisualElement? _topAssetFill;
        private VisualElement? _topAssetPill;
        private int _batchPeak;
        private Label? _topAssetLabel;
        private IVisualElementScheduledItem? _spinnerSchedule;
        private bool _loadingOverlayVisible;
        private float _nextRefreshTime;
        private bool _initialized;

        private void OnEnable()
        {
            // The scene scope owns this view and its GameManager. Re-activation
            // must not perform a late container lookup or create a second binding.
        }

        private void Start()
        {
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            // All six dependencies are required [Inject] registrations populated
            // during scope build, before Start. A null here is a wiring defect,
            // not a transient race: silently skipping would leave the loading
            // indicator permanently dead with no error to diagnose.
            string? missing =
                _gameManager == null ? nameof(GameManager) :
                _assetLoader == null ? nameof(IAssetLoader) :
                _fpsCounter == null ? nameof(FPSCounter) :
                _document == null ? nameof(UIDocument) :
                _terrainRenderer == null ? nameof(TerrainRenderer) :
                _loc == null ? nameof(ILocalizationService) :
                null;
            if (missing != null)
            {
                throw new InvalidOperationException(
                    $"[AssetLoadingIndicator] Required injection '{missing}' is missing. " +
                    "The Game scope must register it before AssetLoadingIndicator.Start runs.");
            }

            // The throw above guards these required injections; the compiler
            // cannot narrow fields through the string? 'missing' pattern, so the
            // dereferences are null-forgiven here.
            GameManager gameManager = _gameManager!;
            ILocalizationService loc = _loc!;
            CreateUI();

            if (_root == null)
            {
                throw new InvalidOperationException(
                    "[AssetLoadingIndicator] UI root was not created during Start.");
            }

            _initialized = true;
            gameManager.OnWorldLoaded += OnWorldLoaded;

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            loc.RegisterLocalizable(this);

            if (!gameManager.IsWorldLoaded && gameManager.IsUIAuthorized)
            {
                ShowLoadingOverlay();
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _spinnerSchedule?.Pause();
            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            _topAssetFill = null;
            _topAssetPill = null;
            _topAssetLabel = null;
            _root?.RemoveFromHierarchy();
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + 0.25f;

            if (_document?.rootVisualElement != null && _root != null && !_document.rootVisualElement.Contains(_root))
            {
                _document.rootVisualElement.Add(_root);
            }

            if (_topAssetLabel == null && _document?.rootVisualElement != null)
            {
                _topAssetFill = _document.rootVisualElement.Q<VisualElement>("AssetStatusFill");
                _topAssetPill = _document.rootVisualElement.Q<VisualElement>("AssetStatusPill");
                _topAssetLabel = _document.rootVisualElement.Q<Label>("AssetStatusLabel");
            }

            if (_root != null && _loadingOverlayVisible && _loadingStatusLabel != null)
            {
                string statusText = GetLoadingStatusText();
                if (_loadingStatusLabel.text != statusText)
                {
                    _loadingStatusLabel.text = statusText;
                }
            }

            Refresh();
        }

        private string GetLoadingStatusText()
        {
            if (_loc == null)
            {
                return string.Empty;
            }

            if (_gameManager == null || !_gameManager.IsUIAuthorized)
            {
                return _loc.Get("assetload.init");
            }

            bool terrainReady = _terrainRenderer?.IsReadyForGameplay ?? false;

            if (!terrainReady)
            {
                return _loc.Get("assetload.terrain");
            }

            if (_assetLoader == null)
            {
                return _loc.Get("assetload.resources");
            }

            int pending = _assetLoader.PendingAssetCount;
            int queued = _assetLoader.QueuedAssetCount;

            return pending > 0 || queued > 0
                ? _loc.Get("assetload.assets", pending, queued)
                : _loc.Get("assetload.ready");
        }

        private void OnWorldLoaded()
        {
            HideLoadingOverlay();
        }

        private void ShowLoadingOverlay()
        {
            if (_loadingOverlay == null)
            {
                return;
            }

            UIState.Show(_loadingOverlay);
            _loadingOverlay.pickingMode = PickingMode.Position;
            _loadingOverlayVisible = true;
            _spinnerSchedule?.Resume();
        }

        private void HideLoadingOverlay()
        {
            if (_loadingOverlay == null)
            {
                return;
            }

            UIState.Hide(_loadingOverlay);
            _loadingOverlay.pickingMode = PickingMode.Ignore;
            _loadingOverlayVisible = false;
            _spinnerSchedule?.Pause();
        }

        private void CreateUI()
        {
            if (_document?.rootVisualElement == null)
            {
                throw new InvalidOperationException(
                    "[AssetLoadingIndicator] UIDocument has no root visual element during Start.");
            }

            VisualTreeAsset uiUxml = Resources.Load<VisualTreeAsset>(
                ResourcePaths.AssetLoadingIndicatorUxml) ??
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/Loading/Assets/Templates/AssetLoadingIndicator.uxml' was not found.");

            VisualElement tree = uiUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _document.rootVisualElement.Add(tree);

            _root = tree;
            _loadingOverlay = tree.Q<VisualElement>("LoadingOverlay");
            _loadingSpinnerLabel = tree.Q<Label>("SpinnerLabel");
            _loadingStatusLabel = tree.Q<Label>("StatusLabel");
            _loadingProgressLabel = tree.Q<Label>("ProgressLabel");
            if (_loadingOverlay == null || _loadingSpinnerLabel == null ||
                _loadingStatusLabel == null || _loadingProgressLabel == null)
            {
                throw new InvalidOperationException(
                    "AssetLoadingIndicator.uxml is missing one or more required controls.");
            }

            UILocalizer.Apply(tree, _loc);

            // This root is always present in the shared UIDocument. It must not
            // become a transparent fullscreen input shield while hidden.
            _root.pickingMode = PickingMode.Ignore;
            if (_loadingOverlay != null)
            {
                _loadingOverlay.pickingMode = PickingMode.Ignore;
            }

            VisualElement? loadingRoot = tree.Q<VisualElement>("LoadingRoot");
            if (loadingRoot != null)
            {
                // The layout-only fullscreen container sits between the ignored
                // template root and the toggled overlay; left pickable it becomes
                // a permanent invisible fullscreen input shield over the HUD.
                loadingRoot.pickingMode = PickingMode.Ignore;
            }

            VisualElement? docRoot = _document.rootVisualElement;
            _topAssetFill = docRoot?.Q<VisualElement>("AssetStatusFill");
            _topAssetPill = docRoot?.Q<VisualElement>("AssetStatusPill");
            _topAssetLabel = docRoot?.Q<Label>("AssetStatusLabel");

            StartSpinner();
            Refresh();
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(AssetLoadingIndicator));
            if (_root == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_root, _loc);
            Refresh();
            UILocalizer.AssertLocalized(_root, _loc);
        }

        private void StartSpinner()
        {
            if (_loadingSpinnerLabel == null)
            {
                return;
            }

            string[] frames = new[] {
"\u25D0", "\u25D3", "\u25D1", "\u25D2"
 };
            _spinnerSchedule = _loadingSpinnerLabel.schedule.Execute(() =>
            {
                if (_loadingSpinnerLabel == null)
                {
                    return;
                }

                int frame = (int)(Time.unscaledTime * 4) % 4;
                _loadingSpinnerLabel.text = frames[frame];
            }).Every(250);
        }

        private void Refresh()
        {
            if (_assetLoader == null)
            {
                return;
            }

            if (_loadingStatusLabel != null)
            {
                _loadingStatusLabel.text = GetLoadingStatusText();
            }

            UpdateProgressText();
            UpdateTopStatusPill();
        }

        private void UpdateProgressText()
        {
            if (_loadingProgressLabel == null || _assetLoader == null || _loc == null)
            {
                return;
            }

            int pending = _assetLoader.PendingAssetCount;
            int queued = _assetLoader.QueuedAssetCount;
            _loadingProgressLabel.text = pending > 0 || queued > 0
                ? _loc.Get("assetload.active", pending, queued)
                : string.Empty;
        }

        // Догрузка ресурсов в строке состояния (макет visual/kern-ui .asset-load): тонкая полоса и «загружено / всего»
        // по текущей партии; видна, только пока идёт, по завершении пропадает. Всего — наибольшая очередь партии:
        // загрузчик знает только, сколько осталось
        private void UpdateTopStatusPill()
        {
            if (_topAssetLabel == null || _assetLoader == null)
            {
                return;
            }

            int remaining = _assetLoader.PendingAssetCount + _assetLoader.QueuedAssetCount;
            bool terrainReady = _terrainRenderer?.IsReadyForGameplay ?? true;
            bool loading = remaining > 0 || !terrainReady;
            if (_topAssetPill != null)
            {
                UIState.SetHidden(_topAssetPill, !loading);
            }

            if (!loading)
            {
                _batchPeak = 0;
                return;
            }

            _batchPeak = Math.Max(_batchPeak, remaining);
            int done = _batchPeak - remaining;
            _topAssetLabel.text = _batchPeak > 0 ? done + "/" + _batchPeak : string.Empty;
            if (_topAssetFill != null)
            {
                float share = _batchPeak > 0 ? (float)done / _batchPeak : 0f;
                _topAssetFill.style.width = Length.Percent(share * 100f);
            }
        }
    }
}
