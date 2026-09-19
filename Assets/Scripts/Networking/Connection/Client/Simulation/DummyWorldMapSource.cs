#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;

namespace MinesServer.Networking.Connection.Client;

internal interface IDummyWorldMapSource
{
    UniTask<string> GetMapFileAsync(string worldCodeName, CancellationToken cancellationToken);
}

// Одна подготовка карты на мир за жизнь приложения: прогрев на старте, кнопка
// «Играть» и инициализация мира ждут один и тот же результат, а не распаковывают
// каждый своё. Неудача не запоминается — следующий запрос начинает заново.
// Вызывается с главного потока.
public sealed class DummyWorldMapSource : IDummyWorldMapSource
{
    // UniTaskCompletionSource, а не Preserve(): Preserve запоминает только
    // завершённый результат, а до завершения второй ожидающий подписывается на
    // исходную задачу повторно и получает «Already continuation registered».
    // Прогрев и инициализация мира ждут одновременно — ровно этот случай.
    private readonly Dictionary<string, UniTaskCompletionSource<string>> _preparations =
        new(StringComparer.Ordinal);

    private readonly IAsyncOperationSupervisor _operations;

    public DummyWorldMapSource(IAsyncOperationSupervisor operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public UniTask<string> GetMapFileAsync(string worldCodeName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(worldCodeName))
        {
            throw new ArgumentException("World code name is required.", nameof(worldCodeName));
        }

        if (!_preparations.TryGetValue(worldCodeName, out UniTaskCompletionSource<string>? preparation))
        {
            preparation = new UniTaskCompletionSource<string>();
            _preparations[worldCodeName] = preparation;
            UniTaskCompletionSource<string> started = preparation;

            // Распаковка живёт под надзором супервизора: он переживает отмену
            // отдельного ожидающего и останавливается вместе с приложением.
            _operations.Run("dummy_world_map_prepare", _ => PrepareAsync(worldCodeName, started));
        }

        // Отмена одного ожидающего не отменяет общую подготовку: её ждут другие.
        return preparation.Task.AttachExternalCancellation(cancellationToken);
    }

    private async UniTask PrepareAsync(string worldCodeName, UniTaskCompletionSource<string> preparation)
    {
        try
        {
            string mapPath = await DummyWorldMapArchive.ResolveMapFileAsync(worldCodeName, CancellationToken.None);
            preparation.TrySetResult(mapPath);
        }
        catch (Exception exception)
        {
            if (_preparations.TryGetValue(worldCodeName, out UniTaskCompletionSource<string>? current) &&
                ReferenceEquals(current, preparation))
            {
                _preparations.Remove(worldCodeName);
            }

            preparation.TrySetException(exception);
        }
    }
}
