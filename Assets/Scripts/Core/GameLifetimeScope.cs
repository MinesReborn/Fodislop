using Fodinae.Scripts;
using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Networking.Connection.Client;
using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using Fodinae.Scripts.UI.HUD.Player.Model;
using Fodinae.Scripts.World;
using VContainer;
using VContainer.Unity;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private GameObject _playerPrefab;
        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[GameLifetimeScope] Configure START");
            Debug.Log($"[GameLifetimeScope] MapStorage.Instance={MapStorage.Instance != null}, InventoryModel.Instance={InventoryModel.Instance != null}");

            builder.RegisterInstance(MapStorage.Instance ?? new MapStorage()).As<IWorldDataStorage>();
            builder.RegisterInstance(InventoryModel.Instance ?? new InventoryModel()).As<IInventoryModel>();

            RegisterManager<MapManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<SingleMeshTerrainRenderer>(builder);
            RegisterManager<ClientAssetLoader>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<AudioSystem>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<WorldTextureManager>(builder);
            RegisterManager<ServerAudioEventManager>(builder);
            RegisterManager<ConnectionManager>(builder);
            RegisterManager<NetworkService>(builder);
            RegisterManager<GameManager>(builder);
            RegisterManager<VFXPool>(builder);
            RegisterManager<PackManager>(builder);
            RegisterManager<RobotManager>(builder);
            RegisterManager<ServerConfig>(builder);
            RegisterManager<TextureStorageManager>(builder);
            RegisterManager<PlayerStatsModel>(builder).AsImplementedInterfaces().AsSelf();

            builder.RegisterBuildCallback(resolver =>
            {
                Debug.Log("[GameLifetimeScope] BuildCallback START");
                ServiceLocator.Initialize(resolver);

                resolver.Resolve<ConnectionManager>();
                Debug.Log("[GameLifetimeScope] Resolved ConnectionManager");
                resolver.Resolve<NetworkService>();
                Debug.Log("[GameLifetimeScope] Resolved NetworkService");
                resolver.Resolve<IAssetLoader>();
                Debug.Log("[GameLifetimeScope] Resolved IAssetLoader");
                resolver.Resolve<IAudioSystem>();
                Debug.Log("[GameLifetimeScope] Resolved IAudioSystem");
                resolver.Resolve<GameManager>();
                Debug.Log("[GameLifetimeScope] Resolved GameManager");
                resolver.Resolve<ServerConfig>();
                Debug.Log("[GameLifetimeScope] Resolved ServerConfig");
                resolver.Resolve<TextureStorageManager>();
                Debug.Log("[GameLifetimeScope] Resolved TextureStorageManager");
                resolver.Resolve<WorldTextureManager>();
                Debug.Log("[GameLifetimeScope] Resolved WorldTextureManager");
                resolver.Resolve<ServerAudioEventManager>();
                Debug.Log("[GameLifetimeScope] Resolved ServerAudioEventManager");
                resolver.Resolve<VFXPool>();
                Debug.Log("[GameLifetimeScope] Resolved VFXPool");
                resolver.Resolve<PackManager>();
                Debug.Log("[GameLifetimeScope] Resolved PackManager");
                resolver.Resolve<RobotManager>();
                Debug.Log("[GameLifetimeScope] Resolved RobotManager");
                resolver.Resolve<IPlayerStats>();
                Debug.Log("[GameLifetimeScope] Resolved IPlayerStats");

                foreach (var terrain in FindObjectsByType<SingleMeshTerrainRenderer>())
                {
                    resolver.Inject(terrain);
                }

                Debug.Log("[GameLifetimeScope] BuildCallback END");
            });
            Debug.Log("[GameLifetimeScope] Configure END");
        }

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder)
            where T : MonoBehaviour
        {
            var existing = FindAnyObjectByType<T>();
            if (existing != null)
            {
                return builder.RegisterInstance(existing);
            }

            return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton);
        }

        private void Start()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                Container.Inject(player);
            }
            else if (_playerPrefab != null)
            {
                Container.Instantiate(_playerPrefab);
            }
        }
    }
}
