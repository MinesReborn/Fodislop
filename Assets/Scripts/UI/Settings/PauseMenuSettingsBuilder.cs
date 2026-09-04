#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Game;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    internal sealed class PauseMenuSettingsBuilder
    {
        private readonly UIDocument _doc;
        private readonly IClientConfigManager _clientConfig;
        private readonly IAudioSystem _audioSystem;
        private readonly DisplayManager _displayManager;
        private readonly GraphicsSettingsController _graphicsSettings;
        private readonly LightingEngine _lightingEngine;
        private readonly PostProcessController _postProcessController;
        private readonly INetworkService _networkService;
        private readonly IConnectionService _connectionService;
        private readonly ILocalPlayerState _localPlayer;

        // Shared with PauseMenu: opening the settings page replays every
        // refresher so each control re-reads its live value instead of showing
        // whatever was current when the menu was first built.
        private readonly ICollection<Action> _refreshers;

        private readonly Action _closeMenu;
        private readonly ILocalizationService _loc;

        // The custom-profile foldout is created on the graphics page but is
        // also opened from technical settings applied elsewhere, so it has to
        // outlive BuildGraphicsPage.
        private Foldout? _customGraphicsSection;
        private Action? _updateLightingQualityButton;

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        // Created before the graphics/advanced pages so the lighting debug
        // controls built alongside the advanced page can be appended to it.
        private Foldout? _debugSection;
#endif

        public PauseMenuSettingsBuilder(
            UIDocument doc,
            IClientConfigManager clientConfig,
            IAudioSystem audioSystem,
            DisplayManager displayManager,
            GraphicsSettingsController graphicsSettings,
            LightingEngine lightingEngine,
            PostProcessController postProcessController,
            INetworkService networkService,
            IConnectionService connectionService,
            ILocalPlayerState localPlayer,
            ICollection<Action> settingsRefreshers,
            Action closeMenu,
            ILocalizationService loc)
        {
            _doc = doc;
            _clientConfig = clientConfig;
            _audioSystem = audioSystem;
            _displayManager = displayManager;
            _graphicsSettings = graphicsSettings;
            _lightingEngine = lightingEngine;
            _postProcessController = postProcessController;
            _networkService = networkService;
            _connectionService = connectionService;
            _localPlayer = localPlayer;
            _refreshers = settingsRefreshers;
            _closeMenu = closeMenu;
            _loc = loc;
        }

        public VisualElement BuildAudioPage(ScrollView audioScroll)
        {
            var builder = new PauseMenuAudioTabBuilder(_clientConfig, _audioSystem, _refreshers, _loc);
            return builder.Build(audioScroll);
        }

        public VisualElement BuildDisplayPage(ScrollView displayScroll)
        {
            var builder = new PauseMenuDisplayTabBuilder(_clientConfig, _displayManager, _refreshers, _loc);
            return builder.Build(displayScroll);
        }

        public VisualElement BuildGraphicsPage(ScrollView graphicsScroll)
        {
            var builder = new PauseMenuGraphicsTabBuilder(
                _graphicsSettings,
                _lightingEngine,
                _clientConfig,
                _refreshers,
                _loc,
                RefreshAll,
                action => _updateLightingQualityButton = action,
                foldout => _customGraphicsSection = foldout);
            return builder.Build(graphicsScroll);
        }

        public VisualElement BuildEffectsPage(ScrollView effectsScroll)
        {
            var builder = new PauseMenuEffectsTabBuilder(
                _graphicsSettings,
                _postProcessController,
                _clientConfig,
                _refreshers,
                _loc);
            return builder.Build(effectsScroll);
        }

        public VisualElement BuildInterfacePage(ScrollView interfaceScroll)
        {
            var builder = new PauseMenuInterfaceTabBuilder(
                _doc,
                _clientConfig,
                _graphicsSettings,
                _refreshers,
                _loc);
            return builder.Build(interfaceScroll);
        }

        public VisualElement BuildAdvancedPage(ScrollView advancedScroll)
        {
            var builder = new PauseMenuAdvancedTabBuilder(
                _graphicsSettings,
                _lightingEngine,
                _clientConfig,
                _localPlayer,
                _refreshers,
                _loc,
                MarkGraphicsCustom
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
                , AddLightingDebugControls
#endif
            );
            return builder.Build(advancedScroll);
        }

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        /// <summary>
        /// Creates the developer tools foldout. Must be called before
        /// <see cref="BuildAdvancedPage"/>, which appends the lighting debug
        /// view and the live diagnostics readout to it.
        /// </summary>
        public Foldout BuildDebugSection()
        {
            var builder = new PauseMenuDebugSectionBuilder(_networkService, _connectionService, _localPlayer, _closeMenu, _loc);
            _debugSection = builder.Build();
            return _debugSection;
        }

        private void AddLightingDebugControls()
        {
            Foldout? debugSection = _debugSection;
            if (debugSection == null)
            {
                return;
            }

            string[] lightingDebugNames =
            [
                "settings.debug.final_lighting",
                "settings.debug.occupancy",
                "settings.debug.albedo",
                "settings.debug.emission",
                "settings.debug.transmission",
                "settings.debug.direct_radiance",
                "settings.debug.diffuse_bounce",
                "settings.debug.exposure",
            ];
            int activeDebugView = (int)_lightingEngine.ActiveDebugView;
            var lightingDebugView = new Button();
            void UpdateLightingDebugButton()
            {
                lightingDebugView.text =
                    _loc.Get("settings.debug.lighting_label") + ": " +
                    _loc.Get(lightingDebugNames[activeDebugView]);
            }

            lightingDebugView.clicked += () =>
            {
                activeDebugView = (activeDebugView + 1) % lightingDebugNames.Length;
                _lightingEngine.SetDebugView(
                    (LightingEngine.DebugView)activeDebugView);

                UpdateLightingDebugButton();
            };
            lightingDebugView.AddToClassList("pause-btn");
            UpdateLightingDebugButton();
            debugSection.Add(lightingDebugView);

            Toggle bypassPostProcessToggle = PauseMenuUIFactory.CreateBoundToggle(
                "Bypass Post-Process (Bisect)",
                () => PostProcessRenderPass.BypassPostProcessEffects,
                value =>
                {
                    PostProcessRenderPass.BypassPostProcessEffects = value;
                    Debug.Log($"[PostProcess] BypassPostProcessEffects = {value}");
                },
                _refreshers);
            debugSection.Add(bypassPostProcessToggle);

            debugSection.Add(PauseMenuUIFactory.CreateLabel(_loc.Get("settings.lighting.actual_params")));
            var lightingDiagnostics = new Label();
            lightingDiagnostics.AddToClassList("pause-slider-label");
            void UpdateLightingDiagnostics()
            {
                lightingDiagnostics.text =
                    $"Quality={_lightingEngine.ActiveGraphicsPreset}\n" +
                    $"Config={_lightingEngine.RuntimeConfigFilePath}\n" +
                    $"Debug={_lightingEngine.ActiveDebugView}\n" +
                    $"DiffuseBounce={(_lightingEngine.DiffuseBounceEnabled ? 1 : 0)} " +
                    $"strength={_lightingEngine.BounceStrength:F3}\n" +
                    $"Ambient={_lightingEngine.AmbientIntensity:F3} " +
                    $"Emission={_lightingEngine.EmissionScale:F3} " +
                    $"DynamicRate={_lightingEngine.DynamicLightUpdatesPerSecond:F1}\n" +
                    $"EmptyExtinction={_lightingEngine.EmptyExtinctionMultiplier:F3} " +
                    $"SolidExtinction={_lightingEngine.SolidExtinctionMultiplier:F3}\n" +
                    $"MinimumTransmission={_lightingEngine.MinimumTransmission:F4} " +
                    $"MaximumLight={_lightingEngine.MaximumLightMultiplier:F3}\n" +
                    $"SafeBorder={_lightingEngine.LightSafeBorder} " +
                    $"TransmissionDistance={_lightingEngine.TransmittanceDebugDistanceCells:F2}\n" +
                    $"Field={_lightingEngine.FieldWidth}x{_lightingEngine.FieldHeight} " +
                    $"AtlasEntries={_lightingEngine.AtlasEntryCount} " +
                    $"DynamicLights={_lightingEngine.DynamicLightCount} " +
                    $"Uploaded={_lightingEngine.UploadedDynamicLightCount} " +
                    $"Dropped={_lightingEngine.DroppedDynamicLightCount} " +
                    $"DroppedIds=[{string.Join(",", _lightingEngine.DroppedDynamicLightIds)}]\n" +
                    $"ComputeAmbient={_lightingEngine.ComputeAmbientColor} " +
                    $"ComputeEmptyExtinction={_lightingEngine.ComputeEmptyExtinction} " +
                    $"ComputeSolidExtinction={_lightingEngine.ComputeSolidExtinction}\n" +
                    $"RequiredPadding={_lightingEngine.RequiredTerrainPadding} " +
                    $"SolveCount={_lightingEngine.SolveCount}";
            }

            UpdateLightingDiagnostics();
            debugSection.Add(lightingDiagnostics);
            var refreshLightingDiagnostics = new Button(UpdateLightingDiagnostics)
            {
                text = _loc.Get("settings.lighting.refresh"),
            };
            refreshLightingDiagnostics.AddToClassList("pause-btn");
            debugSection.Add(refreshLightingDiagnostics);
            var resetLightingPreferences = new Button(() =>
            {
                MarkGraphicsCustom();
                _lightingEngine.ResetRuntimeLightingPreferences();
                ResolveLocalRobot()?.ResetDynamicLightPreferences();
                RefreshAll();
                UpdateLightingDiagnostics();
            })
            {
                text = _loc.Get("settings.lighting.reset"),
            };
            resetLightingPreferences.AddToClassList("pause-btn");
            debugSection.Add(resetLightingPreferences);
        }
#endif

        private Robot? ResolveLocalRobot()
        {
            return _localPlayer.Current?.GetComponent<Robot>();
        }

        private void MarkGraphicsCustom()
        {
            _graphicsSettings.MarkCustom();
            _updateLightingQualityButton?.Invoke();
        }

        private void RefreshAll()
        {
            // Copied first: a refresher may add another control on a page that
            // has not been built yet, and mutating the shared list mid-iteration
            // would throw.
            var snapshot = new List<Action>(_refreshers);
            foreach (Action refresh in snapshot)
            {
                refresh();
            }
        }
    }
}
