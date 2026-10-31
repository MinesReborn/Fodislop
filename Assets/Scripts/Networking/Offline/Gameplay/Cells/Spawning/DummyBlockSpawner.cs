#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Offline;

/// <summary>
/// Раз в 50 мс меняет случайную твёрдую клетку в радиусе видимости игрока на
/// случайный блок — тем же пакетом, каким сервер сообщает о копании. Нагружает
/// путь правки мира: террейн, свет, AO и карту, не засыпая пустоту.
/// </summary>
internal sealed class DummyBlockSpawner
{
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IDummyClock _clock;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Func<int, bool> _loopAlive;
    private readonly Func<CellType, bool> _hasCellTexture;
    internal DummyBlockSpawner(
    IAsyncOperationSupervisor operations,
    IDummyClock clock,
    DummyWorldSimulationState worldState,
    DummyPlayerSimulationState playerState,
    Action<ServerPacket> sendPacket,
    Func<int, bool> loopAlive,
    Func<CellType, bool> hasCellTexture)
    {
        _operations = operations;
        _clock = clock;
        _worldState = worldState;
        _playerState = playerState;
        _sendPacket = sendPacket;
        _loopAlive = loopAlive;
        _hasCellTexture = hasCellTexture;
    }

    private const int IntervalMilliseconds = 50;

    // Полувысота кадра при обычном зуме: клетка в этом круге видна на экране.
    private const int RadiusCells = (int)GameplayCameraDefaults.ReferenceOrthographicSize;

    // Выключен по умолчанию: мир, меняющийся 20 раз в секунду вокруг игрока,
    // загрязняет любое наблюдение за краями блоков, светом и AO. Включается
    // отладочной кнопкой меню паузы (тег toggle_block_spawner).
    public bool Enabled { get; set; }

    public void Start(int lifecycleVersion, DummyWorldDescriptor world)
    {
        CellType[] blocks = CollectBlocks(world.CellConfigurations, _hasCellTexture);
        if (blocks.Length == 0)
        {
            return;
        }

        _operations.Run(
            "dummy_block_spawner",
            cancellationToken => RunAsync(lifecycleVersion, world.Width, world.Height, blocks, cancellationToken));
    }

    // Блок — объявленный тип с конфигурацией, сквозь который нельзя пройти и
    // текстуру которого заглушка может отдать. Тип без текстуры клиент
    // запрашивал впустую: ошибка загрузки со стеком и цвет карты вместо блока.
    internal static CellType[] CollectBlocks(
        CellConfigurationPacket[] configurations,
        Func<CellType, bool> _hasCellTexture)
    {
        var blocks = new List<CellType>();
        for (int index = 0; index < configurations.Length && index <= byte.MaxValue; index++)
        {
            var type = (CellType)index;
            if (type != CellType.Unloaded &&
                Enum.IsDefined(typeof(CellType), type) &&
                (configurations[index].Properties & CellConfigProperties.Passable) == 0 &&
                _hasCellTexture(type))
            {
                blocks.Add(type);
            }
        }

        return blocks.ToArray();
    }

    private async UniTask RunAsync(
        int lifecycleVersion,
        int worldWidth,
        int worldHeight,
        CellType[] blocks,
        CancellationToken cancellationToken)
    {
        while (_loopAlive(lifecycleVersion))
        {
            await _clock.Delay(IntervalMilliseconds, cancellationToken);
            if (!_loopAlive(lifecycleVersion))
            {
                break;
            }

            if (Enabled)
            {
                TrySpawn(worldWidth, worldHeight, blocks);
            }
        }
    }

    private void TrySpawn(int worldWidth, int worldHeight, CellType[] blocks)
    {
        int offsetX;
        int offsetY;
        do
        {
            offsetX = _clock.Random.Next(-RadiusCells, RadiusCells + 1);
            offsetY = _clock.Random.Next(-RadiusCells, RadiusCells + 1);
        }
        while ((offsetX * offsetX) + (offsetY * offsetY) > RadiusCells * RadiusCells);

        // Клетка робота остаётся свободной: блок в ней замуровал бы игрока.
        if (offsetX == 0 && offsetY == 0)
        {
            return;
        }

        int x = _playerState.X + offsetX;
        int y = _playerState.Y + offsetY;
        if (x < 0 || y < 0 || x >= worldWidth || y >= worldHeight ||
            !_worldState.TryGetCell((ushort)x, (ushort)y, out CellType current))
        {
            return;
        }

        // Меняется только твёрдая клетка. Блок в пустоте за минуту засыпал
        // весь круг вокруг робота: тоннели закрывались, свет снаружи не
        // доходил, и всё дальше лампы робота становилось чёрным.
        if (_worldState.GetCellConfig(current) is not { } currentConfig ||
            (currentConfig.Properties & CellConfigProperties.Passable) != 0)
        {
            return;
        }

        CellType block = blocks[_clock.Random.Next(blocks.Length)];
        if (block == current)
        {
            return;
        }

        _worldState.SetCell((ushort)x, (ushort)y, block);
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new MapRegionPacket((ushort)x, (ushort)y, 0, 0, new[] { block }),
        })));
    }
}
