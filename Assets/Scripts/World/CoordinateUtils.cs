using Fodinae.Scripts.Game.Managers;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    /// <summary>
    /// Centralized utility class for coordinate conversions between Server (0 at top, Y+)
    /// and Unity (0 at bottom, Y+) systems using worldHeight.
    /// Handles wrapping and snapping to grid consistently across the project.
    /// </summary>
    public static class CoordinateUtils
    {
        private static int ResolveHeight(int worldHeight)
        {
            if (worldHeight > 0)
            {
                return worldHeight;
            }

            if (MapManager.Instance != null && MapManager.Instance.WorldHeight > 0)
            {
                return MapManager.Instance.WorldHeight;
            }

            return 128;
        }

        /// <summary>
        /// Converts Server Y to Unity World Y (Centered on cell).
        /// </summary>
        public static float ServerToUnityY(int serverY, int worldHeight = 0)
        {
            int h = ResolveHeight(worldHeight);
            return (h - 1 - serverY) + 0.5f;
        }

        /// <summary>
        /// Converts Unity World Y to Server Y with modulo wrapping.
        /// </summary>
        public static int UnityToServerY(float unityY, int worldHeight = 0)
        {
            int h = ResolveHeight(worldHeight);
            int y = Mathf.FloorToInt(unityY);
            int serverY = (h - 1 - y) % h;
            if (serverY < 0)
            {
                serverY += h;
            }

            return serverY;
        }

        /// <summary>
        /// Converts Server position to Unity World position (Center of cell).
        /// </summary>
        public static Vector3 ServerToUnityPos(int x, int y, int worldHeight = 0, float z = 0f)
        {
            return new Vector3(x + 0.5f, ServerToUnityY(y, worldHeight), z);
        }

        /// <summary>
        /// Converts Unity World position to Server Grid position.
        /// </summary>
        public static Vector2Int UnityToServerPos(Vector3 unityPos, int worldHeight = 0)
        {
            return new Vector2Int(Mathf.FloorToInt(unityPos.x), UnityToServerY(unityPos.y, worldHeight));
        }
    }
}
