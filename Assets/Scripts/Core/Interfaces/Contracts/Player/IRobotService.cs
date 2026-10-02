#nullable enable

using UnityEngine;

namespace Kern.Core.Interfaces;
public readonly record struct RobotMetadata(
    int PlayerId,
    byte ClanId,
    string Nickname,
    string SkinPath,
    string TailPath);

public interface IRobotView
{
    Transform transform { get; }

    uint BotId { get; }

    bool IsMetadataLoaded { get; }

    bool IsVisualsLoaded { get; }

    float LogicalFacingAngle { get; }

    // Последняя позиция, присланная сервером. Ложь, пока сервер о боте
    // ничего не сообщал: у свежесозданного бота позиция — начало мира.
    bool TryGetServerPosition(out Vector3 position);

    void Initialize(uint botId);

    void SetMetadata(int playerId, byte clanId, string nickname, string skinPath, string tailPath);

    void SetPosition(ushort x, ushort y);

    void SetRotation(byte rotation);
}

public interface IRobotService
{
    void RegisterRobot(IRobotView robot);
    void UnregisterRobot(uint botId);
    IRobotView GetOrCreateRobot(uint botId);

    /// <summary>
    /// Возвращает только уже существующего робота и никогда не создаёт нового.
    /// Нужен там, где отсутствие сущности — штатная ситуация, а не повод
    /// материализовать призрака: локальный чат сервера приходит игрокам из
    /// чанков вокруг отправителя, которые могут быть вне клиентского вида.
    /// </summary>
    bool TryGetRobot(uint botId, out IRobotView? robot);

    void UpdateRobotMetadata(uint botId, RobotMetadata metadata);
    void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation);
    void SetLocalPlayerBotId(uint botId);
    uint LocalPlayerBotId { get; }
    void ClearAllRobots();
    void PruneStaleRobots(float timeoutSeconds = 2.5f);
    int RobotCount { get; }
}
