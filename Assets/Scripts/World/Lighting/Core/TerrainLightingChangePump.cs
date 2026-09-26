#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces.WorldLighting;

namespace Kern.World.Lighting;

/// <summary>Moves committed Terrain changes into durable Lighting-owned state before acknowledgement.</summary>
internal sealed class TerrainLightingChangePump
{
    private ulong _worldGeneration;
    private ulong _acknowledgedSequence;
    private ulong _stagedThroughSequence;
    private readonly List<ulong> _stagedSequences = new();

    public void Stage(
        ITerrainLightingExchange exchange,
        ulong worldGeneration,
        Action<TerrainLightingChange> transfer)
    {
        if (exchange == null)
        {
            throw new ArgumentNullException(nameof(exchange));
        }

        if (transfer == null)
        {
            throw new ArgumentNullException(nameof(transfer));
        }
        if (worldGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldGeneration));
        }

        if (_worldGeneration != worldGeneration)
        {
            _worldGeneration = worldGeneration;
            _acknowledgedSequence = 0;
            _stagedThroughSequence = 0;
            _stagedSequences.Clear();
        }

        while (exchange.TryReadNextTerrainChange(
            worldGeneration,
            _stagedThroughSequence,
            out TerrainLightingChange change))
        {
            if (change.Sequence != _stagedThroughSequence + 1 &&
                change.Kind != TerrainLightingChangeKind.FullReset)
            {
                throw new InvalidOperationException(
                    $"Terrain lighting change sequence skipped from {_stagedThroughSequence} to {change.Sequence}.");
            }

            if (change.Kind == TerrainLightingChangeKind.FullReset)
            {
                _stagedSequences.Clear();
            }

            transfer(change);
            _stagedThroughSequence = change.Sequence;
            _stagedSequences.Add(change.Sequence);
        }
    }

    public void AcknowledgeStaged(ITerrainLightingExchange exchange)
    {
        if (exchange == null)
        {
            throw new ArgumentNullException(nameof(exchange));
        }

        if (_stagedThroughSequence <= _acknowledgedSequence)
        {
            return;
        }

        for (int index = 0; index < _stagedSequences.Count; index++)
        {
            ulong sequence = _stagedSequences[index];
            exchange.AcknowledgeTerrainChanges(_worldGeneration, sequence);
            _acknowledgedSequence = sequence;
        }

        _stagedSequences.Clear();
    }
}
