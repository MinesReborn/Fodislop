#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;


namespace MinesServer.Networking.Connection.Client;

internal static class DummyCellConfigurationUtilities
{
    /// <summary>
    /// Типы клеток, которым конфигурация задана явно вызовом SetConfig.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ. Массив на 256 позиций предзаполняется нейтральной серой
    /// заглушкой — она нужна тем индексам байтового домена, которым не
    /// соответствует ни один CellType. Побочный эффект: новый тип клетки, для
    /// которого забыли строку SetConfig, тоже получал эту заглушку и выглядел
    /// в игре как серый непроходимый неразрушаемый блок. Ни ошибки, ни
    /// предупреждения — просто клетка ведёт себя не так, и причину надо искать
    /// глазами по списку из девяноста строк.
    /// </remarks>
    private static readonly HashSet<CellType> ConfiguredTypes = [];

    public static CellConfigurationPacket[] CreateCellConfigurations()
    {
        ConfiguredTypes.Clear();
        var configs = new CellConfigurationPacket[256];
        for (int i = 0; i < 256; i++)
        {
            configs[i] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                Color = unchecked((int)0xFF808080),
                FrameOffset = 0,
                Properties = CellConfigProperties.None,
                ReliefGroup = 0,
                Distortion = (CellDistortionType)0,
            };
        }

        const CellConfigProperties ROAD_PROPS = CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow;
        const CellConfigProperties SAND_BOULDER_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;
        const CellConfigProperties ARTIFICIAL_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing;
        const CellConfigProperties ROCK_CRYSTAL_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;
        const CellConfigProperties GLOWING_CRYSTAL_PROPS = ROCK_CRYSTAL_PROPS | CellConfigProperties.Glowing;
        const CellConfigProperties INDESTRUCTIBLE_PROPS = CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;
        const CellConfigProperties BOX_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing;

        SetConfig(configs, CellType.BuildingRoad, ROAD_PROPS | CellConfigProperties.Glowing, 0, color: unchecked((int)0xFFCCCCCC));
        SetConfig(configs, CellType.VolcanoBackground, ROAD_PROPS | CellConfigProperties.Glowing, 0);
        SetConfig(configs, CellType.Empty, ROAD_PROPS, 0, color: unchecked((int)0xFF808080));
        SetConfig(configs, CellType.Road, ROAD_PROPS, 0, color: unchecked((int)0xFFCCCCCC));
        SetConfig(configs, CellType.GoldenRoad, ROAD_PROPS, 0, color: unchecked((int)0xFFCCCC00));
        SetConfig(configs, CellType.PolymerRoad, ROAD_PROPS, 0);
        SetConfig(configs, CellType.Box, BOX_PROPS, 0, distortion: CellDistortionType.Block);

