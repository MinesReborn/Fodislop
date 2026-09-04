#nullable enable

using Fodinae.Core.Interfaces;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyScenarioController
{
    private readonly IOfflineScenarioSettings _settings;
    private int _lifecycleVersion = -1;
    private bool _helloHandled;

    public DummyScenarioController(IOfflineScenarioSettings settings)
    {
        _settings = settings;
    }

    public OfflineScenario ActiveScenario { get; private set; } = OfflineScenario.HappyPath;

    public bool StallsConnection => ActiveScenario == OfflineScenario.HandshakeTimeout;

    public bool DisconnectsDuringHandshake =>
        ActiveScenario == OfflineScenario.DisconnectDuringHandshake;

    public bool RejectsAuthentication =>
        ActiveScenario == OfflineScenario.RejectAuthentication;

    public bool StallsWorldInitialization =>
        ActiveScenario == OfflineScenario.WorldInitializationTimeout;

    public void BeginLifecycle(int lifecycleVersion)
    {
        _lifecycleVersion = lifecycleVersion;
        _helloHandled = false;
        ActiveScenario = _settings.Scenario;
    }

    public bool TryBeginHello(int lifecycleVersion)
    {
        if (lifecycleVersion != _lifecycleVersion || _helloHandled)
        {
            return false;
        }

        _helloHandled = true;
        return true;
    }
}
