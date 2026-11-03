#nullable enable

using Kern.Core.Interfaces;

namespace Kern.Networking.Offline;

internal sealed class DummyScenarioController
{
    private readonly IDummyScenarioSettings _settings;
    private int _lifecycleVersion = -1;
    private bool _helloHandled;

    public DummyScenarioController(IDummyScenarioSettings settings)
    {
        _settings = settings;
    }

    public DummyScenario ActiveScenario { get; private set; } = DummyScenario.HappyPath;

    public bool StallsConnection => ActiveScenario == DummyScenario.HandshakeTimeout;

    public bool DisconnectsDuringHandshake =>
        ActiveScenario == DummyScenario.DisconnectDuringHandshake;

    public bool RejectsAuthentication =>
        ActiveScenario == DummyScenario.RejectAuthentication;

    public bool StallsWorldInitialization =>
        ActiveScenario == DummyScenario.WorldInitializationTimeout;

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
