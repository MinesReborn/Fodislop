#nullable enable

using System;

using Kern.Core.Localization;
using Kern.Core.Interfaces;
using Kern.UI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

using Kern.UI.Localization;
using Kern.UI.Screens;
using Kern.Core;


namespace Kern.Bootstrap.Scenes
{
    [RequireComponent(typeof(UIDocument))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Core", sourceAssembly: null, sourceClassName: "BootstrapLoadingScreen")]
    public sealed class BootstrapLoadingScreen : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private BootstrapLifetimeScope _bootstrap = null!;

        [Inject]
        private ILocalizationService _localization = null!;

        private VisualElement? _overlay;
        private Label? _phase;
        private VisualElement? _meter;
        private VisualElement? _fill;
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

            EnsureVisualElements();
            _bootstrap.TransitionChanged += OnTransitionChanged;
            _localization.RegisterLocalizable(this);
            _initialized = true;
            Hide();
        }

        private void EnsureVisualElements()
        {
            UIDocument document = GetComponent<UIDocument>();
            document.sortingOrder = 200;
            if (!document.enabled)
            {
                document.enabled = true;
            }

            VisualElement root = document.rootVisualElement;
            if (_overlay != null && root.Contains(_overlay))
            {
                return;
            }

            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>(
                ResourcePaths.BootstrapLoadingScreenUxml)
                ?? throw new System.InvalidOperationException("Required UI resource 'UI/Loading/Bootstrap/Templates/BootstrapLoadingScreen' was not found.");

            VisualElement tree = asset.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            tree.pickingMode = PickingMode.Ignore;
            root.Add(tree);

            // Конфиг на загрузочном экране ещё не прочитан: множитель масштаба
            // остаётся тем, что уже стоит у панели.
            UIScreen.Bind(document, null);
            UILocalizer.Apply(tree, _localization);
            _overlay = tree.Q<VisualElement>("BootstrapLoadingOverlay");
            _phase = tree.Q<Label>("BootstrapLoadingPhase");
            _meter = tree.Q<VisualElement>("BootstrapLoadingMeter");
            _fill = tree.Q<VisualElement>("BootstrapLoadingFill");
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
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                UIScreen.Unbind(document);
            }

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
                case SceneTransitionPhase.Loading:
                case SceneTransitionPhase.Attached:
                case SceneTransitionPhase.ActivationRequested:
                case SceneTransitionPhase.StartupReady:
                case SceneTransitionPhase.PresentationReady:
                case SceneTransitionPhase.CleaningPrevious:
                    break;
                default:
                    Debug.LogError(
                        $"[BootstrapLoadingScreen] Фаза перехода {status.Phase} не разобрана; " +
                        "если она терминальная, экран загрузки останется висеть. " +
                        "Добавьте её в switch явно.");
                    break;
            }
        }

        private void Show(string sceneName)
        {
            if (string.Equals(
                    sceneName,
                    SceneNames.MainGame,
                    System.StringComparison.Ordinal))
            {
                Hide();
                return;
            }

            EnsureVisualElements();
            if (_phase != null)
            {
                // Имя сцены игроку не показывается: это внутреннее устройство клиента
                _phase.text = _localization.Get("assetload.resources");
            }

            ClearProgress();

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
            EnsureVisualElements();
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

        // Доля готовности 0…1: шкала ступенями (meter-quant). Пока доля не известна — шкалы нет, только фаза
        public void SetProgress(float progress)
        {
            EnsureVisualElements();
            _meter?.RemoveFromClassList("is-idle");
            if (_fill != null)
            {
                _fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            }
        }

        private void ClearProgress()
        {
            _meter?.AddToClassList("is-idle");
            if (_fill != null)
            {
                _fill.style.width = Length.Percent(0f);
            }
        }

        public void SetPhaseText(string message)
        {
            EnsureVisualElements();
            if (_phase != null)
            {
                _phase.text = message;
            }
        }
    }
}
