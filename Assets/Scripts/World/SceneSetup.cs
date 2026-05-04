using UnityEngine;

namespace Fodinae.Assets.Scripts.World
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

        void Awake()
        {
            // Ensure this is a singleton
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Set up the world background renderer
            SetupWorldBackground();
        }

        private void SetupWorldBackground()
        {
            // Find or create the background setup component
            _backgroundSetup = FindObjectOfType<WorldBackgroundSetup>();
            
            if (_backgroundSetup == null)
            {
                // Create a new GameObject for background setup
                var setupGO = new GameObject("WorldBackgroundSetup");
                _backgroundSetup = setupGO.AddComponent<WorldBackgroundSetup>();
                DontDestroyOnLoad(setupGO);
                
                Debug.Log("WorldBackgroundSetup automatically created");
            }
            else
            {
                Debug.Log("WorldBackgroundSetup already exists in scene");
            }
        }

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
    }
}