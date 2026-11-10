#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.Game.Player.Movement;

public static class PlayerDirectionMapping
{
    // Восемь секторов по 45°: прямые стороны дают одну ось, диагональные —
    // две. Клавиатурное W+A (135°) попадает ровно в центр диагонального
    // сектора, стик и тач — по углу отклонения.
    public static MovementIntent InputToIntent(Vector2 moveInput)
    {
        if (moveInput == Vector2.zero)
        {
            return MovementIntent.None;
        }

        float angle = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg;
        int sector = ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;
        int x = sector is 7 or 0 or 1 ? 1 : sector is 3 or 4 or 5 ? -1 : 0;
        int y = sector is 1 or 2 or 3 ? 1 : sector is 5 or 6 or 7 ? -1 : 0;
        if (x == 0 || y == 0)
        {
            return new MovementIntent(new Vector2Int(x, y), Vector2Int.zero);
        }

        var horizontal = new Vector2Int(x, 0);
        var vertical = new Vector2Int(0, y);
        return Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y)
            ? new MovementIntent(horizontal, vertical)
            : new MovementIntent(vertical, horizontal);
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
