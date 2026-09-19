#nullable enable

namespace Kern.Core.Interfaces;
public interface IInputBlocker
{
    bool IsInputBlocked { get; }
    string? TopWindowTag { get; }
}
