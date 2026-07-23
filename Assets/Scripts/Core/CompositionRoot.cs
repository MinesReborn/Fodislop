using Fodinae.Scripts;
using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    /// <summary>
    /// Composition root that registers all services in ServiceLocator at startup.
    /// Runs on scene load via DefaultExecutionOrder to ensure services are available
    /// before any consumer accesses them through ServiceLocator.
    ///
    /// This does not replace SingletonMonoBehaviour.Instance — it augments it.
    /// Existing Instance accessors still work. ServiceLocator provides the abstraction
    /// layer for testability.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class CompositionRoot : MonoBehaviour
    {
        private void Awake()
        {
            if (ClientAssetLoader.Instance != null)
            {
                ServiceLocator.Register<IAssetLoader>(ClientAssetLoader.Instance);
            }

            if (MapManager.Instance != null)
            {
                ServiceLocator.Register<IMapDataProvider>(MapManager.Instance);
            }

            if (MapStorage.Instance != null)
            {
                ServiceLocator.Register<IWorldDataStorage>(MapStorage.Instance);
            }

            if (AudioSystem.Instance != null)
            {
                ServiceLocator.Register<IAudioSystem>(AudioSystem.Instance);
            }

            var stats = FindAnyObjectByType<PlayerStatsModel>();
            if (stats != null)
            {
                ServiceLocator.Register<IPlayerStats>(stats);
            }

            if (InventoryModel.Instance != null)
            {
                ServiceLocator.Register<IInventoryModel>(InventoryModel.Instance);
            }

            DontDestroyOnLoad(gameObject);
        }
    }
}
