#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Buildings;
public static class BuildingTemplates
{
    private static readonly Dictionary<PackType, PackBuilding> _templates = new()
    {
        [PackType.Teleport] = new Teleport(),
        [PackType.Resp] = new RespawnStation(),
        [PackType.Up] = new UpgradeStation(),
        [PackType.Market] = new Market(),
        [PackType.Clans] = new ClansPack(),
        [PackType.Craft] = new Crafter(),
        [PackType.BombShop] = new BuildingShop(),
        [PackType.Gun] = new Gun(),
        [PackType.Storage] = new Storage(),
        [PackType.Science] = new NC(),
    };

    public static bool TryGet(PackType type, out PackBuilding? building) =>
        _templates.TryGetValue(type, out building);

    public static ushort GetAnchorDistance(PackType type) => type switch
    {
        PackType.Up or PackType.Clans => 3,
        PackType.Science => 6,
        _ => 2,
    };
}
