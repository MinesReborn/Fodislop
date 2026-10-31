#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Networking.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Contracts;
using Unity.Profiling;
using UnityEngine;
using VContainer;
using Kern.Diagnostics.Memory;
using Kern.Networking.Authentication;
using Kern.Networking.Offline;

namespace Kern.Networking.Connection;

internal sealed class InboundPacketDrain
{
    private readonly InboundPacketBuffer _inboundPackets;
    private readonly Action _onBatchStarted;
    private readonly Action _onBatchCompleted;
    private readonly Action<ServerPacket> _onPacketReceived;
    private readonly Action<string> _disconnect;

    internal InboundPacketDrain(InboundPacketBuffer inboundPackets,
        Action onBatchStarted, Action onBatchCompleted,
        Action<ServerPacket> onPacketReceived, Action<string> disconnect)
    {
        _inboundPackets = inboundPackets;
        _onBatchStarted = onBatchStarted;
        _onBatchCompleted = onBatchCompleted;
        _onPacketReceived = onPacketReceived;
        _disconnect = disconnect;
    }

        internal static readonly ProfilerMarker s_packetDrainMarker =
            new("Kern.Net.DrainPacketQueue");

        internal static readonly AllocationLedger.Entry s_allocationEntry =
            AllocationLedger.Register("Сеть — разбор очереди");

        // Бюджет на обработку входящих пакетов — доля времени КАДРА, а не стены часов.
        // Пропорция к deltaTime масштабирует пропускную способность с частотой кадров
        // (независимость от 30 vs 144 FPS) и ограничивает долю CPU, но при этом
        // всплеск пакетов (мировые текстуры при входе в мир) дренится за несколько
        // кадров, а не тянется секунду, как с накоплением 2% реального времени.
        internal const float PacketDrainBudgetFractionOfFrame = 0.33f;
        internal const float PacketDrainBudgetMaximumSeconds = 0.01f;

        internal void Drain()
        {
            using var marker = s_packetDrainMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(s_allocationEntry);
            float budgetSeconds = Mathf.Min(
                Time.unscaledDeltaTime * PacketDrainBudgetFractionOfFrame,
                PacketDrainBudgetMaximumSeconds);
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int processedCount = 0;
            bool stoppedByBudget = false;
            bool stoppedByCap = false;

            bool batchActive = _inboundPackets.Count > 1;
            if (batchActive)
            {
                _onBatchStarted?.Invoke();
            }

            try
            {
                while (processedCount < NetworkRuntimeLimits.MaximumPacketBatchPerFrame)
                {
                    // A handler cannot be preempted. Stop before dequeuing the next
                    // packet so one expensive handler is the only unavoidable overrun.
                    float elapsedMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    if (processedCount > 0 && elapsedMs >= budgetSeconds * 1000f)
                    {
                        stoppedByBudget = true;
                        break;
                    }

                    if (!_inboundPackets.TryTake(out ServerPacket packet))
                    {
                        break;
                    }

                    processedCount++;
                    try
                    {
                        _onPacketReceived?.Invoke(packet);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(
                            new InvalidOperationException(
                                "A server packet could not be processed. Disconnecting to avoid continuing with corrupted state.",
                                ex));
                        _disconnect("Client packet processing failed.");
                        break;
                    }
                }
            }
            finally
            {
                if (batchActive)
                {
                    _onBatchCompleted?.Invoke();
                }
            }

            if (processedCount >= NetworkRuntimeLimits.MaximumPacketBatchPerFrame &&
                !_inboundPackets.IsEmpty)
            {
                stoppedByCap = true;
            }

            // Остаток очереди и причина обрыва — единственный признак того, что
            // пакет уже пришёл, но до обработчика в этом кадре не добрался.
            PacketTelemetry.RecordQueueState(
                _inboundPackets.Count,
                _inboundPackets.PacketBytes,
                stoppedByBudget,
                stoppedByCap);
        }

}
