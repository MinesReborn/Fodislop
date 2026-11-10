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
