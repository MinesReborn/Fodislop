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

internal static class GameUIServiceRegistration
{
    internal static void Register(IContainerBuilder builder, Volume postProcessVolume)
    {
            builder.Register<ServerConfig>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<UIInputManager>(Lifetime.Singleton);
            builder.Register<TouchCommands>(Lifetime.Singleton);
            builder.RegisterEntryPoint<Kern.Game.Player.Input.TouchGestureTracker>().AsSelf();

            if (postProcessVolume == null)
            {
                throw new SceneContractException(
                    "MainGame scene scope is missing serialized postProcessVolume reference.");
            }

            builder.RegisterComponent(postProcessVolume);
            MapServiceRegistration.Register(builder);
            builder.RegisterEntryPoint<DisplayManager>().AsSelf();

            // Единственный фоновый отчёт о провисе кадра: живёт со сценой игры,
            // а не внутри компонента, который случайно тикает каждый кадр.
            builder.RegisterEntryPoint<Kern.Rendering.Diagnostics.Profiling.FrameStallMonitor>();
    }
}
