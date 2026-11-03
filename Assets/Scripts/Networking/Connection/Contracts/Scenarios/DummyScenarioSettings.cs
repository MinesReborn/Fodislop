#nullable enable

namespace Kern.Core.Interfaces;

public enum DummyScenario
{
    HappyPath,
    RejectAuthentication,
    DisconnectDuringHandshake,
    HandshakeTimeout,
    WorldInitializationTimeout,
}

public interface IDummyScenarioSettings
{
    DummyScenario Scenario { get; set; }
}

public sealed class DummyScenarioSettings : IDummyScenarioSettings
{
    public DummyScenario Scenario { get; set; } = DummyScenario.HappyPath;
}
