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
        public const float MinimumOrthographicSize = 10f;
        public const float MaximumOrthographicSize = 20f;
    }

    public static class Gameplay
    {
        public const float DefaultDigCooldown = 0.3f;
    }

    public static class ClientConfiguration
    {
        // По умолчанию — штатная заглушка транспорта. Дефолт обязан быть тем
        // состоянием, в котором клиент запускается и работает: сервер поднят
        // не всегда, и при выключенной заглушке чистый конфиг встречает
        // человека отказом соединения в первую же секунду.
        //
        // Адрес и порт остаются настоящими — 127.0.0.1:8090 из appsettings.json
        // сервера, ключ Mines3:Port: заглушку выключают одним тумблером, и
        // тогда клиент идёт туда, куда надо, без правки адреса.
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

    public static class Networking
    {
        // Must stay aligned with the server's accepted ClientHello protocol
        // version. A development-only old-client path may still send 0.
        public const int ClientVersion = 1;
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
        public const int MaximumConcurrentTextureLoads = 2;
        public const int AssetRequestTimeoutSeconds = 5;
        public const int LargeAssetRequestTimeoutSeconds = 10;
        public const long AssetCacheCapacityBytes = 256L * 1024 * 1024;
        public const long DecodedAssetCacheCapacityBytes = 256L * 1024 * 1024;
    }

    public static class ResourcePaths
    {
        public const string PrismaticFlowMap = "PrismaticFlowMap";
        public const string WorldLightingCompute = "Shaders/Lighting/WorldLighting";
        public const string PostProcessCompute = "Shaders/PostProcessing/PostProcess";
        public const string ScopesCompute = "Shaders/PostProcessing/Scopes";

        // Трасса состояний графического конвейера. Ассет проекта, а не файл в
        // persistentDataPath: из persistentDataPath в билд не попадает ничего,
        // а ассет едет со сборкой сам и приезжает к игроку.
        public const string GraphicsStateCollection = "Rendering/GraphicsStates";
        public const string MissionVirtualRingShader = "Shaders/UI/MissionVirtualRing";
        public const string GatewayUxml = "UI/Menus/Gateway";
        public const string MainMenuUxml = "UI/Menus/MainMenu";
        public const string AssetLoadingIndicatorUxml = "UI/Menus/AssetLoadingIndicator";
        public const string GlobalChatUxml = "UI/Gameplay/GlobalChat";
        public const string PlayerHudUxml = "UI/Gameplay/PlayerHUD";
        public const string ReconnectUxml = "UI/Menus/Reconnect";
        public const string InventoryUxml = "UI/Gameplay/Inventory";
        public const string BootstrapLoadingScreenUxml = "UI/Menus/BootstrapLoadingScreen";
        public const string ProgrammatorUxml = "UI/Gameplay/Programmator";
        public const string TooltipUxml = "UI/Overlays/Tooltip";
        public const string ModalWindowUxml = "UI/Overlays/ModalWindow";
        public const string ObserverJoystickUxml = "UI/Gameplay/ObserverJoystick";
        public const string RadialMenuUxml = "UI/Gameplay/RadialMenu";
        public const string PauseMenuUxml = "UI/Overlays/PauseMenu";
        public const string MinimapUxml = "UI/Gameplay/Minimap";
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
        public const string Starfield = "Kern/UI/Starfield";
        public const string MenuLineUnlit = "Kern/UI/MenuLineUnlit";
        public const string UnpremultiplyAlpha = "Kern/UI/UnpremultiplyAlpha";
        public const string MissionVirtualRing = "Kern/UI/MissionVirtualRing";
    }

    public static class ShaderPassNames
    {
        public const string LightingMaterialField = "LightingMaterialField";
        public const string LightingAmbientOcclusionField = "LightingAmbientOcclusionField";
    }

    public static class ComputeKernelNames
    {
        public const string SolveCascade = "SolveCascade";
        public const string ScrollRadianceAtlas = "ScrollRadianceAtlas";
        public const string ClearCascadeChangedMask = "ClearCascadeChangedMask";
        public const string SolveDynamicLighting = "SolveDynamicLighting";
        public const string ComposeDynamicLighting = "ComposeDynamicLighting";
        public const string TraceDynamicPolar = "TraceDynamicPolar";
        public const string ClearDynamicDirect = "ClearDynamicDirect";
        public const string ResolveDirect = "ResolveDirect";
        public const string ResolveTransmissionDebug = "ResolveTransmissionDebug";
        public const string CompositeLighting = "CompositeLighting";
        public const string BuildCellSolidMask = "BuildCellSolidMask";
        public const string BuildSurfaceAirCache = "BuildSurfaceAirCache";
    }

    public static class RequiredLayers
    {
        public const string WorldUI = "UI";
        public const string WorldUISortingLayer = "World UI";
        public const int TerrainSortingOrder = -1000;

        // Строго ниже террейна, а не вровень с ним. Оба рендерера рисуются с
        // альфа-блендингом в слое Default, и пока номер совпадал, порядок между
        // ними задавала не эта константа, а очередь материала и расстояние до
        // камеры. То есть он мог меняться от кадра к кадру и от положения
        // камеры: подложка мира то ложилась под террейн, то накрывала его
        // собственный фоновый слой.
        public const int WorldBackgroundSortingOrder = TerrainSortingOrder - 100;
    }

    public static class RuntimeLimits
    {
        public const int MaximumPacketBatchPerFrame = 250;
        public const int MaximumQueuedPacketCount = 4096;
        public const long MaximumQueuedPacketBytes = 16L * 1024 * 1024;
    }
}
