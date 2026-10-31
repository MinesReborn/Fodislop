#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Core.Localization;
using Kern.AssetPipeline;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Rendering;
using Kern.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;
using Kern.AssetPipeline.Cache;
using Kern.AssetPipeline.Loading;
using Kern.Bootstrap.Diagnostics;
using Kern.Core.Configuration;
using Kern.Core.Diagnostics;
using Kern.Core.Rendering;
using Kern.Diagnostics.Scenes;
using Kern.Game.Inventory;
using Kern.Game.Player.Stats;
using Kern.Infrastructure.Audio;
using Kern.Networking.Authentication;
using Kern.Networking.Gateways;
using Kern.Networking.Offline;
using Kern.Networking.Processing;
using Kern.Networking.WorldEntry;
using Kern.Rendering.Display;
using Kern.UI.Menu;
using Kern.Core;

namespace Kern.Bootstrap.Scenes;

internal static class BootstrapServiceRegistration
{
    internal static void Register(IContainerBuilder builder)
    {
            builder.Register<AudioBankManifestState>(Lifetime.Singleton)
                .As<IAudioBankManifestState>()
                .AsSelf();
            builder.Register<IMainMenuNavigation>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.Register<ISceneNavigator>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.RegisterInstance(GraphicsQualityProfile.CreateDefault());
            builder.Register<ShaderWarmupService>(Lifetime.Singleton).As<IShaderWarmupService>();
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();
            builder.Register<PersistentAssetCache>(Lifetime.Singleton)
                .As<IPersistentAssetCache>();
            // Window images served over http(s): the same AssetCache
            // machinery as the server asset loader, RAM-only, sourced from
            // the web client instead of the game connection.
            builder.Register<WebAssetLoader>(Lifetime.Singleton).As<IWebAssetLoader>();
            builder.Register<RuntimeAssetPaths>(
                _ => new RuntimeAssetPaths(),
                Lifetime.Singleton).As<IRuntimeAssetPaths>();

            // DummyConnection emulates the game server in offline mode. External
            // identity providers do not route authentication through it.
            builder.Register<RealtimeDummyClock>(Lifetime.Singleton).As<IDummyClock>();
            builder.Register<DummyConnection>(Lifetime.Singleton).AsSelf().AsImplementedInterfaces();
            builder.Register<ConnectionTransportFactory>(Lifetime.Singleton);
            builder.Register<DummyWorldMapSource>(Lifetime.Singleton);
            builder.RegisterEntryPoint<Kern.Networking.WorldEntry.WorldEntryPreparation>()
                .As<IWorldEntryPreparation>();
            builder.Register<GameTokenStore>(Lifetime.Singleton).As<IGameTokenStore>();
            builder.Register<RuntimeDebugSettings>(Lifetime.Singleton).As<IRuntimeDebugSettings>();
            builder.Register<OfflineScenarioSettings>(Lifetime.Singleton).As<IOfflineScenarioSettings>();
            builder.Register<VKIdentityProvider>(Lifetime.Singleton);
            builder.Register<AuthenticationService>(Lifetime.Singleton).As<IAuthenticationService>();

            // Application-tier session state: NetworkService (Bootstrap) and
            // Game-tier processors both resolve the same local-player state.
            builder.Register<LocalPlayerState>(Lifetime.Singleton).As<ILocalPlayerState>();

            // Ник и уровень игрока для бейджа меню и досье. Сервер присылает
            // PlayerInfoPacket/LevelPacket в момент авторизации сокета (Auth:
            // «без этого gate висит»), когда клиент ещё в гейтвее — меню-скип
            // опоздал бы, поэтому идентичность живёт здесь, на Application-tier,
            // и подписывается с самого старта приложения. Игровая область
            // регистрирует свой PlayerStatsModel для HUD, как и раньше.
            builder.Register<Kern.Game.Player.Stats.PlayerStatsModel>(Lifetime.Singleton)
                .AsImplementedInterfaces()
                .AsSelf();
            builder.RegisterEntryPoint<Kern.Networking.Processing.PlayerIdentityProcessor>().AsSelf();

            // World-load phases and server-window visibility are published by
            // the Game scope but consumed by the MainMenu sibling scope, so
            // both relays live at the application tier.
            builder.Register<WorldLoadProgress>(Lifetime.Singleton).As<IWorldLoadProgress>();
            builder.Register<WindowCommandStream>(Lifetime.Singleton);
            builder.Register<ItemRegistry>(Lifetime.Singleton).As<IItemCatalog>();
            builder.RegisterInstance(BlockRegistry.Default).As<IBlockRegistry>();

            // The persistent application camera as a typed DI dependency.
            // RegisterComponent(applicationCamera) below exposes the Camera
            // itself; IGameplayCamera gives a named contract so DI components
            // do not reach into the static GameplayCamera holder.
            builder.Register<GameplayCameraService>(Lifetime.Singleton).As<IGameplayCamera>();

    }
}
