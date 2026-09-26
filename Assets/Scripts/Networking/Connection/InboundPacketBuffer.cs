#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking.Diagnostics;
using MinesServer.Networking.Server.Packets;

namespace Kern.Networking.Connection;

/// <summary>Owns ordered packet admission, teardown generations, and queue accounting.</summary>
internal sealed class InboundPacketBuffer
{
    private readonly ConcurrentQueue<ServerPacket> _queue = new();
    private readonly object _admissionGate = new();
    private readonly PacketAdmissionBudget _admission = new(
        ProjectRuntimeContracts.RuntimeLimits.MaximumQueuedPacketCount,
        ProjectRuntimeContracts.RuntimeLimits.MaximumQueuedPacketBytes);

    private int _receiveGeneration;
    private int _mainThreadId;
    private bool _tearingDown;

    public bool IsTearingDown => _tearingDown;

    public bool IsEmpty => _queue.IsEmpty;

    public int Count => _queue.Count;

    public long PacketBytes => _admission.PacketBytes;

    public void CaptureMainThread() => _mainThreadId = Environment.CurrentManagedThreadId;

    public void BeginTeardown() => _tearingDown = true;

    public void EndTeardown() => _tearingDown = false;

    public bool TryTake(out ServerPacket packet)
    {
        if (!_queue.TryDequeue(out ServerPacket queuedPacket))
        {
            packet = default;
            return false;
        }

        packet = queuedPacket;
        lock (_admissionGate)
        {
            _admission.Release(packet.Size);
            Monitor.PulseAll(_admissionGate);
        }

        return true;
    }

    public int Clear()
    {
        int discardedCount = 0;
        lock (_admissionGate)
        {
            // Новое поколение приёма: поток чтения прежнего соединения,
            // ждущий места в очереди, проснётся и уйдёт, ничего не положив.
            _receiveGeneration++;
            while (_queue.TryDequeue(out _))
            {
                discardedCount++;
            }

            _admission.Clear();
            Monitor.PulseAll(_admissionGate);
        }

        return discardedCount;
    }

    /// <summary>Queues packets under count and byte limits while preserving receive order.</summary>
    ///
    /// Overflow applies backpressure to the socket reader instead of dropping
    /// authoritative updates. The main thread cannot wait on its own queue, so
    /// it admits an overflow packet and records that event in telemetry.
    public void Enqueue(ServerPacket packet)
    {
        int packetBytes = Math.Max(1, packet.Size);
        long waitStart = 0;
        lock (_admissionGate)
        {
            int generation = _receiveGeneration;
            while (true)
            {
                // OnReceived may have observed the old teardown state just
                // before Disconnect acquired the gate. Re-check while the
                // queue and its counters are protected, so no stale packet
                // can be inserted after Clear.
                if (_tearingDown || generation != _receiveGeneration)
                {
                    return;
                }

                if (_admission.TryReserve(packetBytes))
                {
                    break;
                }

                if (Environment.CurrentManagedThreadId == _mainThreadId)
                {
                    _admission.Reserve(packetBytes);
                    PacketTelemetry.RecordAdmissionOverflow();
                    break;
                }

                if (waitStart == 0)
                {
                    waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                }

                // Таймаут — страховка от пропущенного сигнала, а не опрос:
                // место освобождает TryTake с PulseAll.
                Monitor.Wait(_admissionGate, 100);
            }

            _queue.Enqueue(packet);
        }

        if (waitStart != 0)
        {
            PacketTelemetry.RecordBackpressure(
                (System.Diagnostics.Stopwatch.GetTimestamp() - waitStart) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency);
        }
    }
}
