using Fodinae.Scripts.UI;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    /// <summary>
    /// Scene setup manager that ensures the world background renderer is properly configured.
    /// This script should be added to a persistent GameObject in the scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // Run before other scripts
    public class SceneSetup : MonoBehaviour
    {
        private static SceneSetup _instance;
        private WorldBackgroundSetup _backgroundSetup;

        /// <summary>
        /// Get the background setup instance
        /// </summary>
        public static WorldBackgroundSetup GetBackgroundSetup()
        {
            return _instance?._backgroundSetup;
        }

        /// <summary>
        /// Get the background renderer instance
        /// </summary>
        public static SingleMeshTerrainRenderer GetBackgroundRenderer()
        {
            return _instance?._backgroundSetup?.GetBackgroundRenderer();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            SetupWorldBackground();
            SetupSurfaceRenderer();
            SetupWorldMapController();
        }

        private void SetupSurfaceRenderer()
        {
            var existing = FindObjectOfType<SurfaceRenderer>();
            if (existing != null) return;

            var surfaceGO = new GameObject("SurfaceRenderer");
            surfaceGO.transform.SetParent(transform);
            surfaceGO.AddComponent<SurfaceRenderer>();
        }
        private void SetupWorldMapController()
        {
            var existing = FindObjectOfType<WorldMapController>();
            if (existing != null) return;

            var controllerGO = new GameObject("WorldMapController");
            controllerGO.transform.SetParent(transform);
            controllerGO.AddComponent<WorldMapController>();
        }

        private void SetupWorldBackground()
        {
            // Find or create the background setup component
            _backgroundSetup = FindFirstObjectByType<WorldBackgroundSetup>();

            if (_backgroundSetup == null)
            {
                // Create a new GameObject for background setup
                var setupGO = new GameObject("WorldBackgroundSetup");
                _backgroundSetup = setupGO.AddComponent<WorldBackgroundSetup>();

                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(setupGO);
                }

                Debug.Log("WorldBackgroundSetup automatically created");
            }
            else
            {
                Debug.Log("WorldBackgroundSetup already exists in scene");
            }
        }
    }
}
