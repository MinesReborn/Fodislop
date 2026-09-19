#nullable enable

using MinesServer.Data;
// Протокол по-прежнему называет это Pack: PackType живёт во внешней сборке
// MinesServer.Data, исходников которой в проекте нет. Алиас держит границу —
// наш домен говорит Building, провод остаётся Pack.
using BuildingType = MinesServer.Data.PackType;

namespace Kern.Core.Interfaces;
public interface IBuildingService
{
    void AddOrUpdateBuilding(ushort x, ushort y, BuildingType buildingType, byte variant, byte linkedClan);
    void RemoveBuilding(ushort x, ushort y);
    void ClearAllBuildings();
}
