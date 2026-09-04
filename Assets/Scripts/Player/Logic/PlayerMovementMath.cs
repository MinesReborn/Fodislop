#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Player.Logic;

/// <summary>
/// Mathematical and coordinate conversions for 2D player grid movement, rotation, and digging.
/// </summary>
public static class PlayerMovementMath
{
    public static Vector2Int InputToDirection(Vector2 moveInput)
    {
        if (moveInput == Vector2.zero)
        {
            return Vector2Int.zero;
        }

        if (Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y))
        {
            return new Vector2Int(moveInput.x > 0 ? 1 : -1, 0);
        }

        return new Vector2Int(0, moveInput.y > 0 ? 1 : -1);
    }

    public static Direction ToPacketDirection(Vector2Int direction)
    {
        return direction.x switch
        {
            1 => Direction.Right,
            -1 => Direction.Left,
            _ => direction.y > 0 ? Direction.Up : Direction.Down,
        };
    }

    public static float DirectionToAngle(Vector2Int direction)
    {
        if (direction.x != 0)
        {
            return direction.x > 0 ? 0f : 180f;
        }

        return direction.y > 0 ? 90f : 270f;
    }

    public static Vector2Int DirectionToDigOffset(Direction direction)
    {
        return direction switch
        {
            Direction.Down => new Vector2Int(0, 1),
            Direction.Up => new Vector2Int(0, -1),
            Direction.Left => new Vector2Int(-1, 0),
            Direction.Right => new Vector2Int(1, 0),
            _ => Vector2Int.zero,
        };
    }

    public static Vector2Int MovementToDeltaServer(Vector2Int direction)
    {
        int deltaServerX = direction.x;
        int deltaServerY = direction.y > 0 ? -1 : (direction.y < 0 ? 1 : 0);
        return new Vector2Int(deltaServerX, deltaServerY);
    }

    public static float RotationByteToAngle(byte rotation) => rotation switch
    {
        0 => 270f,
        1 => 180f,
        2 => 90f,
        3 => 0f,
        _ => throw new System.ArgumentOutOfRangeException(nameof(rotation), rotation, "Unsupported robot rotation value."),
    };
}
