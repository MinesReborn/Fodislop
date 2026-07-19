namespace Fodinae.Scripts
{
    public static class GameConstants
    {
        public static class World
        {
            public const int DEFAULT_CHUNK_SIZE = 32;
            public const float CELL_SIZE = 1.0f;
            
            /// <summary>
            /// Global world darkness factor (0 = normal, 1 = pitch black).
            /// Hardcoded for all players - not configurable.
            /// </summary>
            public const float WorldDarknessFactor = 0.8f;
        }

        public static class UI
        {
            public const float MINIMAP_UPDATE_DELAY = 0.033f; // 30 FPS
            public const int MINIMAP_THRESHOLD = 8;
            public const int MINIMAP_WIDTH = 128;
            public const int MINIMAP_HEIGHT = 128;
        }

        public static class Debug
        {
            public const int COLLISION_DEBUG_RANGE = 10;
        }

        public static class Movement
        {
            public const float DEFAULT_MOVE_SPEED = 15f;
            public const float REFERENCE_MOVE_SPEED = 25f;
        }
    }
}
