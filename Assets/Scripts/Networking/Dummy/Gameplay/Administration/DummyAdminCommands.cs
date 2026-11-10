#nullable enable

using System;
using System.Globalization;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Contracts.Packets;

namespace Kern.Networking.Dummy;

internal sealed class DummyAdminCommands
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyWorldSimulationState _worldState;
    private readonly IDummyClock _clock;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly Func<int> _getLifecycleVersion;
    private readonly Func<int, bool> _loopAlive;
    private readonly ushort _playerBotId;
    private bool _unloadedView;
    private int _staticBotsStartedLifecycleVersion;
    private int _circularBotsStartedLifecycleVersion;
    internal DummyAdminCommands(
    Action<ServerPacket> sendPacket,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    IDummyClock clock,
    ushort playerBotId,
    IAsyncOperationSupervisor operations,
    Func<int> getLifecycleVersion,
    Func<int, bool> loopAlive)
    {
        _sendPacket = sendPacket;
        _playerState = playerState;
        _worldState = worldState;
        _clock = clock;
        _playerBotId = playerBotId;
        _operations = operations;
        _getLifecycleVersion = getLifecycleVersion;
        _loopAlive = loopAlive;
    }

    private const string CommandPrefix = "/";

    private const CellType DefaultPlacedCell = CellType.SuperRainbow;
    private const int FillWidth = 100;
    private const int FillHeight = 100;
    private const CellType DefaultFillCell = CellType.SuperRainbow;

    private static readonly System.Drawing.Color s_serverColor =
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
            case "visual":
                PlayVisualEffect(parts);
                return true;
            case "set":
                ApplySetCommand(parts);
                return true;
            case "fill":
                ApplyFillCommand(parts);
                return true;
            case "unloaded":
                ToggleUnloadedView();
                return true;
            case "bots-static":
                SpawnStaticBots();
                return true;
            case "bots-circle":
                StartCircularBots();
                return true;
            case "afk":
                ApplyAfkCommand(parts);
                return true;
            case "skin":
                ApplySkinCommand(parts);
                return true;
            case "tail":
                ApplyTailCommand(parts);
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
        Reply("/visual list — список игровых визуальных эффектов; /visual <тип> [тип бомбы] — вызвать через пакет визуального эффекта");
        Reply($"/set [тип] — поставить блок перед роботом (без аргумента — {DefaultPlacedCell})");
        Reply(
            $"/fill [тип] — заполнить область {FillWidth}x{FillHeight} вокруг робота " +
            $"(без аргумента — {DefaultFillCell})");
        Reply("/unloaded — включить/выключить отображение всех клеток как Unloaded");
        Reply("/bots-static — создать 100 неподвижных роботов вокруг игрока");
        Reply("/bots-circle — создать 100 роботов, движущихся по кругу вокруг игрока");
        Reply("/afk [on|off] — включить/выключить капсулу AFK (без аргумента — переключить)");
        Reply($"/skin [имя|next|list] — сменить скин робота (без аргумента — следующий). Скины: {string.Join(", ", DummyRobotAssets.SkinNames)}");
        Reply($"/tail [имя|next|list] — сменить хвост робота (без аргумента — следующий). Хвосты: {string.Join(", ", DummyRobotAssets.TailNames)}");
    }

    private void ApplyAfkCommand(string[] parts)
    {
        bool afk;
        if (parts.Length == 1)
        {
            afk = !_playerState.IsAfk;
        }
        else if (parts.Length == 2 && parts[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            afk = true;
        }
        else if (parts.Length == 2 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            afk = false;
        }
        else
        {
            Reply("Формат: /afk [on|off]");
            return;
        }

        _playerState.SetAfk(afk);
        SendRobotInfo();
        Reply(afk ? "AFK: робот в капсуле." : "AFK выключен: обычный скин.");
    }

    private void ApplySkinCommand(string[] parts)
    {
        if (parts.Length == 2 && parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Reply("Скины: " + string.Join(", ", DummyRobotAssets.SkinNames));
            return;
        }

        string skin;
        if (parts.Length == 1 ||
            (parts.Length == 2 && parts[1].Equals("next", StringComparison.OrdinalIgnoreCase)))
        {
            skin = DummyRobotAssets.NextSkin(_playerState.SkinPath);
        }
        else if (parts.Length == 2 && DummyRobotAssets.TryResolveSkin(parts[1], out string selected))
        {
            skin = selected;
        }
        else
        {
            Reply("Формат: /skin [имя|next|list]. Скины: " + string.Join(", ", DummyRobotAssets.SkinNames));
            return;
        }

        _playerState.SetSkin(skin);
        _playerState.SetAfk(false);
        SendRobotInfo();
        Reply($"Скин: {DummyRobotAssets.SkinNameFor(skin)}");
    }

    private void ApplyTailCommand(string[] parts)
    {
        if (parts.Length == 2 && parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Reply("Хвосты: " + string.Join(", ", DummyRobotAssets.TailNames));
            return;
        }

        string tail;
        if (parts.Length == 1 ||
            (parts.Length == 2 && parts[1].Equals("next", StringComparison.OrdinalIgnoreCase)))
        {
            tail = DummyRobotAssets.NextTail(_playerState.TailPath);
        }
        else if (parts.Length == 2 && DummyRobotAssets.TryResolveTail(parts[1], out string selected))
        {
            tail = selected;
        }
        else
        {
            Reply("Формат: /tail [имя|next|list]. Хвосты: " + string.Join(", ", DummyRobotAssets.TailNames));
            return;
        }

        _playerState.SetTail(tail);
        SendRobotInfo();
        Reply($"Хвост: {DummyRobotAssets.TailNameFor(tail)}");
    }

    /// <summary>Переотправляет внешность робота игрока: скин и хвост из состояния.</summary>
    private void SendRobotInfo()
    {
        _sendPacket(new ServerPacket(new RobotInfoPacket(
            _playerBotId,
            DummyPlayerIdentity.PlayerId,
            DummyPlayerIdentity.ClanId,
            _playerState.VisualSkinPath,
            _playerState.TailPath,
            string.Empty)));
    }

    private void SpawnStaticBots()
    {
        int lifecycleVersion = _getLifecycleVersion();
        if (_staticBotsStartedLifecycleVersion == lifecycleVersion)
        {
            Reply("Неподвижные роботы уже созданы в этой сессии.");
            return;
        }

        _operations.Run(
            "dummy_command_static_bots",
            cancellationToken => DummyBotRunner.RunStaticBotsAround(
                lifecycleVersion,
                100,
                _playerState.X,
                _playerState.Y,
                _clock,
                _sendPacket,
                _loopAlive,
                cancellationToken));
        _staticBotsStartedLifecycleVersion = lifecycleVersion;
        Reply("Созданы 100 неподвижных роботов вокруг игрока.");
    }

    private void StartCircularBots()
    {
        int lifecycleVersion = _getLifecycleVersion();
        if (_circularBotsStartedLifecycleVersion == lifecycleVersion)
        {
            Reply("Круговые роботы уже созданы в этой сессии.");
            return;
        }

        _operations.Run(
            "dummy_command_circle_bots",
            cancellationToken => DummyBotRunner.RunCircularBotsAround(
                lifecycleVersion,
                _playerState.X,
                _playerState.Y,
                _clock,
                _sendPacket,
                _loopAlive,
                cancellationToken));
        _circularBotsStartedLifecycleVersion = lifecycleVersion;
        Reply("Созданы 100 роботов, движущихся по кругу вокруг игрока.");
    }

    private void PlayVisualEffect(string[] parts)
    {
        if (parts.Length == 2 && parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Reply("Визуальные эффекты: " + string.Join(", ", Enum.GetNames(typeof(VisualEffect))));
            Reply("BombTick/Boom: необязательный тип 0 Plasma, 1 Proton, 2 Discharge.");
            return;
        }

        if (parts.Length is < 2 or > 3 || !Enum.TryParse(parts[1], true, out VisualEffect effect) ||
            !Enum.IsDefined(typeof(VisualEffect), effect))
        {
            Reply("Формат: /visual <тип эффекта> [тип бомбы] или /visual list.");
            return;
        }

        int bombType = 0;
        if (parts.Length == 3)
        {
            if (effect is not (VisualEffect.BombTick or VisualEffect.Boom) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out bombType) ||
                bombType is < 0 or > 2)
            {
                Reply("Тип 0–2 доступен только для BombTick и Boom.");
                return;
            }
        }

        StringPairPacket[] parameters = effect is VisualEffect.BombTick or VisualEffect.Boom
            ? new[] { new StringPairPacket("bombType", bombType.ToString(CultureInfo.InvariantCulture)) }
            : Array.Empty<StringPairPacket>();

        ushort x = _playerState.X;
        ushort y = _playerState.Y;
        if (effect is VisualEffect.Pull or VisualEffect.Push or VisualEffect.GunShot)
        {
            x = x == ushort.MaxValue ? (ushort)(x - 1) : (ushort)(x + 1);
        }

        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new VisualPacket(effect, _playerBotId, x, y, parameters),
        })));
        Reply($"пакет визуального эффекта {effect} отправлен в локальный обработчик.");
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

        _playerState.SetPosition(x, y);
        _worldState.QueueChunksAround(x, y, _sendPacket);
        _sendPacket(new ServerPacket(new TeleportPacket(x, y)));
        Reply($"Телепорт в ({x}, {y}).");
    }

    private void ToggleUnloadedView()
    {
        _unloadedView = !_unloadedView;
        UnityEngine.Shader.SetGlobalFloat("_TerrainForceUnloaded", _unloadedView ? 1f : 0f);
        Reply(_unloadedView
            ? "Вид Unloaded включён."
            : "Вид Unloaded выключен.");
    }

    private void ApplySetCommand(string[] parts)
    {
        CellType placed = DefaultPlacedCell;
        if (parts.Length >= 2 && !Enum.TryParse(parts[1], ignoreCase: true, out placed))
        {
            Reply($"Неизвестный тип клетки «{parts[1]}».");
            return;
        }

        (int offsetX, int offsetY) = _playerState.Direction switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };

        int targetX = _playerState.X + offsetX;
        int targetY = _playerState.Y + offsetY;
        if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue)
        {
            Reply("Перед роботом край мира.");
            return;
        }

        var cellX = (ushort)targetX;
        var cellY = (ushort)targetY;
        _worldState.SetCell(cellX, cellY, placed);

        // Тот же пакет, которым отвечает постройка: клиент не должен различать,
        // откуда клетка изменилась — командой или обычным действием.
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new MapRegionPacket(cellX, cellY, 0, 0, new[] { placed }),
        })));

        Reply($"{placed} поставлен в ({cellX}, {cellY}).");
    }

    private void ApplyFillCommand(string[] parts)
    {
        CellType fillType = DefaultFillCell;
        if (parts.Length >= 2 && !Enum.TryParse(parts[1], ignoreCase: true, out fillType))
        {
            Reply($"Неизвестный тип клетки «{parts[1]}».");
            return;
        }

        WorldLayer<CellType>? layer = _worldState.Layer;
        if (layer == null)
        {
            Reply("Мир ещё не загружен.");
            return;
        }

        int worldWidth = Math.Min(layer.WidthChunks * layer.ChunkSize, ushort.MaxValue + 1);
        int worldHeight = Math.Min(layer.HeightChunks * layer.ChunkSize, ushort.MaxValue + 1);
        if (worldWidth < FillWidth || worldHeight < FillHeight)
        {
            Reply($"Мир меньше требуемой области {FillWidth}x{FillHeight}.");
            return;
        }

        int maxX = worldWidth - FillWidth;
        int maxY = worldHeight - FillHeight;
        int startX = Math.Clamp(_playerState.X - (FillWidth / 2), 0, maxX);
        int startY = Math.Clamp(_playerState.Y - (FillHeight / 2), 0, maxY);

        var payload = new CellType[FillWidth * FillHeight];
        Array.Fill(payload, fillType);
        layer.SetRegion(startX, startY, FillWidth, FillHeight, payload);

        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new MapRegionPacket(
                (ushort)startX,
                (ushort)startY,
                (byte)(FillWidth - 1),
                (byte)(FillHeight - 1),
                payload),
        })));

        Reply($"Область {FillWidth}x{FillHeight} заполнена клеткой {fillType} от ({startX}, {startY}).");
    }

    private static bool TryParseCoordinate(string text, out ushort value) =>
        ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void Reply(string text)
    {
        long now = DummyClockTime.UnixMilliseconds(_clock);
        var message = new ChatMessagePacket(
            now,
            now,
            0,
            0,
            s_serverColor,
            "Сервер",
            s_serverColor,
            text);
        _sendPacket(new ServerPacket(new ChatMessageListPacket(
            ChatProtocolLimits.GlobalChannelTag,
            new[] { message })));
    }
}
