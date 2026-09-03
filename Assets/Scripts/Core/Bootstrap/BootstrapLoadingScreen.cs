#nullable enable

using Fodinae.Core.Localization;
using Fodinae.Core.Interfaces;
using Fodinae.UI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.Core
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BootstrapLoadingScreen : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private BootstrapLifetimeScope _bootstrap = null!;

        [Inject]
        private ILocalizationService _localization = null!;

        private VisualElement? _overlay;
        private Label? _phase;
        private bool _initialized;

        private void OnEnable()
        {
            UIDocument document = GetComponent<UIDocument>();
            VisualElement? root = document.rootVisualElement;
            if (root != null)
            {
                root.pickingMode = PickingMode.Ignore;
            }

            if (_overlay != null && UIState.IsHidden(_overlay))
            {
                _overlay.pickingMode = PickingMode.Ignore;
            }
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            UIDocument document = GetComponent<UIDocument>();
            document.sortingOrder = 200;
            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.BootstrapLoadingScreenUxml)
                ?? throw new System.InvalidOperationException("Required UI resource 'UI/BootstrapLoadingScreen' was not found.");

            VisualElement root = document.rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            VisualElement tree = asset.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            root.Add(tree);
            UILayoutTier.Attach(tree);
            UILocalizer.Apply(tree, _localization);
            _overlay = tree.Q<VisualElement>("BootstrapLoadingOverlay");
            _phase = tree.Q<Label>("BootstrapLoadingPhase");

            _bootstrap.TransitionChanged += OnTransitionChanged;
            _localization.RegisterLocalizable(this);
            _initialized = true;
            Hide();
        }

        public void ApplyLocalizedText()
        {
            if (_overlay?.parent != null)
            {
                UILocalizer.Apply(_overlay.parent, _localization);
            }
        }

        private void OnDestroy()
        {
            if (_bootstrap != null)
            {
                _bootstrap.TransitionChanged -= OnTransitionChanged;
            }

            _localization?.UnregisterLocalizable(this);
        }

        private void OnTransitionChanged(SceneTransitionStatus status)
        {
            switch (status.Phase)
            {
                case SceneTransitionPhase.Created:
                    Show(status.TargetSceneName);
                    break;
                case SceneTransitionPhase.Completed:
                case SceneTransitionPhase.CompletedWithWarnings:
                case SceneTransitionPhase.Failed:
                    Hide();
                    break;
            }
        }

        private void Show(string sceneName)
        {
            // The MainMenu -> MainGame transition is owned entirely by the MainMenu
            // descent screen and loader (LoaderContainer with planet animation & phase steps).
            // Do not show the generic bootstrap overlay over it.
            if (string.Equals(
                    sceneName,
                    ProjectRuntimeContracts.SceneNames.MainGame,
                    System.StringComparison.Ordinal))
            {
                Hide();
                return;
            }

            if (_phase != null)
            {
                _phase.text = $"{_localization.Get("network.connecting")} {sceneName}";
            }

            UIState.Show(_overlay);
            if (_overlay != null)
            {
                _overlay.pickingMode = PickingMode.Position;
            }
        }

        private void Hide()
        {
            UIState.Hide(_overlay);
            if (_overlay != null)
            {
                _overlay.pickingMode = PickingMode.Ignore;
            }
        }

        public void ShowDirect(string message)
        {
            if (_phase != null)
            {
                _phase.text = message;
            }

            UIState.Show(_overlay);
            if (_overlay != null)
            {
                _overlay.pickingMode = PickingMode.Position;
            }
        }

        public void SetPhaseText(string message)
        {
            if (_phase != null)
            {
                _phase.text = message;
            }
        }
    }
}
