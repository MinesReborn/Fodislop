#nullable enable

using Kern.Core.Interfaces;

namespace Kern.Networking.Dummy;

public sealed class DummyScenarioSettings : IDummyScenarioSettings
{
    public DummyScenario Scenario { get; set; } = DummyScenario.HappyPath;
}
