#nullable enable

namespace Fodinae.Core.Interfaces;

public enum OfflineScenario
{
    HappyPath,
    RejectAuthentication,
    DisconnectDuringHandshake,
    HandshakeTimeout,
    WorldInitializationTimeout,
}

public interface IOfflineScenarioSettings
{
    OfflineScenario Scenario { get; set; }
}

public sealed class OfflineScenarioSettings : IOfflineScenarioSettings
{
    public OfflineScenario Scenario { get; set; } = OfflineScenario.HappyPath;
}
