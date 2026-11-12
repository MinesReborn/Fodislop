#nullable enable

using System;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.UserInterface;
using Kern.UI.Localization;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Gateway
{
    // Ворота входа и онбординг — слой поверх главного меню, а не отдельная сцена.
    // Авторизация — серверное окно поверх этого слоя (Theme=Kern, присылается
    // после подключения, GatewayWindowProcessor): слой блокирует ввод меню,
    // пока сессия не подтверждена. После подтверждения — онбординг (первый
    // запуск), затем слой закрывается и меню становится интерактивным.
    public sealed class MenuGate : ILocalizableUI, IDisposable
    {
        // Состояние ворот. Ровно один класс на корне за раз: онбординг скрыт,
        // пока идёт авторизация; локальная форма входа удалена — вход теперь
        // серверное окно.
        private const string StateAuthClass = "gateway--auth";
        private const string StateOnboardingClass = "gateway--onboarding";
        private const string LayerClass = "menu-gate";
        private const long AuthPollMs = 200;


        private readonly IClientConfigManager _clientConfig;
        private readonly ILocalizationService _loc;
        private readonly ILocalPlayerState _localPlayer;
        private readonly Kern.Rendering.Display.DisplayManager _display;
        private readonly IPlayerStats _stats;

        private UIDocument? _document;
        private VisualElement? _layer;
        private VisualElement? _gatewayRoot;
        private GatewayOnboarding? _onboarding;
        private bool _registered;

        public MenuGate(
            IClientConfigManager clientConfig,
            ILocalizationService loc,
            ILocalPlayerState localPlayer,
            Kern.Rendering.Display.DisplayManager display,
            IPlayerStats stats)
        {
            _display = display;
            _stats = stats;
            _clientConfig = clientConfig;
            _loc = loc;
            _localPlayer = localPlayer;
        }

        // Ворота пройдены в этой сцене: перестройка меню не открывает их снова.
        public bool Passed { get; private set; }

        public bool IsOpen => _layer?.parent != null;

        public void Open(UIDocument document, VisualElement host, bool forceOnboarding = false)
        {
            Close();
            Passed = false;
            _document = document;

            bool alreadyDone = !GatewayDevFlags.ForceGates && _clientConfig.Config.Interface.OnboardingDone;
            if (alreadyDone && !forceOnboarding)
            {
                // Онбординг пройден: ворота не строятся. Авторизация — серверное
                // окно, к воротам она больше не относится.
                Passed = true;
                return;
            }

            var asset = Resources.Load<VisualTreeAsset>(ResourcePaths.GatewayUxml)
                ?? throw new InvalidOperationException($"Required UI asset '{ResourcePaths.GatewayUxml}' was not found.");

            VisualElement layer = asset.CloneTree();
            layer.AddToClassList("ui-fullscreen");
            layer.AddToClassList(LayerClass);
            layer.pickingMode = PickingMode.Position;
            host.Add(layer);
            _layer = layer;

            if (layer.Q<VisualElement>("OnboardingOverlay") == null)
            {
                // Диагностика импорта: в дереве клона нет онбординга — печатаем,
                // что реально приехало из ассета, чтобы отличить stale-импорт
                // и битый UXML от ошибки в запросе.
                var dumped = string.Join(" | ", layer.Query<VisualElement>().Build()
                    .Take(10).Select(e => string.IsNullOrEmpty(e.name) ? $"«{e.GetType().Name}»" : e.name));
                throw new InvalidOperationException(
                    $"[MenuGate] Gateway.uxml не содержит OnboardingOverlay (children: {layer.childCount}: {dumped}). " +
                    "Проверьте ошибки импорта UXML в Console.");
            }

            // Векторная микрографика слоя — сразу, а не на следующем пересмотре дерева
            KernVector.Attach(layer);

            // Статические ключи Uxml резолвятся сразу, а не только по смене языка.
            UILocalizer.Apply(layer, _loc);
            _gatewayRoot = layer.Q<VisualElement>("GatewayRoot") ?? layer;

            _onboarding = GatewayOnboarding.TryCreate(layer, _clientConfig, _loc, OnOnboardingFinished, ApplyUIScale, document, _display, _stats)
                ?? throw new InvalidOperationException("[MenuGate] Разметка онбординга неполна: Gateway.uxml не содержит шагов онбординга.");

            if (!_registered)
            {
                // Реестр применяет текст сразу и на каждой смене языка.
                _loc.RegisterLocalizable(this);
                _registered = true;
            }

            // Онбординг — после авторизации: пока сессия не подтверждена,
            // слой в состоянии входа (онбординг скрыт), а форму входа рисует
            // сервер поверх ворот. Как только токен подтверждён — онбординг.
            SetState(StateAuthClass);

            if (GatewayDevFlags.ForceGates || _localPlayer.IsAuthenticated)
            {
                ShowOnboarding();
                return;
            }

            _layer.schedule.Execute(WaitForAuthentication).Every(AuthPollMs);
        }

        private void WaitForAuthentication()
        {
            if (_layer == null || _onboarding == null || Passed)
            {
                return;
            }

            if (!_localPlayer.IsAuthenticated)
            {
                return;
            }

            ShowOnboarding();
        }

        private void ShowOnboarding()
        {
            SetState(StateOnboardingClass);
            _onboarding?.Show();
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
