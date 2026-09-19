#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MinesServer.Networking.Connection.Client;

// Время и случайность офлайн-сервера.
//
// Все задержки, отметки времени и случайные числа dummy-сервера идут через
// эти часы. В игре это реальное время; в тестах — виртуальное, которое
// двигает сам тест, поэтому сценарий повторяется до пакета при любом темпе
// кадров и любой загрузке машины.
public interface IDummyClock
{
    long UtcNowTicks { get; }

    Random Random { get; }

    UniTask Delay(int milliseconds, CancellationToken cancellationToken = default);

    UniTask Yield(CancellationToken cancellationToken = default);
}

public static class DummyClockTime
{
    public static long UnixMilliseconds(IDummyClock clock) =>
        new DateTimeOffset(clock.UtcNowTicks, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public static long UnixSeconds(IDummyClock clock) =>
        new DateTimeOffset(clock.UtcNowTicks, TimeSpan.Zero).ToUnixTimeSeconds();
}

public sealed class RealtimeDummyClock : IDummyClock
{
    public long UtcNowTicks => DateTimeOffset.UtcNow.Ticks;

    public Random Random { get; } = new();

    public UniTask Delay(int milliseconds, CancellationToken cancellationToken = default) =>
        UniTask.Delay(milliseconds, cancellationToken: cancellationToken);

    public UniTask Yield(CancellationToken cancellationToken = default) =>
        UniTask.Yield(cancellationToken);
}

// Время стоит, пока тест не вызовет Advance. Ожидания завершаются по сроку, а
// при равном сроке — в порядке постановки; продолжения выполняются синхронно
// внутри Advance. Yield — ожидание с нулевым сроком: оно завершается на
// ближайшем Advance, а не на следующем кадре.
public sealed class VirtualDummyClock : IDummyClock
{
    private static readonly DateTimeOffset _Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly SortedSet<Waiter> _waiters = new(WaiterOrder.Instance);
    private long _nowMilliseconds;
    private long _sequence;

    public VirtualDummyClock(int seed)
    {
        Random = new Random(seed);
    }

    public long NowMilliseconds => _nowMilliseconds;

    public int PendingCount => _waiters.Count;

    public long UtcNowTicks => _Epoch.AddMilliseconds(_nowMilliseconds).Ticks;

    public Random Random { get; }

    public UniTask Delay(int milliseconds, CancellationToken cancellationToken = default)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, "Delay must not be negative.");
        }

        return Schedule(_nowMilliseconds + milliseconds, cancellationToken);
    }

    public UniTask Yield(CancellationToken cancellationToken = default) =>
        Schedule(_nowMilliseconds, cancellationToken);

    // Двигает время на milliseconds, по дороге завершая все ожидания, срок
    // которых наступил, включая поставленные продолжениями этих ожиданий.
    public void Advance(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, "Time only moves forward.");
        }

        long target = _nowMilliseconds + milliseconds;
        while (_waiters.Count > 0 && _waiters.Min!.DueMilliseconds <= target)
        {
            Waiter next = _waiters.Min;
            _waiters.Remove(next);
            _nowMilliseconds = next.DueMilliseconds;
            next.Registration.Dispose();
            next.Completion.TrySetResult();
        }

        _nowMilliseconds = target;
    }

    private UniTask Schedule(long dueMilliseconds, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return UniTask.FromCanceled(cancellationToken);
        }

        var waiter = new Waiter(dueMilliseconds, _sequence++);
        _waiters.Add(waiter);
        if (cancellationToken.CanBeCanceled)
        {
            waiter.Registration = cancellationToken.Register(() =>
            {
                if (_waiters.Remove(waiter))
                {
                    waiter.Completion.TrySetCanceled(cancellationToken);
                }
            });
        }

        return waiter.Completion.Task;
    }

    private sealed class Waiter(long dueMilliseconds, long sequence)
    {
        public long DueMilliseconds { get; } = dueMilliseconds;

        public long Sequence { get; } = sequence;

        public UniTaskCompletionSource Completion { get; } = new();

        public CancellationTokenRegistration Registration { get; set; }
    }

    private sealed class WaiterOrder : IComparer<Waiter>
    {
        public static readonly WaiterOrder Instance = new();

        public int Compare(Waiter? left, Waiter? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            int due = left!.DueMilliseconds.CompareTo(right!.DueMilliseconds);
            return due != 0 ? due : left.Sequence.CompareTo(right.Sequence);
        }
    }
}
