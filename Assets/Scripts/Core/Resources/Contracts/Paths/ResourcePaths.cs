#nullable enable

namespace Kern.Core;

public static class ResourcePaths
{
    public const string PrismaticFlowMap = "World/Terrain/Animation/Flow/PrismaticFlowMap";
    public const string WorldLightingCompute = "Shaders/Lighting/Pipeline/WorldLighting";
    public const string WorldMapCompute = "Shaders/UI/Map/Rendering/WorldMap";
    public const string PostProcessCompute = "Shaders/PostProcessing/Display/PostProcess";
    public const string CalibrationMarkTexture = "Rendering/Calibration/Textures/CalibrationMark";
    public const string ScopesCompute = "Shaders/PostProcessing/Scopes/Scopes";

    // Трасса состояний графического конвейера. Ассет проекта, а не файл в
    // persistentDataPath: из persistentDataPath в билд не попадает ничего,
    // а ассет едет со сборкой сам и приезжает к игроку.
    public const string GraphicsStateCollection = "Rendering/Warmup/States/GraphicsStates";
    public const string MissionVirtualRingShader = "Shaders/UI/Missions/Arrow/MissionVirtualRing";
    public const string CellDestructionShader = "Shaders/Effects/Cells/Destruction/CellDestruction";
    public const string GatewayUxml = "UI/Gateway/Templates/GatewayOnboarding";
    public const string MainMenuUxml = "UI/Menu/Templates/MainMenu";
    public const string AssetLoadingIndicatorUxml = "UI/Loading/Assets/Templates/AssetLoadingIndicator";
    public const string GlobalChatUxml = "UI/Chat/Global/Templates/GlobalChat";
    public const string LocalChatUxml = "UI/Chat/Local/Templates/LocalChat";
    public const string PlayerHudUxml = "UI/HUD/Player/Templates/PlayerHUD";
    public const string ReconnectUxml = "UI/Connection/Reconnection/Templates/Reconnect";
    public const string InventoryUxml = "UI/Inventory/Templates/Inventory";
    public const string BootstrapLoadingScreenUxml = "UI/Loading/Bootstrap/Templates/BootstrapLoadingScreen";
    public const string ProgrammatorUxml = "UI/Programmator/Grid/Templates/Programmator";
    public const string TooltipUxml = "UI/Tooltips/Templates/Tooltip";
    public const string ModalWindowUxml = "UI/Windows/Modal/Templates/ModalWindow";
    public const string LinkDialogUxml = "UI/Windows/Link/Templates/LinkDialog";
    public const string ObserverJoystickUxml = "UI/Programmator/Observer/Templates/ObserverJoystick";
    public const string ProgrammatorPaletteUxml = "UI/Programmator/Palette/Templates/ProgrammatorPalette";
    public const string PauseMenuUxml = "UI/Settings/Pause/Templates/PauseMenu";
    public const string SettingsWindowUxml = "UI/Settings/Window/Templates/SettingsWindow";
    public const string CalibrationUxml = "UI/Settings/Calibration/Templates/Calibration";
    public const string MinimapUxml = "UI/Map/Minimap/Templates/Minimap";

    // Лист пиксельных цифр счётчика добычи (раскладка — CrystalDigitSheet).
    public const string CrystalDigitsTexture = "Effects/Crystals/Digits/CrystalDigits";

}
