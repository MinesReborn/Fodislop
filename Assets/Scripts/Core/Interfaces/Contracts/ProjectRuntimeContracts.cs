#nullable enable

namespace Fodinae.Core;

public static class ProjectRuntimeContracts
{
    public static class World
    {
        public const float CellSize = 1f;
        public const int ChunkSize = 32;
        public const int MaximumWidth = ushort.MaxValue;
        public const int MaximumHeight = ushort.MaxValue;
    }

    public static class Gameplay
    {
        public const float DefaultDigCooldown = 0.3f;
    }

    public static class ClientConfiguration
    {
        public const bool DefaultUseDummyConnection = true;
        public const string DefaultServerHost = "127.0.0.1";
        public const int DefaultServerPort = 7777;
        public const bool DefaultHDREnabled = true;
    }

    /// <summary>
    /// Deployment-owned authentication settings. These are application
    /// metadata, not mutable player preferences.
    /// </summary>
    public static class Authentication
    {
        public const string VkClientId = "";
        public const string VkBackendUrl = "";
    }

    public static class Chat
    {
        public const int MaximumGlobalChatLength = 256;
        public const int MaximumLocalChatLength = 256;
    }

    public static class Movement
    {
        public const float RobotMoveSpeed = 15f;
        public const float RobotRotationSpeed = 1080f;
        public const float ReferenceMoveSpeed = 25f;
    }

    public static class Debug
    {
        public const int CollisionDebugRange = 10;
    }

    public static class AssetStreaming
    {
        public const int RequestBatchIntervalMilliseconds = 50;
        public const int AssetRequestTimeoutSeconds = 5;
        public const int LargeAssetRequestTimeoutSeconds = 10;
        public const long AssetCacheCapacityBytes = 256L * 1024 * 1024;
        public const long DecodedAssetCacheCapacityBytes = 256L * 1024 * 1024;
    }

    public static class ResourcePaths
    {
        public const string GraphicsQualityProfile = "GraphicsQualityProfile";
        public const string WorldLightingCompute = "Shaders/Lighting/WorldLighting";
        public const string PostProcessCompute = "Shaders/PostProcessing/PostProcess";
        public const string GatewayUxml = "UI/Gateway";
        public const string MainMenuUxml = "UI/MainMenu";
        public const string AssetLoadingIndicatorUxml = "UI/AssetLoadingIndicator";
        public const string GlobalChatUxml = "UI/GlobalChat";
        public const string LocalChatUxml = "UI/LocalChat";
        public const string PlayerHudUxml = "UI/PlayerHUD";
        public const string ReconnectUxml = "UI/Reconnect";
        public const string InventoryUxml = "UI/Inventory";
        public const string BootstrapLoadingScreenUxml = "UI/BootstrapLoadingScreen";
        public const string ProgrammatorUxml = "UI/Programmator";
        public const string TooltipUxml = "UI/Tooltip";
        public const string ModalWindowUxml = "UI/ModalWindow";
        public const string ObserverJoystickUxml = "UI/ObserverJoystick";
        public const string RadialMenuUxml = "UI/RadialMenu";
        public const string PauseMenuUxml = "UI/PauseMenu";
        public const string MinimapUxml = "UI/Minimap";
    }

    public static class SceneNames
    {
        public const string Bootstrap = "Bootstrap";
        public const string Gateway = "Gateway";
        public const string MainMenu = "MainMenu";
        public const string MainGame = "MainGame";
    }

    public static class PreviewVisuals
    {
        public const float RobotPixelsPerUnit = 16f;
    }

    public static class ShaderNames
    {
        public const string Terrain = "Universal Render Pipeline/Custom/Terrain";
        public const string DynamicEmission = "Hidden/Fodinae/DynamicEmission";
        public const string WorldSurface = "Fodinae/World Surface";
        public const string WorldEntity = "Fodinae/World Entity";
        public const string PlanetSurface = "Fodinae/UI/PlanetSurface";
        public const string PlanetAtmosphere = "Fodinae/UI/PlanetAtmosphere";
        public const string Starfield = "Fodinae/UI/Starfield";
        public const string MenuLineUnlit = "Fodinae/UI/MenuLineUnlit";
        public const string UnpremultiplyAlpha = "Fodinae/UI/UnpremultiplyAlpha";
    }

    public static class ShaderPassNames
    {
        public const string LightingMaterialField = "LightingMaterialField";
    }

    public static class ComputeKernelNames
    {
        public const string SolveCascade = "SolveCascade";
        public const string SolveAutomaticNormals = "SolveAutomaticNormals";
        public const string ResolveDirect = "ResolveDirect";
        public const string SolveDiffuseBounce = "SolveDiffuseBounce";
        public const string CompositeLighting = "CompositeLighting";
    }

    public static class RequiredLayers
    {
        public const string WorldUI = "UI";
        public const string WorldUISortingLayer = "World UI";
        public const int TerrainSortingOrder = -1000;
    }

    public static class RuntimeLimits
    {
        public const int MaximumPacketBatchPerFrame = 250;
        public const int MaximumWorldWidth = World.MaximumWidth;
        public const int MaximumWorldHeight = World.MaximumHeight;
        public const int WorldChunkSize = World.ChunkSize;
        public const int MaximumLightingUpdatesPerSecond = 60;
    }
}
