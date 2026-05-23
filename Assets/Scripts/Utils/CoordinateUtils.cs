using UnityEngine;

namespace Fodinae.Scripts.Utils
{
    /// <summary>
    /// Utility class to handle coordinate conversions between Server (0 at top) 
    /// and Unity (0 at bottom) coordinate systems.
    /// </summary>
    public static class CoordinateUtils
    {
        /// <summary>
        /// Converts a Server Y coordinate to a Unity World Y coordinate.
        /// </summary>
        public static float ServerToUnityY(ushort serverY, ushort worldHeight)
        {
            return (worldHeight - 1 - serverY) + 0.5f;
        }

        /// <summary>
        /// Converts a Unity World Y coordinate to a Server Y coordinate.
        /// </summary>
        public static ushort UnityToServerY(float unityY, ushort worldHeight)
        {
            return (ushort)Mathf.Clamp(worldHeight - 1 - Mathf.FloorToInt(unityY), 0, worldHeight - 1);
        }

        /// <summary>
        /// Converts a Server position to a Unity World position.
        /// </summary>
        public static Vector3 ServerToUnityPos(ushort x, ushort y, ushort worldHeight, float z = 0f)
        {
            return new Vector3(x + 0.5f, ServerToUnityY(y, worldHeight), z);
        }

        /// <summary>
        /// Converts a Unity World position to a Server grid position.
        /// </summary>
        public static Vector2Int UnityToServerPos(Vector3 unityPos, ushort worldHeight)
        {
            return new Vector2Int(Mathf.FloorToInt(unityPos.x), UnityToServerY(unityPos.y, worldHeight));
        }
    }
}
