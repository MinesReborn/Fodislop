#nullable enable

using UnityEngine;

namespace Kern.Core.Interfaces;
public readonly record struct RobotMetadata(
    int PlayerID,
    byte ClanID,
    string Nickname,
    string SkinPath,
    string TailPath);

public interface IRobotView
{
    Transform transform { get; }

    uint BotID { get; }

    bool IsMetadataLoaded { get; }

    bool IsVisualsLoaded { get; }

    float LogicalFacingAngle { get; }

    // Последняя позиция, присланная сервером. Ложь, пока сервер о боте
    // ничего не сообщал: у свежесозданного бота позиция — начало мира.
    bool TryGetServerPosition(out Vector3 position);

    void Initialize(uint botID);

    void SetMetadata(int playerID, byte clanID, string nickname, string skinPath, string tailPath);

    void SetPosition(ushort x, ushort y);

    void SetRotation(byte rotation);
}

public interface IRobotService
{
    void RegisterRobot(IRobotView robot);
    void UnregisterRobot(uint botID);
    IRobotView GetOrCreateRobot(uint botID);

    /// <summary>
    /// Возвращает только уже существующего робота и никогда не создаёт нового.
    /// Нужен там, где отсутствие сущности — штатная ситуация, а не повод
    /// материализовать призрака: локальный чат сервера приходит игрокам из
    /// чанков вокруг отправителя, которые могут быть вне клиентского вида.
    /// </summary>
    bool TryGetRobot(uint botID, out IRobotView? robot);

    void UpdateRobotMetadata(uint botID, RobotMetadata metadata);
    void UpdateRobotPosition(uint botID, ushort x, ushort y, byte rotation);
    void SetLocalPlayerBotID(uint botID);
    uint LocalPlayerBotID { get; }
    void ClearAllRobots();
    void PruneStaleRobots(float timeoutSeconds = 2.5f);
    int RobotCount { get; }
}
