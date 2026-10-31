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

internal static class GamePacketProcessorRegistration
{
    internal static void Register(IContainerBuilder builder)
    {
            builder.Register<ClanProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<InventoryProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<StatusProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<MapRegionProcessor>(Lifetime.Singleton);
            builder.Register<AudioPacketProcessor>(Lifetime.Singleton);
            builder.Register<VisualPacketProcessor>(Lifetime.Singleton);
            builder.RegisterEntryPoint<ProgrammatorProcessor>().AsSelf();
            builder.Register<PlayerInfoProcessor>(Lifetime.Singleton);
            builder.Register<ChatProcessor>(Lifetime.Singleton);
            builder.Register<MissionProcessor>(Lifetime.Singleton);
            builder.Register<BuildingProcessor>(Lifetime.Singleton);
            builder.Register<ConnectionProcessor>(Lifetime.Singleton);
            builder.Register<MissionArrowProcessor>(Lifetime.Singleton);
            builder.Register<WindowPacketProcessor>(Lifetime.Singleton);
            builder.Register<ClientConfigProcessor>(Lifetime.Singleton);
    }
}
