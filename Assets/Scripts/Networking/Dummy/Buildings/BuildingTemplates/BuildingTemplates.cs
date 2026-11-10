#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Dummy;
public static class BuildingTemplates
{
    private static readonly Dictionary<BuildingType, PackBuilding> s_templates = new()
    {
        [BuildingType.Teleport] = new Teleport(),
        [BuildingType.Resp] = new RespawnStation(),
        [BuildingType.Up] = new UpgradeStation(),
        [BuildingType.Market] = new Market(),
        [BuildingType.Clans] = new ClansPack(),
        [BuildingType.Craft] = new Crafter(),
        [BuildingType.BombShop] = new BuildingShop(),
        [BuildingType.Gun] = new Gun(),
        [BuildingType.Storage] = new Storage(),
        [BuildingType.Science] = new NC(),
    };

    public static bool TryGet(BuildingType type, out PackBuilding? building) =>
        s_templates.TryGetValue(type, out building);

    public static ushort GetAnchorDistance(BuildingType type) => type switch
    {
        BuildingType.Resp => 1,
        BuildingType.Up => 4,
        BuildingType.Clans => 3,
        BuildingType.Science => 6,
        _ => 2,
    };
}
