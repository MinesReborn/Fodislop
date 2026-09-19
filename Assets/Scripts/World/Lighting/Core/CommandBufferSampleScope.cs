#nullable enable

using System;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

// RAII guard pairing CommandBuffer.BeginSample with EndSample across every
// exit: early returns and exceptions included. A raw Begin/End pair skews
// the profiler as soon as any path between them throws or returns early.
internal readonly struct CommandBufferSampleScope : IDisposable
{
    private readonly CommandBuffer _commandBuffer;
    private readonly string _sampleName;

    public CommandBufferSampleScope(CommandBuffer commandBuffer, string sampleName)
    {
        _commandBuffer = commandBuffer;
        _sampleName = sampleName;
    }

    public void Dispose()
    {
        _commandBuffer.EndSample(_sampleName);
    }
}
