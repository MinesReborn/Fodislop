#nullable enable

namespace Kern.Core.Interfaces;
public interface IInputBlocker
{
    bool IsInputBlocked { get; }
    bool IsInputBlockedExcludingMapMode { get; }
    string? TopWindowTag { get; }
}
