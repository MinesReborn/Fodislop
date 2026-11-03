#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World;

public sealed class BlockRegistry : IBlockRegistry
{
    private const string ResourcePath = "World/Cells/Configuration/cells";
    private const string RelativeFilePath = "Assets/Resources/World/Cells/Configuration/cells.json";

    private static BlockDefinition[] s_blockArray = new BlockDefinition[256];
    private static CellType s_underlayType;
    private static Dictionary<CellType, BlockDefinition> s_blocks = LoadRegistry();
    private static readonly BlockRegistry s_defaultInstance = new();

    public static IBlockRegistry Default => s_defaultInstance;

    public static IReadOnlyDictionary<CellType, BlockDefinition> Blocks => s_blocks;

    /// <summary>Тип с drawLayer: Underlay — подложка под каждым передним планом.</summary>
    public static CellType UnderlayType
    {
        get
        {
            _ = s_blocks;
            return s_underlayType;
        }
    }

    IReadOnlyDictionary<CellType, BlockDefinition> IBlockRegistry.All => s_blocks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Warmup()
    {
        _ = s_blocks;
    }

    public static BlockDefinition Get(CellType type) =>
        s_blockArray[(byte)type];

    BlockDefinition IBlockRegistry.Get(CellType type) =>
        s_blockArray[(byte)type];

    public static bool TryGet(CellType type, out BlockDefinition definition) =>
        s_blocks.TryGetValue(type, out definition);

    bool IBlockRegistry.TryGet(CellType type, out BlockDefinition definition) =>
        s_blocks.TryGetValue(type, out definition);

    /// <summary>Готовая к подмене таблица: собрана и проверена целиком, но ещё не видна клиенту.</summary>
    public sealed class Update
    {
        internal Update(BlockDefinition[] array, Dictionary<CellType, BlockDefinition> blocks, CellType underlay)
        {
            Array = array;
            Blocks = blocks;
            Underlay = underlay;
        }

        internal BlockDefinition[] Array { get; }

        internal Dictionary<CellType, BlockDefinition> Blocks { get; }

        internal CellType Underlay { get; }
    }

    /// <summary>
    /// Собирает таблицу типов из описаний сервера. Сервер — источник описаний: тип, которого нет в cells.json,
    /// появляется здесь, а у известного подменяются поля, которые сервер присылает. Задержка движения и имя группы
    /// тайлов в описании не едут и остаются прежними. Пустое описание (всё по умолчанию) типа не создаёт.
    /// Ничего не меняет: упавшая проверка оставляет прежнюю таблицу.
    /// </summary>
    public static Update Prepare(CellConfigurationPacket[] cells)
    {
        BlockDefinition[] array = (BlockDefinition[])s_blockArray.Clone();
        var blocks = new Dictionary<CellType, BlockDefinition>(s_blocks);
        int count = Math.Min(cells.Length, array.Length);
        for (int index = 0; index < count; index++)
        {
            CellConfigurationPacket cell = cells[index];
            BlockDefinition previous = array[index];
            var type = (CellType)index;
            if (!blocks.ContainsKey(type) && IsEmpty(cell))
            {
                continue;
            }

            var definition = new BlockDefinition(
                Passable: cell.Passable,
                MovementCooldownMs: previous.MovementCooldownMs,
                DrawLayer: (CellDrawLayer)cell.DrawLayer,
                Outline: (CellOutline)cell.Outline,
                TextureName: cell.TextureName ?? string.Empty,
                TextureAnchor: (CellTextureAnchor)cell.TextureAnchor,
                TileGroup: previous.TileGroup,
                DecalTextureName: string.IsNullOrEmpty(cell.DecalTextureName) ? null : cell.DecalTextureName,
                ReliefGroup: cell.ReliefGroup,
                AnimationType: cell.AnimationType,
                AnimationPalette: cell.AnimationPalette,
                FrameAnimationSpeed: cell.FrameAnimationSpeed,
                Emission: cell.Emission,
                MapColor: UnpackColor(cell.MapColor));
            array[index] = definition;
            blocks[type] = definition;
        }

        return new Update(array, blocks, ResolveUnderlay(blocks));
    }

