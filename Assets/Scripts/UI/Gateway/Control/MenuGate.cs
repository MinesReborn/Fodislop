#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.UserInterface;
using Kern.Networking.Authentication;
using Kern.UI.Localization;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Gateway
{
    // Ворота входа и онбординг — слой поверх главного меню, а не отдельная сцена.
    // Сцен три: Bootstrap → MainMenu → MainGame; вход, онбординг и меню — экраны
    // одного документа, как в макете (visual/kern-ui: boot | auth · onboarding · menu | game).
    // Меню строится под слоем; пока ворота открыты, слой перехватывает ввод целиком.
    public sealed class MenuGate : ILocalizableUI, IDisposable
    {
        // Состояние ворот. Ровно один класс на корне за раз: иначе ничто не мешало
        // бы показать вход и онбординг одновременно.
        private const string StateAuthClass = "gateway--auth";
        private const string StateOnboardingClass = "gateway--onboarding";
        private const string LayerClass = "menu-gate";

        private readonly IClientConfigManager _clientConfig;
        private readonly ILocalizationService _loc;
        private readonly IGameTokenStore _tokens;
        private readonly Kern.Rendering.Display.DisplayManager _display;
        private readonly IPlayerStats _stats;

        private UIDocument? _document;
        private VisualElement? _layer;
        private VisualElement? _gatewayRoot;
        private AuthGate? _authGate;
        private GatewayOnboarding? _onboarding;
        private bool _registered;

        public MenuGate(
            IClientConfigManager clientConfig,
            ILocalizationService loc,
            IGameTokenStore tokens,
            Kern.Rendering.Display.DisplayManager display,
            IPlayerStats stats)
        {
            _display = display;
            _stats = stats;
            _clientConfig = clientConfig;
            _loc = loc;
            _tokens = tokens;
        }

        // Ворота пройдены в этой сцене: перестройка меню не открывает их снова.
        public bool Passed { get; private set; }

        public bool IsOpen => _layer?.parent != null;

        // forceForm — смена аккаунта: форма входа показывается даже с сохранённым токеном.
        public void Open(UIDocument document, VisualElement host, bool forceForm)
        {
            Close();
            Passed = false;
            _document = document;

            var asset = Resources.Load<VisualTreeAsset>(ResourcePaths.GatewayUxml)
                ?? throw new InvalidOperationException($"Required UI asset '{ResourcePaths.GatewayUxml}' was not found.");

            VisualElement layer = asset.CloneTree();
            layer.AddToClassList("ui-fullscreen");
            layer.AddToClassList(LayerClass);
            layer.pickingMode = PickingMode.Position;
            host.Add(layer);
            _layer = layer;

            // Векторная микрографика слоя — сразу, а не на следующем пересмотре дерева
            KernVector.Attach(layer);

            // Статические ключи Uxml резолвятся сразу, а не только по смене языка.
            UILocalizer.Apply(layer, _loc);
            _gatewayRoot = layer.Q<VisualElement>("GatewayRoot") ?? layer;

            _authGate = AuthGate.TryCreate(layer, _clientConfig, _tokens, _loc)
                ?? throw new InvalidOperationException("[MenuGate] Разметка ворот входа неполна: Gateway.uxml не содержит формы входа.");
            _authGate.Passed += OnAuthPassed;
            _onboarding = GatewayOnboarding.TryCreate(layer, _clientConfig, _loc, OnOnboardingFinished, ApplyUIScale, document, _display, _stats);

            if (!_registered)
            {
                // Реестр применяет текст сразу и на каждой смене языка.
                _loc.RegisterLocalizable(this);
                _registered = true;
            }

            SetState(StateAuthClass);
            _authGate.Show(forceForm);
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(MenuGate));
            if (_layer == null)
            {
                return;
            }

            UILocalizer.Apply(_layer, _loc);
            _onboarding?.ApplyLocalizedText();
            UILocalizer.AssertLocalized(_layer, _loc);
        }

        public void Dispose()
        {
            Close();
            if (_registered)
            {
                _loc.UnregisterLocalizable(this);
                _registered = false;
            }
        }

        private void OnAuthPassed()
        {
            bool alreadyDone = !GatewayDevFlags.ForceGates && _clientConfig.Config.Interface.OnboardingDone;
            if (alreadyDone || _onboarding == null)
            {
                Finish();
                return;
            }

            SetState(StateOnboardingClass);
            _onboarding.Show();
        }

        private void OnOnboardingFinished()
        {
            _clientConfig.UpdateSection(config => config.Interface, settings => settings.OnboardingDone = true);
            Finish();
        }

        private void Finish()
        {
            Passed = true;
            Close();
        }

        private void Close()
        {
            if (_authGate != null)
            {
                _authGate.Passed -= OnAuthPassed;
                _authGate = null;
            }

            _onboarding = null;
            _gatewayRoot = null;
            _layer?.RemoveFromHierarchy();
            _layer = null;
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

        private void ApplyUIScale(float scale)
        {
            if (_document == null || _document.panelSettings == null)
            {
                return;
            }

            UIScreen.Bind(_document, UIScalePolicy.Clamp(scale), _clientConfig.Config.Interface.TextScale);
        }
    }
}
