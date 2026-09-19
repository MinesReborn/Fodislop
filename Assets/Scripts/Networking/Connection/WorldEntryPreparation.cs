#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Networking.Connection.Client;
using VContainer.Unity;

namespace Kern.Networking.Connection;

// Офлайн-сервер готовит карту мира до входа в него: распаковка ~300 МБ не должна
// съедать таймаут перехода сцены. Прогрев стартует вместе с приложением, вход в
// мир дожидается его; ошибка подготовки видна сразу, а не таймаутом через 30 с.
// Настоящему серверу готовить нечего.
public sealed class WorldEntryPreparation : IWorldEntryPreparation, IStartable
{
    private readonly IClientConfigManager _clientConfig;
    private readonly DummyConnection _dummyConnection;
    private readonly DummyWorldMapSource _worldMaps;
    private readonly IAsyncOperationSupervisor _operations;

    public WorldEntryPreparation(
        IClientConfigManager clientConfig,
        DummyConnection dummyConnection,
        DummyWorldMapSource worldMaps,
        IAsyncOperationSupervisor operations)
    {
        _clientConfig = clientConfig;
        _dummyConnection = dummyConnection;
        _worldMaps = worldMaps;
        _operations = operations;
    }

    public void Start()
    {
        if (!UsesDummyTransport())
        {
            return;
        }

        _operations.Run("dummy_world_map_prewarm", EnsureReadyAsync);
    }

    public UniTask EnsureReadyAsync(CancellationToken cancellationToken)
    {
        return UsesDummyTransport()
            ? _worldMaps.GetMapFileAsync(_dummyConnection.PrebakedWorldCodeName, cancellationToken).AsUniTask()
            : UniTask.CompletedTask;
    }

    private bool UsesDummyTransport()
    {
        _clientConfig.EnsureInitialized();
        ClientConfig? config = _clientConfig.Config;

        // Тот же выбор, что в ConnectionManager.CreateConnection: без конфига — заглушка.
        return config == null ||
            ConnectionTransportConfig.SelectTransport(config.Connection.UseDummyConnection) == ConnectionTransportKind.Dummy;
    }
}
