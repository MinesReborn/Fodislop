#nullable enable

using System;

namespace Kern.Core;

public sealed class SceneContractException : InvalidOperationException
{
    public SceneContractException(string message)
        : base(message)
    {
    }

    public SceneContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
