#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking.Auth;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class GatewayController : MonoBehaviour, ILocalizableUI
    {
        private const string MainMenuSceneName = ProjectRuntimeContracts.SceneNames.MainMenu;

        // Состояние ворот. Ровно один класс на корне за раз: раньше видимость
        // была своя у каждого слоя, и ничто не мешало показать вход и онбординг
        // одновременно — онбординг просто ложился поверх формы.
        private const string StateAuthClass = "gateway--auth";
        private const string StateOnboardingClass = "gateway--onboarding";

        private UIDocument _document = null!;
        private VisualElement _root = null!;
        private VisualElement? _gatewayRoot;
        private AuthGate? _authGate;
        private GatewayOnboarding? _onboarding;
        private bool _leaving;
        private bool _initialized;

        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IAuthenticationService _authentication = null!;

        private void OnEnable()
        {
            // Первичная сборка — в Start(): к нему гарантированы и инжекция
            // (мост, фаза Awake), и панель UIDocument (создаётся в OnEnable
            // документа). Здесь — только реактивация уже построенного UI:
            // переприменяем текст, не перестраивая.
            if (_initialized && _root != null && _loc != null)
            {
                _loc.RegisterLocalizable(this);
                ApplyLocalizedText();
            }
        }

        public void InitializeScene()
        {
            if (_initialized)
            {
                return;
            }

            if (_clientConfig == null || _loc == null)
            {
                // К Start инжекция гарантирована (мост, фаза Awake); отсутствие
                // зависимостей здесь — дефект, а не гонка.
                throw new InvalidOperationException(
                    "[Gateway] DI-инжекция не произошла до scene entry — вьюха строила бы UI без зависимостей.");
            }

            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                // К Start панель гарантирована: UIDocument создаёт её в своём
                // OnEnable, а Start выполняется после всех OnEnable сцены.
                throw new InvalidOperationException(
                    "[Gateway] UIDocument panel is not available at Start (панель создаётся в OnEnable документа и к Start обязана существовать).");
            }

            var asset = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.GatewayUxml);
            if (asset == null)
            {
                Debug.LogWarning($"[Gateway] UI resource '{ProjectRuntimeContracts.ResourcePaths.GatewayUxml}' is missing; returning to main menu.");
                GoToMainMenu();
                return;
            }

            _root = _document.rootVisualElement;
            _initialized = true;
            _root.Clear();

            VisualElement tree = asset.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            // Статические ключи Uxml резолвятся сразу при сборке, а не только
            // по событию смены языка — иначе ворота показали бы сырые ключи.
            UILocalizer.Apply(tree, _loc);

            // Тир раскладки вместо @media — как и в остальных экранах.
            UILayoutTier.Attach(tree);
            _root = tree;

            // Состояние ставится на тот же элемент, на котором оно задано в
            // разметке. Иначе начальный gateway--auth из Uxml снять было бы
            // некому и форма входа осталась бы видимой поверх онбординга.
            _gatewayRoot = _root.Q<VisualElement>("GatewayRoot") ?? _root;

            _authGate = AuthGate.TryCreate(_root, _clientConfig, _authentication, _loc);
            if (_authGate == null)
            {
                Debug.LogWarning("[Gateway] Ворота входа не собрались — сразу уходим в меню.");
                GoToMainMenu();
                return;
            }

            _authGate.Passed += OnAuthPassed;

            _onboarding = GatewayOnboarding.TryCreate(
                _root,
                _clientConfig,
                _loc,
                OnOnboardingFinished,
                ApplyUIScale);

            ApplySavedUIScale();

            SetState(StateAuthClass);
            _authGate.Show();

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            _loc.RegisterLocalizable(this);
            Debug.Log("[Gateway] Gateway UI initialized and displayed.");
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(GatewayController));
            if (_root == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_root, _loc);
            _onboarding?.ApplyLocalizedText();
            UILocalizer.AssertLocalized(_root, _loc);
        }

        private void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }
        }

        private void OnAuthPassed()
        {
            bool alreadyDone = !GatewayDevFlags.ForceGates
                && _clientConfig.Config.Interface.OnboardingDone;

            if (alreadyDone || _onboarding == null)
            {
                GoToMainMenu();
                return;
            }

            SetState(StateOnboardingClass);
            _onboarding.Show();
        }

        private void OnOnboardingFinished()
        {
            _clientConfig.UpdateSection(config => config.Interface, settings => settings.OnboardingDone = true);
            GoToMainMenu();
        }

        private void SetState(string state)
        {
            if (_gatewayRoot == null)
            {
                return;
            }

            _gatewayRoot.EnableInClassList(StateAuthClass, state == StateAuthClass);
            _gatewayRoot.EnableInClassList(StateOnboardingClass, state == StateOnboardingClass);
        }

        private void ApplySavedUIScale()
        {
            if (_clientConfig == null)
            {
                return;
            }

            float saved = _clientConfig.Config.Interface.UIScale;

            // Ноль означает «в конфиге ничего нет» — множитель ноль погасил бы
            // весь интерфейс, поэтому такое значение трактуем как штатное.
            ApplyUIScale(UIScaleUtility.ResolveEffectiveScale(saved));
        }

        private void ApplyUIScale(float scale)
        {
            PanelSettings? panel = _document.panelSettings;
            if (panel == null)
            {
                return;
            }

            // Диапазон тот же, что проверяет ClientConfigManager.
            panel.scale = UIScaleUtility.Clamp(scale);
        }

        // ─────────────────────────────────────────────────────────────
        // Переход в меню
        // ─────────────────────────────────────────────────────────────

        private void GoToMainMenu()
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;
            _operations.Run("gateway_to_main_menu", LoadMainMenuAsync);
        }

        private async UniTask LoadMainMenuAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                destroyCancellationToken);
            await _sceneNavigator.TransitionAsync(
                MainMenuSceneName,
                linkedCancellation.Token);
        }
    }
}
