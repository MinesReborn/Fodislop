#nullable enable

using System;
using System.Globalization;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyAdminCommands(
    Action<ServerPacket> sendPacket,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    IDummyClock clock)
{
    private const string CommandPrefix = "/";

    private const CellType DefaultPlacedCell = CellType.SuperRainbow;

    private static readonly System.Drawing.Color _ServerColor =
        System.Drawing.Color.FromArgb(255, 255, 180, 60);

    public bool TryHandle(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string trimmed = message.Trim();
        if (!trimmed.StartsWith(CommandPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = trimmed[CommandPrefix.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "help":
                Help();
                return true;
            case "tp":
                Teleport(parts);
                return true;
            case "set":
                ApplySetCommand(parts);
                return true;
            default:
                Reply($"Неизвестная команда «{parts[0]}». Список — /help");
                return true;
        }
    }

    private void Help()
    {
        Reply("Команды:");
        Reply("/help — этот список");
        Reply("/tp <x> <y> — телепорт в указанную клетку");
        Reply($"/set [тип] — поставить блок перед роботом (без аргумента — {DefaultPlacedCell})");
    }

    private void Teleport(string[] parts)
    {
        if (parts.Length < 3)
        {
            Reply("Нужны обе координаты: /tp <x> <y>");
            return;
        }

        if (!TryParseCoordinate(parts[1], out ushort x) ||
            !TryParseCoordinate(parts[2], out ushort y))
        {
            Reply("Координаты — целые числа от 0 до 65535.");
            return;
        }

        playerState.SetPosition(x, y);
        worldState.QueueChunksAround(x, y, sendPacket);
        sendPacket(new ServerPacket(new TeleportPacket(x, y, false)));
        Reply($"Телепорт в ({x}, {y}).");
    }

    private void ApplySetCommand(string[] parts)
    {
        CellType placed = DefaultPlacedCell;
        if (parts.Length >= 2 && !Enum.TryParse(parts[1], ignoreCase: true, out placed))
        {
            Reply($"Неизвестный тип клетки «{parts[1]}».");
            return;
        }

        (int offsetX, int offsetY) = playerState.Direction switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };

        int targetX = playerState.X + offsetX;
        int targetY = playerState.Y + offsetY;
        if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue)
        {
            Reply("Перед роботом край мира.");
            return;
        }

        var cellX = (ushort)targetX;
        var cellY = (ushort)targetY;
        worldState.SetCell(cellX, cellY, placed);

        // Тот же пакет, которым отвечает постройка: клиент не должен различать,
        // откуда клетка изменилась — командой или обычным действием.
        sendPacket(new ServerPacket(new HBPacket(
        [
            new MapRegionPacket(cellX, cellY, 0, 0, [placed]),
        ])));

        Reply($"{placed} поставлен в ({cellX}, {cellY}).");
    }

    private static bool TryParseCoordinate(string text, out ushort value) =>
        ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void Reply(string text)
    {
        long now = DummyClockTime.UnixMilliseconds(clock);
        var message = new ChatMessagePacket(
            now,
            now,
            0,
            0,
            _ServerColor,
            "Сервер",
            _ServerColor,
            text);
        sendPacket(new ServerPacket(new ChatMessageListPacket("global", [message])));
    }
}
