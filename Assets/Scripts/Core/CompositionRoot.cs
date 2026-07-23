using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using Fodinae.Scripts.UI.HUD.Player.Model;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    /// <summary>
    /// Native Unity composition root that automatically registers all core services in ServiceLocator
    /// before any scene is loaded or any Awake() method is executed.
    /// </summary>
    public static class CompositionRoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            // Pure C# Singletons — register immediately
            ServiceLocator.Register<IWorldDataStorage>(MapStorage.Instance);
            ServiceLocator.Register<IInventoryModel>(InventoryModel.Instance);

            // MonoBehaviour Singletons — register if present or lazily when Awake runs
            if (ClientAssetLoader.Instance != null)
            {
                ServiceLocator.Register<IAssetLoader>(ClientAssetLoader.Instance);
            }

            if (MapManager.Instance != null)
            {
                ServiceLocator.Register<IMapDataProvider>(MapManager.Instance);
            }

            if (AudioSystem.Instance != null)
            {
                ServiceLocator.Register<IAudioSystem>(AudioSystem.Instance);
            }

            if (PlayerStatsModel.Instance != null)
            {
                ServiceLocator.Register<IPlayerStats>(PlayerStatsModel.Instance);
            }
        }
    }
}
