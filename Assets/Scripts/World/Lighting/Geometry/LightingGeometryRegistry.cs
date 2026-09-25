#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

public sealed class LightingGeometryRegistry
{
    private readonly List<Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor> _contributors = [];
    private readonly RenderTargetIdentifier[] _fieldTargets = new RenderTargetIdentifier[2];
    private readonly RenderTargetIdentifier[] _ambientOcclusionTarget = new RenderTargetIdentifier[1];
    private ulong _registryRevision = 1;

    public bool HasContributors => _contributors.Count > 0;

    public ulong GeometryRevision
    {
        get
        {
            ulong revision = _registryRevision;
            foreach (Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor contributor in _contributors)
            {
                revision = RotateLeft(revision, 7) ^ contributor.LightingGeometryRevision;
            }

            return revision;
        }
    }

    public void Register(Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor contributor)
    {
        if (contributor == null)
        {
            throw new ArgumentNullException(nameof(contributor));
        }

        if (_contributors.Contains(contributor))
        {
            return;
        }

        _contributors.Add(contributor);
        _registryRevision++;
    }

    public void Unregister(Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor contributor)
    {
        if (contributor == null)
        {
            throw new ArgumentNullException(nameof(contributor));
        }

        if (_contributors.Remove(contributor))
        {
            _registryRevision++;
        }
    }

    public void RenderMaterialEmissionFields(
        CommandBuffer commandBuffer,
        RenderTexture materialField,
        RenderTexture emissionField,
        Vector4 worldRect,
        bool clearFields = true)
    {
        if (commandBuffer == null)
        {
            throw new ArgumentNullException(nameof(commandBuffer));
        }

        if (!materialField.IsCreated() || !emissionField.IsCreated())
        {
            throw new InvalidOperationException(
                "Lighting fields must be created before geometry contributors are rendered.");
        }

        if (_contributors.Count == 0)
        {
            throw new InvalidOperationException(
                "World lighting has no registered geometry contributors.");
        }

        // clearFields == false means the material-field pass already
        // bound these targets for the terrain draw right before this call.
        // Re-issuing SetRenderTarget would end that render pass and force a
        // tile-memory flush + store/load on TBDR GPUs (Apple Metal) for no
        // reason — one SetRenderTarget keeps terrain and contributors in a
        // single pass over the tiles. Only rebind when we must start a pass
        // with a clear.
        if (clearFields)
        {
            _fieldTargets[0] = new RenderTargetIdentifier(materialField);
            _fieldTargets[1] = new RenderTargetIdentifier(emissionField);
            commandBuffer.SetRenderTarget(
                _fieldTargets,
                new RenderTargetIdentifier(BuiltinRenderTextureType.None));
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);
        }

        Matrix4x4 projection = Matrix4x4.Ortho(
            worldRect.x,
            worldRect.x + worldRect.z,
            worldRect.y,
            worldRect.y + worldRect.w,
            -100f,
            100f);
        commandBuffer.SetViewProjectionMatrices(
            Matrix4x4.identity,
            GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));

        var context = new LightingMaterialEmissionContext(materialField, emissionField, worldRect);
        foreach (Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor contributor in _contributors)
        {
            contributor.RenderMaterialEmissionFields(commandBuffer, context);
        }
    }

    public void RenderAmbientOcclusionField(
        CommandBuffer commandBuffer,
        RenderTexture ambientOcclusionField,
        Vector4 worldRect,
        bool clearField = true)
    {
        if (commandBuffer == null)
        {
            throw new ArgumentNullException(nameof(commandBuffer));
        }

        if (!ambientOcclusionField.IsCreated())
        {
            throw new InvalidOperationException(
                "The AO contact field must be created before geometry contributors are rendered.");
        }

        if (_contributors.Count == 0)
        {
            throw new InvalidOperationException(
                "World lighting has no registered geometry contributors.");
        }

        if (clearField)
        {
            _ambientOcclusionTarget[0] = new RenderTargetIdentifier(ambientOcclusionField);
            commandBuffer.SetRenderTarget(
                _ambientOcclusionTarget,
                new RenderTargetIdentifier(BuiltinRenderTextureType.None));
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);
        }

        Matrix4x4 projection = Matrix4x4.Ortho(
            worldRect.x,
            worldRect.x + worldRect.z,
            worldRect.y,
            worldRect.y + worldRect.w,
            -100f,
            100f);
        commandBuffer.SetViewProjectionMatrices(
            Matrix4x4.identity,
            GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));

        var context = new LightingAmbientOcclusionContext(ambientOcclusionField, worldRect);
        foreach (Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor contributor in _contributors)
        {
            contributor.RenderAmbientOcclusionField(commandBuffer, context);
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
    {
        return (value << offset) | (value >> (64 - offset));
    }
}
