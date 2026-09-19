#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.Networking.Diagnostics;

public static class PacketTelemetry
{
    public const int HistoryCapacity = 200;

    private static readonly Dictionary<string, PacketStat> _Incoming = [];
    private static readonly Dictionary<string, PacketStat> _Outgoing = [];
    private static readonly PacketEvent[] _History = new PacketEvent[HistoryCapacity];
    private static int _historyCursor;
    private static int _historyCount;
    private static double _windowStart;
    private static long _windowIncoming;
    private static long _windowOutgoing;

    public static bool Enabled { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        Enabled = false;
        Reset();
    }

    public static long TotalIncoming { get; private set; }

    public static long TotalOutgoing { get; private set; }

    public static long TotalUnhandled { get; private set; }

    public static long BatchCount { get; private set; }

    public static long BatchedPacketCount { get; private set; }

    public static long CompressedCount { get; private set; }

    public static double IncomingPerSecond { get; private set; }

    public static double OutgoingPerSecond { get; private set; }

    public static int QueueDepth { get; private set; }

    public static int PeakQueueDepth { get; private set; }

    public static long BudgetStopCount { get; private set; }

    public static long BatchCapStopCount { get; private set; }

    public static void RecordIncoming(Type packetType, bool handled, double timeSeconds)
    {
        if (!Enabled)
        {
            return;
        }

        TotalIncoming++;
        _windowIncoming++;
        if (!handled)
        {
            TotalUnhandled++;
        }

        Accumulate(_Incoming, packetType.Name, handled, timeSeconds);
        PushHistory(new PacketEvent(packetType.Name, Incoming: true, handled, timeSeconds));
    }

    // Время обработчика одного пакета. Бюджет разбора очереди проверяется
    // только между пакетами, поэтому пик кадра — это самый тяжёлый обработчик.
    public static void RecordHandlerTime(Type packetType, double milliseconds)
    {
        if (!Enabled)
        {
            return;
        }

        string name = packetType.Name;
        _Incoming.TryGetValue(name, out PacketStat stat);
        _Incoming[name] = stat with
        {
            Name = name,
            HandlerCount = stat.HandlerCount + 1,
            HandlerTotalMilliseconds = stat.HandlerTotalMilliseconds + milliseconds,
            HandlerPeakMilliseconds = Math.Max(stat.HandlerPeakMilliseconds, milliseconds),
        };

        if (milliseconds > SlowestHandlerMilliseconds)
        {
            SlowestHandlerMilliseconds = milliseconds;
            SlowestHandlerName = name;
        }
    }

    public static double SlowestHandlerMilliseconds { get; private set; }

    public static string SlowestHandlerName { get; private set; } = string.Empty;

    public static void RecordOutgoing(Type packetType, double timeSeconds)
    {
        if (!Enabled)
        {
            return;
        }

        TotalOutgoing++;
        _windowOutgoing++;
        Accumulate(_Outgoing, packetType.Name, handled: true, timeSeconds);
        PushHistory(new PacketEvent(packetType.Name, Incoming: false, Handled: true, timeSeconds));
    }

    public static void RecordQueueState(int depth, bool stoppedByBudget, bool stoppedByCap)
    {
        if (!Enabled)
        {
            return;
        }

        QueueDepth = depth;
        PeakQueueDepth = Math.Max(PeakQueueDepth, depth);
        if (stoppedByBudget)
        {
            BudgetStopCount++;
        }

        if (stoppedByCap)
        {
            BatchCapStopCount++;
        }
    }

    public static void Poll(double nowSeconds)
    {
        if (_windowStart <= 0d)
        {
            _windowStart = nowSeconds;
            return;
        }

        double elapsed = nowSeconds - _windowStart;
        if (elapsed < 1d)
        {
            return;
        }

        IncomingPerSecond = _windowIncoming / elapsed;
        OutgoingPerSecond = _windowOutgoing / elapsed;
        _windowIncoming = 0;
        _windowOutgoing = 0;
        _windowStart = nowSeconds;
    }

    public static void RecordBatch(int packetCount)
    {
        if (!Enabled)
        {
            return;
        }

        BatchCount++;
        BatchedPacketCount += packetCount;
    }

    public static void RecordCompressed()
    {
        if (Enabled)
        {
            CompressedCount++;
        }
    }

    public static void Reset()
    {
        _Incoming.Clear();
        _Outgoing.Clear();
        Array.Clear(_History, 0, _History.Length);
        _historyCursor = 0;
        _historyCount = 0;
        TotalIncoming = 0;
        TotalOutgoing = 0;
        TotalUnhandled = 0;
        BatchCount = 0;
        BatchedPacketCount = 0;
        CompressedCount = 0;
        IncomingPerSecond = 0d;
        OutgoingPerSecond = 0d;
        QueueDepth = 0;
        PeakQueueDepth = 0;
        BudgetStopCount = 0;
        BatchCapStopCount = 0;
        SlowestHandlerMilliseconds = 0d;
        SlowestHandlerName = string.Empty;
        _windowStart = 0d;
        _windowIncoming = 0;
        _windowOutgoing = 0;
    }

    public static void CollectIncoming(List<PacketStat> destination) => Copy(_Incoming, destination);

    public static void CollectOutgoing(List<PacketStat> destination) => Copy(_Outgoing, destination);

    public static void CollectHistory(List<PacketEvent> destination)
    {
        destination.Clear();
        for (int i = 0; i < _historyCount; i++)
        {
            int index = (_historyCursor - 1 - i + HistoryCapacity * 2) % HistoryCapacity;
            destination.Add(_History[index]);
        }
    }

    private static void Accumulate(
        Dictionary<string, PacketStat> target,
        string name,
        bool handled,
        double timeSeconds)
    {
        target.TryGetValue(name, out PacketStat stat);
        target[name] = stat with
        {
            Name = name,
            Count = stat.Count + 1,
            UnhandledCount = handled ? stat.UnhandledCount : stat.UnhandledCount + 1,
            LastSeenSeconds = timeSeconds,
        };
    }

    private static void PushHistory(PacketEvent entry)
    {
        _History[_historyCursor] = entry;
        _historyCursor = (_historyCursor + 1) % HistoryCapacity;
        _historyCount = Math.Min(_historyCount + 1, HistoryCapacity);
    }

    private static void Copy(Dictionary<string, PacketStat> source, List<PacketStat> destination)
    {
        destination.Clear();
        foreach (PacketStat stat in source.Values)
        {
            destination.Add(stat);
        }
    }
}

public readonly record struct PacketStat(
    string Name,
    long Count,
    long UnhandledCount,
    double LastSeenSeconds,
    long HandlerCount = 0,
    double HandlerTotalMilliseconds = 0d,
    double HandlerPeakMilliseconds = 0d)
{
    public double HandlerAverageMilliseconds => HandlerCount > 0 ? HandlerTotalMilliseconds / HandlerCount : 0d;
}

public readonly record struct PacketEvent(
    string Name,
    bool Incoming,
    bool Handled,
    double TimeSeconds);