    /// <summary>Подменяет таблицу целиком: потоки сборки видят либо прежнюю, либо новую, но не смесь.</summary>
    public static void Commit(Update update)
    {
        System.Threading.Volatile.Write(ref s_blockArray, update.Array);
        System.Threading.Volatile.Write(ref s_blocks, update.Blocks);
        s_underlayType = update.Underlay;
    }

    // Описание, в котором нет ничего, кроме цвета карты: сервер шлёт такие на неопределённые номера
    private static bool IsEmpty(in CellConfigurationPacket cell) =>
        !cell.Passable &&
        (int)cell.DrawLayer == 0 &&
        (int)cell.Outline == 0 &&
        string.IsNullOrEmpty(cell.TextureName) &&
        string.IsNullOrEmpty(cell.DecalTextureName) &&
        cell.ReliefGroup == 0 &&
        (int)cell.AnimationType == 0 &&
        cell.AnimationPalette == 0 &&
        cell.FrameAnimationSpeed == 0f &&
        !cell.Emission &&
        (int)cell.TextureAnchor == 0;

    private static Color32 UnpackColor(int argb) =>
        new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF), (byte)((argb >> 24) & 0xFF));

    private static Dictionary<CellType, BlockDefinition> LoadRegistry()
    {
        string json = LoadJsonContent();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        var rawMap = JsonSerializer.Deserialize<Dictionary<string, RawCellDefinition>>(json, options);
        if (rawMap == null || rawMap.Count == 0)
        {
            throw new InvalidDataException($"Failed to deserialize block config from '{RelativeFilePath}'.");
        }

        RejectUnknownKeys(json);
        var result = new Dictionary<CellType, BlockDefinition>(rawMap.Count);
        Array.Clear(s_blockArray, 0, s_blockArray.Length);

        foreach ((string cellName, RawCellDefinition raw) in rawMap)
        {
            if (!Enum.TryParse(cellName, out CellType cellType))
            {
                throw new InvalidDataException($"Cell '{cellName}': no such CellType.");
            }

            var def = new BlockDefinition(
                Passable: Required(raw.Passable, cellName, "passable"),
                MovementCooldownMs: Required(raw.MovementCooldownMs, cellName, "movementCooldownMs"),
                DrawLayer: ParseEnum<CellDrawLayer>(raw.DrawLayer, cellName, "drawLayer"),
                Outline: ParseEnum<CellOutline>(raw.Outline, cellName, "outline"),
                TextureName: RequiredString(raw.TextureName, cellName, "textureName"),
                TextureAnchor: ParseEnum<CellTextureAnchor>(raw.TextureAnchor, cellName, "textureAnchor"),
                TileGroup: raw.TileGroup,
                DecalTextureName: raw.DecalTextureName,
                ReliefGroup: Required(raw.ReliefGroup, cellName, "reliefGroup"),
                AnimationType: ParseEnum<CellAnimationType>(raw.AnimationType, cellName, "animationType"),
                AnimationPalette: Required(raw.AnimationPalette, cellName, "animationPalette"),
                FrameAnimationSpeed: Required(raw.FrameAnimationSpeed, cellName, "frameAnimationSpeed"),
                Emission: Required(raw.Emission, cellName, "emission"),
                MapColor: ParseColor(raw.MapColor, cellName, "mapColor"));

            result[cellType] = def;
            s_blockArray[(byte)cellType] = def;
        }

        s_underlayType = ResolveUnderlay(result);
        return result;
    }

    // Подложка — ровно один тип с drawLayer: Underlay.
    private static CellType ResolveUnderlay(Dictionary<CellType, BlockDefinition> blocks)
    {
        var underlays = new List<CellType>();
        foreach ((CellType type, BlockDefinition def) in blocks)
        {
            if (def.DrawLayer == CellDrawLayer.Underlay)
            {
                underlays.Add(type);
            }
        }

        if (underlays.Count != 1)
        {
            throw new InvalidDataException(
                $"cells.json: exactly one cell must have \"drawLayer\": \"Underlay\", found {underlays.Count}.");
        }

        return underlays[0];
    }

    // Ключ с опечаткой иначе молча пропал бы, а поле упало бы на «missing».
    private static void RejectUnknownKeys(string json)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Reflection.PropertyInfo property in typeof(RawCellDefinition).GetProperties())
        {
            known.Add(property.Name);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonProperty cell in document.RootElement.EnumerateObject())
        {
            foreach (JsonProperty field in cell.Value.EnumerateObject())
            {
                if (!known.Contains(field.Name))
                {
                    throw new InvalidDataException($"Cell '{cell.Name}': unknown key '{field.Name}'.");
                }
            }
        }
    }

    // Каждый ключ обязателен: пропуск — ошибка конфига, а не повод тихо
    // подставить значение, выведенное из других полей.
    private static T Required<T>(T? value, string cellName, string key)
        where T : struct =>
        value ?? throw new InvalidDataException($"Cell '{cellName}': missing '{key}'.");

    private static string RequiredString(string? value, string cellName, string key) =>
        value ?? throw new InvalidDataException($"Cell '{cellName}': missing '{key}'.");

    private static T ParseEnum<T>(string? value, string cellName, string key)
        where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException($"Cell '{cellName}': missing '{key}'.");
        }

        return Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(typeof(T), parsed)
            ? parsed
            : throw new InvalidDataException($"Cell '{cellName}': unknown {key} '{value}'.");
    }

    // #RRGGBB или #RRGGBBAA.
    private static Color32 ParseColor(string? value, string cellName, string key)
    {
        ReadOnlySpan<char> hex = value.AsSpan();
        if (hex.StartsWith("#"))
        {
            hex = hex[1..];
        }

        if ((hex.Length == 6 || hex.Length == 8) &&
            uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint bits))
        {
            uint rgba = hex.Length == 6 ? (bits << 8) | 0xFFu : bits;
            return new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        throw new InvalidDataException($"Cell '{cellName}': '{key}' must be #RRGGBB or #RRGGBBAA, got '{value}'.");
    }

    private static string LoadJsonContent()
    {
        string directPath = Path.Combine(Application.dataPath, "Resources/World/Cells/Configuration/cells.json");
        if (File.Exists(directPath))
        {
            return File.ReadAllText(directPath);
        }

        string workingDirPath = Path.Combine(Directory.GetCurrentDirectory(), RelativeFilePath);
        if (File.Exists(workingDirPath))
        {
            return File.ReadAllText(workingDirPath);
        }

        // Поднимаемся от текущей папки вверх; это нужно при запуске dotnet test из bin/Debug/net10.0/.
        DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, RelativeFilePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = dir.Parent;
        }

        TextAsset? asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset != null && !string.IsNullOrEmpty(asset.text))
        {
            return asset.text;
        }

        throw new FileNotFoundException(
            $"Master block config file not found at '{RelativeFilePath}' or Resources '{ResourcePath}'.");
    }

    // Ключи cells.json — те же имена, что у BlockDefinition.
    private sealed class RawCellDefinition
    {
        public bool? Passable { get; set; }

        public ushort? MovementCooldownMs { get; set; }

        public string? DrawLayer { get; set; }

        public string? Outline { get; set; }

        public string? TextureName { get; set; }

        public string? TextureAnchor { get; set; }

        public string? TileGroup { get; set; }

        public string? DecalTextureName { get; set; }

        public byte? ReliefGroup { get; set; }

        public string? AnimationType { get; set; }

        public byte? AnimationPalette { get; set; }

        public float? FrameAnimationSpeed { get; set; }

        public bool? Emission { get; set; }

        public string? MapColor { get; set; }
    }
}
