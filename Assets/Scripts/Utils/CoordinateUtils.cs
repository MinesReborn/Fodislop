using UnityEngine;

namespace Fodinae.Scripts.Utils
{
    /// <summary>
    /// Utility class to handle coordinate conversions between Server (0 at top) 
    /// and Unity (0 at bottom) coordinate systems with support for world wrapping.
    /// </summary>
    public static class CoordinateUtils
    {
        /// <summary>
        /// Converts a Server Y coordinate to a Unity World Y coordinate.
        /// Does NOT wrap by default as Unity world is usually not wrapped in Y.
        /// </summary>
        public static float ServerToUnityY(int serverY, int worldHeight)
        {
            return (worldHeight - 1 - serverY) + 0.5f;
        }

        /// <summary>
        /// Converts a Unity World Y coordinate to a Server Y coordinate with wrap-around support.
        /// </summary>
        public static int UnityToServerY(float unityY, int worldHeight)
        {
            if (worldHeight <= 0) return 0;
            int y = Mathf.FloorToInt(unityY);
            int serverY = (worldHeight - 1 - y) % worldHeight;
            if (serverY < 0) serverY += worldHeight;
            return serverY;
        }

        /// <summary>
        /// Wraps a Unity X coordinate to the world width.
        /// </summary>
        public static int WrapWorldX(float unityX, int worldWidth)
        {
            if (worldWidth <= 0) return 0;
            int x = Mathf.FloorToInt(unityX);
            int worldX = x % worldWidth;
            if (worldX < 0) worldX += worldWidth;
            return worldX;
        }

        /// <summary>
        /// Converts a Server position to a Unity World position.
        /// </summary>
        public static Vector3 ServerToUnityPos(int x, int y, int worldHeight, float z = 0f)
        {
            return new Vector3(x + 0.5f, ServerToUnityY(y, worldHeight), z);
        }

        /// <summary>
        /// Converts a Unity World position to a Server grid position with wrap-around.
        /// </summary>
        public static Vector2Int UnityToServerPos(Vector3 unityPos, int worldHeight, int worldWidth)
        {
            return new Vector2Int(WrapWorldX(unityPos.x, worldWidth), UnityToServerY(unityPos.y, worldHeight));
        }
    }
}
