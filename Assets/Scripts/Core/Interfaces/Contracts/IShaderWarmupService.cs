#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Fodinae.Core;

/// <summary>
/// Upfront shader and graphics pipeline prewarm service.
/// Compiles raster shader passes, variant combinations and compute kernels on the user's GPU
/// before gameplay starts, priming the driver's persistent PSO cache and eliminating runtime hitches.
/// </summary>
public interface IShaderWarmupService
{
    UniTask WarmupAsync(Action<string, float>? progressCallback, CancellationToken cancellationToken);
}
