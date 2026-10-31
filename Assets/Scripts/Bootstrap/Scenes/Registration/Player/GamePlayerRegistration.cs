#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.WorldLighting;
using Kern.Core.Lifecycle;
using Kern.Game;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.UI;
using Kern.Game.Inventory;
using Kern.UI.Inventory;
using Kern.Game.Player.Stats;
using Kern.UI.Programmator;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using global::Kern.Core.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;
using Kern.Bootstrap.WorldLighting;
using Kern.Core.Rendering;
using Kern.Diagnostics.Scenes;
using Kern.Game.Configuration;
using Kern.Game.Entities.Buildings;
using Kern.Game.Entities.Robots;
using Kern.Game.Events.Playback;
using Kern.Game.Player.Actions;
using Kern.Game.Player.Cameras;
using Kern.Game.Player.Input;
using Kern.Game.Player.Movement;
using Kern.Game.Rendering;
using Kern.Game.Session;
using Kern.Game.Visual.Effects.Cells;
using Kern.Game.Visual.Effects.Pooling;
using Kern.Game.Visual.Effects.Sprites;
using Kern.Networking.Gateways;
using Kern.Networking.Processing;
using Kern.Networking.Status;
using Kern.Rendering.Diagnostics.Profiling;
using Kern.Rendering.Display;
using Kern.UI.Chat;
using Kern.UI.Connection;
using Kern.UI.Diagnostics;
using Kern.UI.HUD;
using Kern.UI.Input;
using Kern.UI.Loading;
using Kern.UI.Map;
using Kern.UI.Missions;
using Kern.UI.Screens;
using Kern.UI.ServerWindows;
using Kern.UI.Settings;
using Kern.UI.WorldLabels;
using Kern.Core;
using Kern.Diagnostics.World;

namespace Kern.Bootstrap.Scenes;

internal static class GamePlayerRegistration
{
    internal static void Register(IContainerBuilder builder, GameLifetimeScope scope, PlayerMovementController playerMovement)
    {
            if (playerMovement == null)
            {
                throw new SceneContractException(
                    "MainGame scene must contain an authored PlayerMovementController reference.");
            }

            builder.RegisterComponent(playerMovement);
            if (playerMovement.TryGetComponent<Robot>(out Robot? playerRobot))
            {
                // The local player's Robot carries [Inject] dependencies
                // (IAssetLoader, LightingEngine, MapManager, RobotManager) that
                // VContainer applies only to components it registers. Without
                // this, the authored fallback skin path triggers Start-time
                // loading against a null loader.
                builder.RegisterComponent(playerRobot);
            }

            if (playerMovement.TryGetComponent<PlayerInteractionController>(out PlayerInteractionController? playerInteraction))
            {
                // The PlayerInteractionController on the authored Player prefab
                // must be registered exactly like the movement controller:
                // without a registration VContainer never injects its [Inject]
                // fields, so the component silently dropped every ClickCellPacket
                // and mouse clicks on world cells never reached the server.
                builder.RegisterComponent(playerInteraction);
            }

            SceneSetup? sceneSetup = scope.GetComponentInChildren<SceneSetup>(includeInactive: true);
            if (sceneSetup == null)
            {
                throw new SceneContractException(
                    "MainGame scene must contain an authored SceneSetup component.");
            }

            builder.RegisterComponent(sceneSetup);

            if (playerMovement.TryGetComponent<PlayerInputHandler>(out PlayerInputHandler? playerInput))
            {
                // Same rule as the interaction controller: the authored
                // Player's PlayerInputHandler carries [Inject]
                // IClientConfigManager, and without a registration VContainer
                // never injects it. The handler then silently falls back to
                // hardcoded default keys, so InterfaceSettings.Key* rebinds
                // from the controls tab change only the displayed key.
                builder.RegisterComponent(playerInput);
            }

    }
}
