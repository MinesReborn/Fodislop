#nullable enable

using Kern.UI.HUD.Player.Model;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public class PlayerStatsModelTests
{
    private PlayerStatsModel _statsModel = null!;

    [SetUp]
    public void SetUp()
    {
        _statsModel = new PlayerStatsModel();
    }

    [Test]
    public void HealthPercent_CalculatesCorrectRatio()
    {
        _statsModel.SetHealth(50, 100);
        Assert.AreEqual(0.5f, _statsModel.HealthPercent, 0.001f);

        _statsModel.SetHealth(0, 100);
        Assert.AreEqual(0.0f, _statsModel.HealthPercent, 0.001f);

        _statsModel.SetHealth(100, 100);
        Assert.AreEqual(1.0f, _statsModel.HealthPercent, 0.001f);
    }

    [Test]
    public void HealthPercent_ZeroMaxHealth_ReturnsZeroWithoutException()
    {
        _statsModel.SetHealth(0, 0);
        Assert.AreEqual(0.0f, _statsModel.HealthPercent, 0.001f, "HealthPercent should safely return 0 when max health is 0.");
    }

    [Test]
    public void SetHealth_FiresOnHealthChangedAndOnStatsChanged()
    {
        bool healthFired = false;
        bool statsFired = false;

        _statsModel.OnHealthChanged += () => healthFired = true;
        _statsModel.OnStatsChanged += () => statsFired = true;

        _statsModel.SetHealth(75, 100);

        Assert.IsTrue(healthFired);
        Assert.IsTrue(statsFired);
        Assert.AreEqual(75, _statsModel.Health);
        Assert.AreEqual(100, _statsModel.MaxHealth);
    }

    [Test]
    public void SetHealth_WithSameValues_DoesNotFireEvents()
    {
        int healthEvents = 0;
        int statsEvents = 0;
        _statsModel.OnHealthChanged += () => healthEvents++;
        _statsModel.OnStatsChanged += () => statsEvents++;

        _statsModel.SetHealth(75, 100);
        _statsModel.SetHealth(75, 100);

        Assert.AreEqual(1, healthEvents);
        Assert.AreEqual(1, statsEvents);
    }

    [Test]
    public void StatusLines_AddAndRemove_UpdatesDictionaryAndFiresEvents()
    {
        bool statusLinesChanged = false;
        _statsModel.OnStatusLinesChanged += () => statusLinesChanged = true;

        _statsModel.AddStatusLine("buff_shield", new[] { "Shield Active" }, Color.blue, 0, 123456);

        Assert.IsTrue(statusLinesChanged);
        Assert.AreEqual(1, _statsModel.StatusLines.Count);
        Assert.IsTrue(_statsModel.StatusLines.ContainsKey("buff_shield"));
        Assert.AreEqual("Shield Active", _statsModel.StatusLines["buff_shield"].Text[0]);

        statusLinesChanged = false;
        _statsModel.RemoveStatusLine("buff_shield");

        Assert.IsTrue(statusLinesChanged);
        Assert.AreEqual(0, _statsModel.StatusLines.Count);
    }

    [Test]
    public void StatusLines_RepeatedPayload_DoesNotFireChangeEvent()
    {
        int changeEvents = 0;
        _statsModel.OnStatusLinesChanged += () => changeEvents++;

        _statsModel.AddStatusLine("online", new[] { "Online", "42" }, Color.white, 0, 0);
        _statsModel.AddStatusLine("online", new[] { "Online", "42" }, Color.white, 0, 0);

        Assert.AreEqual(1, changeEvents);
    }

    [Test]
    public void MissionLifecycle_SetProgressAndClear_UpdatesMissionProperties()
    {
        _statsModel.SetMission("Mine 50 Ores", "Mine any ores in layer 1", 50);

        Assert.IsTrue(_statsModel.IsMissionActive);
        Assert.AreEqual("Mine 50 Ores", _statsModel.MissionTitle);
        Assert.AreEqual(0, _statsModel.MissionProgress);
        Assert.AreEqual(50, _statsModel.MissionMaxProgress);

        _statsModel.SetMissionProgress(25);
        Assert.AreEqual(25, _statsModel.MissionProgress);

        _statsModel.ClearMission();
        Assert.IsFalse(_statsModel.IsMissionActive);

        // PlayerStatsModel использует string.Empty (как и остальные строковые поля),
        // а не null, для сброшенных значений.
        Assert.IsEmpty(_statsModel.MissionTitle);
        Assert.IsEmpty(_statsModel.MissionDescription);
    }
}
