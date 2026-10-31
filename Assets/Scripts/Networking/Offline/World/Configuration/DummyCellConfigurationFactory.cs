#nullable enable

using System;
using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Offline;

internal static class DummyCellConfigurationFactory
{
    public static BlockDefinition GetBlockDefinition(CellType type) =>
        BlockRegistry.Get(type);

    // Правило экономики сервера, а не свойство блока: добыча кристалла кладёт
    // его в корзину, слот — номер CrystalType в BasketPacket.

    public static byte[][] CreateTileGroups()
    {
        var pack = new List<byte>();
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            if (def.Outline is CellOutline.Wall or CellOutline.Corner or CellOutline.Door)
            {
                pack.Add((byte)type);
            }
        }

        pack.Sort();
        return new[] { pack.ToArray() };
    }

    public static CellConfigurationPacket[] CreateCellConfigurations()
    {
        var configs = new CellConfigurationPacket[256];
        for (int i = 0; i < 256; i++)
        {
            var type = (CellType)i;
            BlockDefinition def = BlockRegistry.Get(type);

            // Визуальные поля (слой, контур, декаль и пр.) описаны на клиенте
            // в cells.json (BlockRegistry) и больше не входят в пакет.
            var properties = CellConfigProperties.None;
            if (def.IsPassable)
            {
                properties |= CellConfigProperties.Passable;
            }

            if (def.Emission)
            {
                properties |= CellConfigProperties.Glowing;
            }

            configs[i] = new CellConfigurationPacket(
                Properties: properties,
                Distortion: CellDistortionType.Neutral,
                Animation: def.ShaderAnimation,
                AnimationSpeed: (byte)def.FrameAnimationSpeed,
                FrameOffset: 0,
                Color: DummyMapColors.Get(i),
                ReliefGroup: def.ReliefGroup);
        }

        return configs;
    }

    public static Dictionary<CellType, ushort> CreateMovementSpeeds(
        CellConfigurationPacket[] configurations)
    {
        var speeds = new Dictionary<CellType, ushort>(BlockRegistry.Blocks.Count);
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            // Правило сервера: по проходимому — быстро, по остальному — медленно.
            speeds[type] = (ushort)(def.IsPassable ? 20 : 100);
        }

        return speeds;
    }
}
