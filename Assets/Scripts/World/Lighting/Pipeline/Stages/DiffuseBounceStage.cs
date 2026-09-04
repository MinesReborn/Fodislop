#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting.Pipeline.Stages
{
    /// <summary>
    /// Dispatches <c>SolveDiffuseBounce</c>: scatters the direct radiance in
    /// <c>_directTexture</c> by surface albedo into the receiver hemisphere.
    /// Extracted verbatim from the diffuse-bounce block inside the engine's
    /// former inline <c>UpdateLighting</c>. The enable/strength gate that
    /// decides whether to call this stage at all stays in
    /// <c>LightingEngine.UpdateLighting</c> - a stage only knows how to
    /// record its own dispatch, not when it should run.
    /// </summary>
    public sealed class DiffuseBounceStage : ILightingStage
    {
        private static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
        private static readonly int StaticDirectInputId = Shader.PropertyToID("_StaticDirectInput");
        private static readonly int BounceTextureId = Shader.PropertyToID("_BounceTexture");

        private readonly int _kernel;

        public DiffuseBounceStage(int kernel)
        {
            _kernel = kernel;
        }

        public void Record(CommandBuffer commandBuffer, in LightingFrameContext context)
        {
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                DirectInputId,
                context.DirectTexture);
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                StaticDirectInputId,
                context.StaticDirectTexture);
            commandBuffer.SetComputeTextureParam(
                context.Compute,
                _kernel,
                BounceTextureId,
                context.BounceTexture);
            commandBuffer.DispatchCompute(
                context.Compute,
                _kernel,
                Mathf.CeilToInt(context.BounceWidth / 8f),
                Mathf.CeilToInt(context.BounceHeight / 8f),
                1);
        }
    }
}
