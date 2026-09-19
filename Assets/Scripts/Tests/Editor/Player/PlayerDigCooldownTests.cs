#nullable enable

using Kern.Player.Logic;
using NUnit.Framework;

namespace Kern.Tests.Editor.Player;

public sealed class PlayerDigCooldownTests
{
    [TestCase(10f, 9.75f, 0.3f, true)]
    [TestCase(10f, 9.5f, 0.3f, false)]
    [TestCase(10f, 9.5f, 0.75f, true)]
    [TestCase(10f, 9f, 0.75f, false)]
    public void UsesServerCooldownExactly(
        float currentTime,
        float lastDigTime,
        float serverCooldown,
        bool expectedActive)
    {
        Assert.That(
            PlayerMovementController.IsDigCooldownActive(
                currentTime,
                lastDigTime,
                serverCooldown),
            Is.EqualTo(expectedActive));
    }
}
