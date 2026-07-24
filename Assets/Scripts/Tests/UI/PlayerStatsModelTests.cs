using NUnit.Framework;
using Fodinae.Scripts.UI.HUD.Player.Model;
using UnityEngine;

namespace Fodinae.Tests.UI
{
    [TestFixture]
    public class PlayerStatsModelTests
    {
        private GameObject _go;
        private PlayerStatsModel _statsModel;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestPlayerStatsModel");
            _statsModel = _go.AddComponent<PlayerStatsModel>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
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
            Assert.AreEqual(0.0f, _statsModel.HealthPercent, "HealthPercent should safely return 0 when max health is 0.");
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
            Assert.IsNull(_statsModel.MissionTitle);
        }
    }
}
