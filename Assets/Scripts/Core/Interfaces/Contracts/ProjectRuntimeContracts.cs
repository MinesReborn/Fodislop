#nullable enable

namespace Kern.Core;

public static class ProjectRuntimeContracts
{
    public static class World
    {
        public const float CellSize = 1f;
        public const int ChunkSize = 32;
        public const int ResidentChunkCacheCapacity = 2000;
    }

    public static class Camera
    {
        // Половина видимой высоты в клетках. Максимум задаёт и размер области
        // освещения: свет считается на кадр максимального отдаления, чтобы зум
        // не менял сетку каскадов и не перекрашивал сцену.
        public const float MinimumOrthographicSize = 5f;
        public const float MaximumOrthographicSize = 30f;
    }

    public static class Gameplay
    {
        public const float DefaultDigCooldown = 0.3f;
    }

    public static class ClientConfiguration
    {
        // По умолчанию — реальный сервер 127.0.0.1:8090 (порт сервера из
        // appsettings.json, ключ Mines3:Port; прежний дефолт 7777 с портом
        // сервера не совпадал).
        public const bool DefaultUseDummyConnection = false;
        public const string DefaultServerHost = "127.0.0.1";
        public const int DefaultServerPort = 8090;
        public const bool DefaultHDREnabled = true;
    }

    public static class Authentication
    {
        public const string VKClientID = "";
        public const string VKBackendURL = "";
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
        public const string ScopesCompute = "Shaders/PostProcessing/Scopes";
        public const string GatewayUxml = "UI/Gateway";
        public const string MainMenuUxml = "UI/MainMenu";
        public const string AssetLoadingIndicatorUxml = "UI/AssetLoadingIndicator";
        public const string GlobalChatUxml = "UI/GlobalChat";
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

    public static class EditorSession
    {
        // Сцена, из которой нажали Play: редактор кладёт её сюда, Bootstrap забирает.
        public const string PlayModeTargetScene = "Kern.PlayModeTargetScene";
    }

    public static class PreviewVisuals
    {
        public const float RobotPixelsPerUnit = 16f;
    }

    public static class ShaderNames
    {
        public const string Terrain = "Universal Render Pipeline/Custom/Terrain";
        public const string WorldSurface = "Kern/World Surface";
        public const string WorldEntity = "Kern/World Entity";
        public const string PlanetSurface = "Kern/UI/PlanetSurface";
        public const string PlanetAtmosphere = "Kern/UI/PlanetAtmosphere";
        public const string Starfield = "Kern/UI/Starfield";
        public const string MenuLineUnlit = "Kern/UI/MenuLineUnlit";
        public const string UnpremultiplyAlpha = "Kern/UI/UnpremultiplyAlpha";
    }

    public static class ShaderPassNames
    {
        public const string LightingMaterialField = "LightingMaterialField";
    }

    public static class ComputeKernelNames
    {
        public const string SolveCascade = "SolveCascade";
        public const string ScrollRadianceAtlas = "ScrollRadianceAtlas";
        public const string SolveDynamicLighting = "SolveDynamicLighting";
        public const string ComposeDynamicLighting = "ComposeDynamicLighting";
        public const string TraceDynamicPolar = "TraceDynamicPolar";
        public const string ClearDynamicDirect = "ClearDynamicDirect";
        public const string ResolveDirect = "ResolveDirect";
        public const string ResolveTransmissionDebug = "ResolveTransmissionDebug";
        public const string SolveDiffuseBounce = "SolveDiffuseBounce";
        public const string CompositeLighting = "CompositeLighting";
        public const string BuildCellSolidMask = "BuildCellSolidMask";
        public const string BuildBounceTaps = "BuildBounceTaps";
        public const string BuildBounceFilter = "BuildBounceFilter";
    }

    public static class RequiredLayers
    {
        public const string WorldUI = "UI";
        public const string WorldUISortingLayer = "World UI";
        public const int TerrainSortingOrder = -1000;
    }

    public static class RuntimeLimits
    {
        public const int MaximumPacketBatchPerFrame = 250;    }
}
