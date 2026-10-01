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

    /// <summary>
    /// Шаблон пака для выбранного предмета инвентаря. Соответствие повторяет
    /// серверный Inventory.typeditems: каждый пак-предмет ставит свой класс
    /// здания (ScienceCentre ставит NC — на проводе он PackType.Science).
    /// </summary>
    public static bool TryGetByItem(ItemType item, out PackBuilding? building)
    {
        PackType? type = item switch
        {
            ItemType.Teleport => PackType.Teleport,
            ItemType.Resp => PackType.Resp,
            ItemType.Up => PackType.Up,
            ItemType.Market => PackType.Market,
            ItemType.Clans => PackType.Clans,
            ItemType.Craft => PackType.Craft,
            ItemType.BombShop => PackType.BombShop,
            ItemType.Storage => PackType.Storage,
            ItemType.ScienceCentre => PackType.Science,
            _ => null,
        };

        building = type is { } resolved && TryGet(resolved, out PackBuilding? found)
            ? found
            : null;
        return building != null;
    }

    public static ushort GetAnchorDistance(PackType type) => type switch
    {
        PackType.Up or PackType.Clans => 3,
        PackType.Science => 6,
        _ => 2,
    };
}
