#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyCellConfigurationUtilities
{
    private const byte GreenBlueRockReliefGroup = 6;

    private static readonly HashSet<CellType> _ConfiguredTypes = [];

    public static CellConfigurationPacket[] CreateCellConfigurations()
    {
        _ConfiguredTypes.Clear();
        var configs = new CellConfigurationPacket[256];
        for (int i = 0; i < 256; i++)
        {
            configs[i] = new CellConfigurationPacket
            {
                Animation = CellAnimationType.None,
                AnimationSpeed = 0,
                FrameOffset = 0,
                Properties = CellConfigProperties.None,
                ReliefGroup = 0,
                Distortion = (CellDistortionType)0,
            };
        }

        const CellConfigProperties ROAD_PROPS = CellConfigProperties.Passable;
        const CellConfigProperties DESTRUCTIBLE_SHADOW_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow;
        const CellConfigProperties GLOWING_CRYSTAL_PROPS = DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing;
        const CellConfigProperties INDESTRUCTIBLE_PROPS = CellConfigProperties.DropsShadow;

        SetConfig(configs, CellType.BuildingRoad, ROAD_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.VolcanoBackground, ROAD_PROPS | CellConfigProperties.Glowing, 0);
        SetConfig(configs, CellType.Empty, ROAD_PROPS, 0);
        SetConfig(configs, CellType.Road, ROAD_PROPS, 0);
        SetConfig(configs, CellType.GoldenRoad, ROAD_PROPS, 0);
        SetConfig(configs, CellType.PolymerRoad, ROAD_PROPS, 0);
        SetConfig(configs, CellType.Box, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);

        SetConfig(configs, CellType.BlackBoulder1, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BlackBoulder2, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BlackBoulder3, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MetalBoulder1, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MetalBoulder2, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MetalBoulder3, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.WhiteSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.DarkWhiteSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.RustySand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.DarkRustySand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BlackSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.DarkBlackSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BlueSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.DarkBlueSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.YellowSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.DarkYellowSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.DeepMagmaBoulder, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MilitaryBlockSand, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Lava, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Boulder1, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Boulder2, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Boulder3, DESTRUCTIBLE_SHADOW_PROPS, 1, distortion: CellDistortionType.Block);

        SetConfig(configs, CellType.GrayAcid, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, animation: CellAnimationType.Blinking, animationSpeed: 5, frameOffset: 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.PurpleAcid, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, animation: CellAnimationType.Shimmer, animationSpeed: 50, frameOffset: 1, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.PassiveAcid, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.LivingActiveAcid, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.CorrosiveActiveAcid, DESTRUCTIBLE_SHADOW_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.BuildingDoor, INDESTRUCTIBLE_PROPS | CellConfigProperties.Passable, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BuildingCorner, INDESTRUCTIBLE_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.QuadBlock, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Support, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MilitaryBlockFrame, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.MilitaryBlock, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.GreenBlock, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.YellowBlock, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.FedBlock, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.RedBlock, DESTRUCTIBLE_SHADOW_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.BuildingWall, INDESTRUCTIBLE_PROPS, 0, distortion: CellDistortionType.Block);

        // Зелёные и синие кристаллы с пустоскалом образуют отдельную общую
        // группу, не сливающуюся с остальными кристаллами и породами.
        SetConfig(configs, CellType.XGreen, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XBlue, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XRed, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XCyan, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.XViolet, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepObsidianRock, DESTRUCTIBLE_SHADOW_PROPS, 5, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepTurquoiseRock, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepRainbowRock, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepStripedRock, DESTRUCTIBLE_SHADOW_PROPS, 5, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Rock, DESTRUCTIBLE_SHADOW_PROPS, GreenBlueRockReliefGroup, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Green, GLOWING_CRYSTAL_PROPS, GreenBlueRockReliefGroup, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Red, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Blue, GLOWING_CRYSTAL_PROPS, GreenBlueRockReliefGroup, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Violet, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.White, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Cyan, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.HeavyRock, DESTRUCTIBLE_SHADOW_PROPS, 5, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.AcidRock, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.GoldenRock, DESTRUCTIBLE_SHADOW_PROPS, 5, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepRock, DESTRUCTIBLE_SHADOW_PROPS, 5, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.GRock, DESTRUCTIBLE_SHADOW_PROPS, 5, distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.AliveCyan, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.AliveRed, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.AliveViol, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.AliveBlack, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.AliveWhite, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.AliveRainbow, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.AliveBlue, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.Pearl, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.DeepLazuriteSand, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.SuperRainbow, GLOWING_CRYSTAL_PROPS, 3);
        SetConfig(configs, CellType.HypnoRock, GLOWING_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);

        SetConfig(configs, CellType.BlackRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.LivingBlackRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.RedRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
        SetConfig(configs, CellType.Gate, ROAD_PROPS, 0, distortion: CellDistortionType.Block);
        SetConfig(configs, CellType.TeleportBlock, ROAD_PROPS, 0, distortion: CellDistortionType.Block);

        RequireEveryCellTypeConfigured();
        ApplyMapColors(configs);
        return configs;
    }

    private static void ApplyMapColors(CellConfigurationPacket[] configs)
    {
        for (int cellID = 0; cellID < configs.Length; cellID++)
        {
            configs[cellID] = configs[cellID] with { Color = DummyMapColors.Get(cellID) };
        }
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
        CellAnimationType animation = CellAnimationType.None,
        byte animationSpeed = 0,
        byte frameOffset = 0,
        CellDistortionType distortion = (CellDistortionType)0)
    {
        _ConfiguredTypes.Add(type);
        configs[(int)type] = new CellConfigurationPacket
        {
            Properties = props,
            ReliefGroup = reliefGroup,
            Animation = animation,
            AnimationSpeed = animationSpeed,
            FrameOffset = frameOffset,
            Distortion = distortion,
        };
    }

    private static readonly CellType[] _KnownUnconfiguredTypes =
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
            if (!_ConfiguredTypes.Contains(type) && Array.IndexOf(_KnownUnconfiguredTypes, type) < 0)
            {
                missing.Add(type);
            }
        }

        if (missing.Count > 0)
        {
            // Ошибка в лог, а не исключение. CellType живёт во внешнем пакете
            // (darkar25.kern.data), и обновление зависимости добавляет
            // значения без участия этого файла. Падать на инициализации мира
            // из-за чужого коммита — хуже той тишины, которую здесь чинят:
            // клетка без конфигурации отрисуется серой заглушкой, как и
            // раньше, но теперь об этом будет сказано.
            UnityEngine.Debug.LogError(
                "[DummyCellConfiguration] Эти типы клеток отрисуются нейтральной серой " +
                "заглушкой, потому что конфигурация им не задана: " + string.Join(", ", missing) +
                ". Добавьте строку SetConfig либо внесите тип в _KnownUnconfiguredTypes с причиной.");
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
