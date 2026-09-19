#nullable enable

using System;
using Kern.UI.HUD.Player.Model;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public class PlayerStatsModelFuzzTests
{
    [Test]
    public void RandomHealth_PercentFinite()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var m = new PlayerStatsModel();
            m.SetHealth(random.Next(-100, 10000), random.Next(0, 10000));
            float pct = m.HealthPercent;
            Assert.That(float.IsNaN(pct), Is.False, $"i={i}");
            Assert.That(float.IsInfinity(pct), Is.False, $"i={i}");
        }
    }

    [Test]
    public void ZeroMaxHealth_ZeroPercent()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var m = new PlayerStatsModel();
            m.SetHealth(random.Next(-100, 100), 0);
            Assert.That(m.HealthPercent, Is.EqualTo(0f));
        }
    }

    [Test]
    public void BasketMaxPercent_Clamped()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 100; i++)
        {
            var m = new PlayerStatsModel();
            uint cap = (uint)random.Next(1, 1000);
            int n = random.Next(1, 10);
            var contents = new long[n];
            for (int j = 0; j < n; j++) contents[j] = random.Next(0, (int)cap + 100);
            m.SetBasket(cap, contents);
            Assert.That(m.BasketMaxPercent, Is.InRange(0, 100), $"i={i}");
        }
    }

    [Test]
    public void IsReady_DependsOnAllFields()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 100; i++)
        {
            var m = new PlayerStatsModel();
            m.SetHealth(random.Next(1, 100), random.Next(1, 100));
            m.SetNickname("player");
            m.SetLevel(random.Next(1, 100));
            m.SetBasket((uint)random.Next(1, 100), new long[1]);
            Assert.IsTrue(m.IsReady, $"i={i}");
        }
    }

    [Test]
    public void BasketContents_Cloned()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var m = new PlayerStatsModel();
            var contents = new long[random.Next(1, 5)];
            for (int j = 0; j < contents.Length; j++) contents[j] = random.Next(0, 100);
            m.SetBasket((uint)random.Next(1, 100), contents);
            contents[0] = -1;
            Assert.That(m.BasketContents[0], Is.Not.EqualTo(-1L), $"i={i}");
        }
    }

    [Test]
    public void SetLevel_SameValue_NoEvent()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var m = new PlayerStatsModel();
            long level = random.Next(1, 100);
            m.SetLevel(level);
            int count = 0;
            m.OnLevelChanged += () => count++;
            m.SetLevel(level);
            Assert.That(count, Is.EqualTo(0), $"i={i}");
        }
    }

    [Test]
    public void SetNicknameNull_Empty()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var m = new PlayerStatsModel();
            m.SetNickname("test");
            m.SetNickname(null!);
            Assert.That(m.Nickname, Is.EqualTo(string.Empty), $"i={i}");
        }
    }
}
