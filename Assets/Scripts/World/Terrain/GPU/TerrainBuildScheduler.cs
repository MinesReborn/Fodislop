#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kern.World.Terrain;

public readonly record struct TerrainBuildCompletion<TRequest, TResult>(
    TRequest Request,
    TResult? Result,
    Exception? Failure,
    bool WasCanceled);

/// <summary>
/// Одна фоновая сборка за раз, без ожидания на главном потоке.
/// </summary>
///
/// Очереди нет намеренно. Сборка террейна — приращение к состоянию, которым
/// владеет рабочий поток, пока она идёт, поэтому следующая сборка не может
/// стартовать раньше публикации предыдущей. Следующий запрос составляется
/// в момент старта по самой свежей цели камеры: движение во время сборки не
/// отменяет почти готовый результат, а просто становится следующим шагом.
public sealed class TerrainBuildScheduler<TRequest, TResult> : IDisposable
    where TRequest : class
{
    private sealed class ActiveWork
    {
        public TRequest Request = null!;
        public CancellationTokenSource Cancellation = null!;
        public Task<TResult> Task = null!;
    }

    private readonly Func<TRequest, CancellationToken, TResult> _build;
    private ActiveWork? _active;
    private bool _disposed;

    public TerrainBuildScheduler(Func<TRequest, CancellationToken, TResult> build)
    {
        _build = build ?? throw new ArgumentNullException(nameof(build));
    }

    public bool IsBusy => _active != null;

    public TRequest? ActiveRequest => _active?.Request;

    public Task CompletionTask => _active?.Task ?? Task.CompletedTask;

    public void Start(TRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ThrowIfDisposed();
        if (_active != null)
        {
            throw new InvalidOperationException(
                "A terrain build is already running; the next one starts after its publication.");
        }

        var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        _active = new ActiveWork
        {
            Request = request,
            Cancellation = cancellation,
            Task = Task.Run(() => _build(request, token), token),
        };
    }

    public bool TryTakeCompleted(out TerrainBuildCompletion<TRequest, TResult> completion)
    {
        if (_active == null || !_active.Task.IsCompleted)
        {
            completion = default;
            return false;
        }

        ActiveWork completed = _active;
        _active = null;
        completion = ReadCompletion(completed);
        return true;
    }

    /// <summary>Кооперативная отмена: результат всё равно забирается через TryTakeCompleted.</summary>
    public void Cancel() => _active?.Cancellation.Cancel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_active == null)
        {
            return;
        }

        ActiveWork active = _active;
        _active = null;
        active.Cancellation.Cancel();

        // Главный поток не ждёт рабочий. Исход задачи забирается здесь, чтобы
        // исключение брошенной сборки не всплыло как ненаблюдённое.
        _ = active.Task.ContinueWith(
            static (task, state) =>
            {
                _ = task.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            active.Cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static TerrainBuildCompletion<TRequest, TResult> ReadCompletion(ActiveWork work)
    {
        try
        {
            TResult result = work.Task.GetAwaiter().GetResult();
            return new TerrainBuildCompletion<TRequest, TResult>(work.Request, result, null, false);
        }
        catch (OperationCanceledException)
        {
            return new TerrainBuildCompletion<TRequest, TResult>(work.Request, default, null, true);
        }
        catch (AggregateException exception) when (IsCancellation(exception))
        {
            // Parallel.For оборачивает отмену тела в AggregateException.
            return new TerrainBuildCompletion<TRequest, TResult>(work.Request, default, null, true);
        }
        catch (Exception exception)
        {
            return new TerrainBuildCompletion<TRequest, TResult>(work.Request, default, exception, false);
        }
        finally
        {
            work.Cancellation.Dispose();
        }
    }

    private static bool IsCancellation(AggregateException exception)
    {
        foreach (Exception inner in exception.Flatten().InnerExceptions)
        {
            if (inner is not OperationCanceledException)
            {
                return false;
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerrainBuildScheduler<TRequest, TResult>));
        }
    }
}
