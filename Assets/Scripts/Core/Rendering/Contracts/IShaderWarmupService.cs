#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kern.Core;

public interface IShaderWarmupService
{
    UniTask WarmupAsync(Action<string, float>? progressCallback, CancellationToken cancellationToken);
}
