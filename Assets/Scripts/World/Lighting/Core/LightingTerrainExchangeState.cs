#nullable enable

using System;
using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;

namespace Kern.World.Lighting
{
    /// <summary>Owns Terrain/Lighting exchange sequencing and publication generations.</summary>
    internal sealed class LightingTerrainExchangeState
    {
        private readonly TerrainLightingFramePump _framePump = new();
        private readonly TerrainLightingChangePump _changePump = new();
        private ulong _requirementsPolicyRevision;
        private ulong _outputGeneration;
        private int _publishedTerrainPadding = -1;
        private int _publishedStablePadding = -1;

        public void PublishRequirements(
            ITerrainLightingExchange exchange,
            int terrainPadding,
            int stablePadding)
        {
            if (_requirementsPolicyRevision > 0 &&
                terrainPadding == _publishedTerrainPadding &&
                stablePadding == _publishedStablePadding)
            {
                return;
            }

            ulong revision = checked(_requirementsPolicyRevision + 1);
            var requirements = new LightingTerrainRequirements(
                revision,
                terrainPadding,
                stablePadding);
            exchange.PublishLightingRequirements(requirements);
            _requirementsPolicyRevision = revision;
            _publishedTerrainPadding = terrainPadding;
            _publishedStablePadding = stablePadding;
        }

        public void ProcessLatestFrame(
            ITerrainLightingExchange exchange,
            Func<TerrainLightingFrameSnapshot, bool> processFrame) =>
            _framePump.ProcessLatest(exchange, processFrame);

        public void StageTerrainChanges(
            ITerrainLightingExchange exchange,
            ulong worldGeneration,
            Action<TerrainLightingChange> applyChange) =>
            _changePump.Stage(exchange, worldGeneration, applyChange);

        public void AcknowledgeStagedChanges(ITerrainLightingExchange exchange) =>
            _changePump.AcknowledgeStaged(exchange);

        public void PublishOutput(
            ITerrainLightingExchange exchange,
            ulong worldGeneration,
            LightingOutputState state,
            RectInt worldRectCells)
        {
            ulong generation = checked(_outputGeneration + 1);
            var output = new LightingOutputSnapshot(
                generation,
                worldGeneration,
                state,
                worldRectCells);
            exchange.PublishLightingOutput(output);
            _outputGeneration = generation;
        }
    }
}
