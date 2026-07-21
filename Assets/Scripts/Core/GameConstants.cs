namespace Fodinae.Scripts
{
    public static class GameConstants
    {
        public static class World
        {
            public const int DEFAULTCHUNKSIZE = 32;
            public const float CELLSIZE = 1.0f;

            /// <summary>
            /// Global world darkness factor (0 = normal, 1 = pitch black).
            /// Hardcoded for all players - not configurable.
            /// </summary>
            public const float WorldDarknessFactor = 0.8f;
        }

        public static class UI
        {
            public const float MINIMAPUPDATEDELAY = 0.033f; // 30 FPS
            public const int MINIMAPTHRESHOLD = 8;
            public const int MINIMAPWIDTH = 128;
            public const int MINIMAPHEIGHT = 128;
        }

        public static class Debug
        {
            public const int COLLISIONDEBUGRANGE = 10;
        }

        public static class Movement
        {
            public const float DEFAULTMOVESPEED = 15f;
            public const float REFERENCEMOVESPEED = 25f;
        }
    }
}