        SetConfig(configs, CellType.BlackBoulder1, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF000000), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.BlackBoulder2, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.BlackBoulder3, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.MetalBoulder1, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.MetalBoulder2, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.MetalBoulder3, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.WhiteSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFFFFF00), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DarkWhiteSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFCCCC00), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.RustySand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFCD853F), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DarkRustySand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF8B4513), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.BlackSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF2F2F2F), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DarkBlackSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF1A1A1A), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.BlueSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF4169E1), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DarkBlueSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF00008B), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.YellowSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFFFD700), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DarkYellowSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFB8860B), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepMagmaBoulder, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.MilitaryBlockSand, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Lava, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFFFF4500), animation: (CellAnimationType)4, animationSpeed: 10, frameOffset: 0, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Boulder1, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF000000), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Boulder2, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Boulder3, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.GrayAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF00FF00), animation: CellAnimationType.Blinking, animationSpeed: 5, frameOffset: 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.PurpleAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF800080), animation: CellAnimationType.Shimmer, animationSpeed: 50, frameOffset: 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.PassiveAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF8A2BE2), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.LivingActiveAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF66FF22), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.CorrosiveActiveAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF9AFF22), distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.BuildingDoor, INDESTRUCTIBLE_PROPS | CellConfigProperties.Passable, 2, color: unchecked((int)0xFF8B4513), distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BuildingCorner, INDESTRUCTIBLE_PROPS, 2, color: unchecked((int)0xFF555555), distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.QuadBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Support, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MilitaryBlockFrame, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MilitaryBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.GreenBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.YellowBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.FedBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.RedBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BuildingWall, INDESTRUCTIBLE_PROPS, 2, color: unchecked((int)0xFF666666), distortion: CellDistortionType.Block);

        SetConfig(configs, CellType.XGreen, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF00FF3D), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XBlue, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XRed, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF2920), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XCyan, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XViolet, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepObsidianRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepTurquoiseRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepRainbowRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF59E6), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepStripedRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Rock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Green, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF00FF00), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Red, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF2920), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Blue, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Violet, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.White, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFF2F7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Cyan, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.HeavyRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AcidRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.GoldenRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.GRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.AliveCyan, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AliveRed, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF2920), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AliveViol, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AliveNigger, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF802EB8), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AliveWhite, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFF2F7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AliveRainbow, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF59E6), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AliveBlue, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Pearl, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFF2F7FF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepLazuriteSand, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.SuperRainbow, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF59E6));
        SetConfig(configs, CellType.HypnoRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.NiggerRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.LivingBlackRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.RedRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Gate, CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing, 0);
        SetConfig(configs, CellType.TeleportBlock, CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing, 0);

        RequireEveryCellTypeConfigured();
        return configs;
    }

    public static int GetCrystalBasketIndex(CellType cell)
    {
        return cell switch
        {
            CellType.Green => 0,
            CellType.Blue => 1,
            CellType.Red => 2,
            CellType.Violet => 3,
            CellType.White => 4,
            CellType.Cyan => 5,
            _ => -1,
        };
    }

    public static ItemType PickRandomBonusItem(Random random)
    {
        var items = new[]
        {
            ItemType.Teleport, ItemType.Compressor, ItemType.C190, ItemType.Trans,
            ItemType.Nano, ItemType.Battery, ItemType.ConstructionBot, ItemType.PortableTeleporter,
            ItemType.Scanner, ItemType.GeoBlackRock, ItemType.GeoRedRock, ItemType.Cred,
            ItemType.GeoCyan, ItemType.GeoHypno, ItemType.Rem, ItemType.Charge,
            ItemType.Geopack, ItemType.Poly, ItemType.RazBomb, ItemType.ProtonBomb,
        };
        return items[random.Next(items.Length)];
    }

    public static long PickRandomAmount(ItemType item, Random random)
    {
        return item switch
        {
            ItemType.Teleport or ItemType.PortableTeleporter => 1,
            ItemType.Cred => random.Next(5, 11),
            ItemType.Rem => random.Next(50, 101),
            ItemType.Geopack => random.Next(10, 16),
            _ => random.Next(5, 20),
        };
    }

    public static void SetConfig(
        CellConfigurationPacket[] configs,
        CellType type,
        CellConfigProperties props,
        byte reliefGroup,
        int color = unchecked((int)0xFF808080),
        CellAnimationType animation = CellAnimationType.None,
        byte animationSpeed = 0,
        byte frameOffset = 0,
        CellDistortionType distortion = (CellDistortionType)0)
    {
        ConfiguredTypes.Add(type);
        configs[(int)type] = new CellConfigurationPacket
        {
            Properties = props,
            ReliefGroup = reliefGroup,
            Color = color,
            Animation = animation,
            AnimationSpeed = animationSpeed,
            FrameOffset = frameOffset,
            Distortion = distortion,
        };
    }

    /// <summary>
    /// Типы, которым конфигурация сознательно не задана.
    /// </summary>
    /// <remarks>
    /// Эти пять сидят на нейтральной серой заглушке и сидели на ней всегда —
    /// просто об этом никто не знал, потому что заглушка выдаётся молча.
    /// Список — не разрешение, а протокол: он фиксирует, что именно сейчас
    /// не настроено, и заставляет шестой такой тип падать, а не прятаться.
    ///
    /// Unloaded и Pregener на заглушке, видимо, законно: это состояния «чанк
    /// ещё не приехал». Про Skull и две фоновые с следами решение за автором
    /// мира — они выглядят как настоящие клетки, которым конфигурация нужна.
    /// </remarks>
    private static readonly CellType[] KnownUnconfiguredTypes =
    [
        CellType.Unloaded,
        CellType.Pregener,
        CellType.BackgroundWithLightTraces,
        CellType.BackgroundWithHeavyTraces,
        CellType.Skull,
    ];

    private static void RequireEveryCellTypeConfigured()
    {
        var missing = new List<CellType>();
        foreach (CellType type in Enum.GetValues(typeof(CellType)))
        {
            if (!ConfiguredTypes.Contains(type) && Array.IndexOf(KnownUnconfiguredTypes, type) < 0)
            {
                missing.Add(type);
            }
        }

        if (missing.Count > 0)
        {
            // Ошибка в лог, а не исключение. CellType живёт во внешнем пакете
            // (darkar25.fodinae.data), и обновление зависимости добавляет
            // значения без участия этого файла. Падать на инициализации мира
            // из-за чужого коммита — хуже той тишины, которую здесь чинят:
            // клетка без конфигурации отрисуется серой заглушкой, как и
            // раньше, но теперь об этом будет сказано.
            UnityEngine.Debug.LogError(
                "[DummyCellConfiguration] Эти типы клеток отрисуются нейтральной серой " +
                "заглушкой, потому что конфигурация им не задана: " + string.Join(", ", missing) +
                ". Добавьте строку SetConfig либо внесите тип в KnownUnconfiguredTypes с причиной.");
        }
    }

    public static Dictionary<CellType, ushort> CreateMovementSpeeds(
        CellConfigurationPacket[] configurations)
    {
        var speeds = new Dictionary<CellType, ushort>(configurations.Length);
        for (int index = 0; index < configurations.Length; index++)
        {
            CellConfigurationPacket configuration = configurations[index];
            if (configuration.Properties == CellConfigProperties.None &&
                index != (int)CellType.Empty)
            {
                continue;
            }

            bool passable = (configuration.Properties & CellConfigProperties.Passable) != 0;
            speeds[(CellType)index] = (ushort)(passable ? 20 : 100);
        }

        return speeds;
    }
}
