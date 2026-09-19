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
    void UpdateRobotMetadata(uint botID, RobotMetadata metadata);
    void UpdateRobotPosition(uint botID, ushort x, ushort y, byte rotation);
    void SetLocalPlayerBotID(uint botID);
    uint LocalPlayerBotID { get; }
    void ClearAllRobots();
    int RobotCount { get; }
}
