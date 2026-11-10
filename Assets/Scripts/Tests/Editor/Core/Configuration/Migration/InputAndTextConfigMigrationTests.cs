#nullable enable

using Kern.Core;
using Kern.Core.Configuration;
using NUnit.Framework;

namespace Kern.Tests.Core;

public sealed class InputAndTextConfigMigrationTests
{
    [Test]
    public void TerrainDepthDefaultsToDisabledAndRetainsItsStrength()
    {
        var settings = new TerrainSettings();
        Assert.That(settings.DepthEnabled, Is.False);
        Assert.That(settings.DepthPixels, Is.EqualTo(16f));
        Assert.That(settings.DepthDarkening, Is.EqualTo(0.35f));
    }

    [Test]
    public void SavedActionKeepsKey_NewConflictingMovementIsUnassigned()
    {
        var settings = new InterfaceSettings { KeyDig = "w", UIScale = 1.2f };

        InputAndTextConfigMigration.Apply(settings, "{\"KeyDig\":\"w\",\"UIScale\":1.2}");

        Assert.That(settings.KeyDig, Is.EqualTo("w"));
        Assert.That(settings.KeyMoveUp, Is.EqualTo("None"));
        Assert.That(settings.KeyMoveUpAlternate, Is.EqualTo("UpArrow"));
        Assert.That(settings.KeyMoveDown, Is.EqualTo("S"));
        Assert.That(settings.UIScale, Is.EqualTo(1.2f));
        Assert.That(settings.TextScale, Is.EqualTo(1f));
    }

    [Test]
    public void ExplicitMovementAndTextSettingsArePreserved()
    {
        var settings = new InterfaceSettings { KeyMoveUp = "K", TextScale = 1.5f, KeyDig = "Space" };

        InputAndTextConfigMigration.Apply(settings, "{\"KeyMoveUp\":\"K\",\"TextScale\":1.5,\"KeyDig\":\"Space\"}");

        Assert.That(settings.KeyMoveUp, Is.EqualTo("K"));
        Assert.That(settings.TextScale, Is.EqualTo(1.5f));
        Assert.That(settings.KeyDig, Is.EqualTo("Space"));
    }

    [Test]
    public void LocalKeyOwnershipFollowsLiveMovementBindings()
    {
        var settings = new InterfaceSettings { KeyMoveUp = "K" };

        Assert.That(settings.IsGameplayKey("k"), Is.True);
        Assert.That(settings.IsGameplayKey("W"), Is.False);
        Assert.That(settings.IsGameplayKey("UpArrow"), Is.True);
        Assert.That(settings.IsGameplayKey("LeftAlt"), Is.True);
        Assert.That(settings.IsGameplayKey("Space"), Is.True);
    }

    [TestCase("LeftShift", "KeyTurnOnly")]
    [TestCase("LeftAlt", "KeyFastMove")]
    [TestCase("UpArrow", "KeyMoveUpAlternate")]
    public void SavedActionMayOwnAnAlternateOrModifier(string saved, string newlyAdded)
    {
        var settings = new InterfaceSettings { KeyHeal = saved };

        InputAndTextConfigMigration.Apply(settings, "{\"KeyHeal\":\"" + saved + "\"}");

        Assert.That(settings.KeyHeal, Is.EqualTo(saved));
        Assert.That(typeof(InterfaceSettings).GetField(newlyAdded)!.GetValue(settings), Is.EqualTo("None"));
    }
}
