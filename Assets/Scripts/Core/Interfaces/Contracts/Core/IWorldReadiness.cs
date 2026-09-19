#nullable enable

using System;

namespace Kern.Core.Interfaces;
public interface IWorldReadiness
{
    bool IsWorldLoaded { get; }

    void NotifyWorldLoaded();
}
