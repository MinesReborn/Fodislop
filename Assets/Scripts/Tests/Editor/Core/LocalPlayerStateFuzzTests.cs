#nullable enable

using Kern.Core.Lifecycle;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Core;

[TestFixture]
public class LocalPlayerStateFuzzTests
{
    [Test]
    public void PublishThenClear_CurrentMatchesLast()
    {
        var go1 = new GameObject("p1"); go1.SetActive(false);
        var go2 = new GameObject("p2"); go2.SetActive(false);
        var p1 = go1.AddComponent<Kern.Player.Logic.PlayerMovementController>();
        var p2 = go2.AddComponent<Kern.Player.Logic.PlayerMovementController>();
        try
        {
            var random = new System.Random(42);
            var state = new LocalPlayerState();
            Kern.Core.Interfaces.ILocalPlayer? last = null;

            for (int i = 0; i < 200; i++)
            {
                if (random.Next(2) == 0)
                {
                    state.Publish(p1);
                    last = p1;
                }
                else
                {
                    state.Publish(p2);
                    last = p2;
                }

                if (random.Next(5) == 0 && last != null)
                {
                    state.Clear(last);
                    last = null;
                }

                if (last != null)
                    Assert.That(ReferenceEquals(state.Current, last), $"i={i}");
                else
                    Assert.That(state.Current, Is.Null, $"i={i}");
            }
        }
        finally
        {
            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
        }
    }

    [Test]
    public void PublishSameTwice_NoDuplicateEvent()
    {
        var go = new GameObject("p"); go.SetActive(false);
        var p = go.AddComponent<Kern.Player.Logic.PlayerMovementController>();
        try
        {
            var state = new LocalPlayerState();
            state.Publish(p);
            int count = 0;
            state.Changed += _ => count++;
            state.Publish(p);
            Assert.That(count, Is.EqualTo(0));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void ClearWrongPlayer_NoChange()
    {
        var go1 = new GameObject("a"); go1.SetActive(false);
        var go2 = new GameObject("b"); go2.SetActive(false);
        var p1 = go1.AddComponent<Kern.Player.Logic.PlayerMovementController>();
        var p2 = go2.AddComponent<Kern.Player.Logic.PlayerMovementController>();
        try
        {
            var state = new LocalPlayerState();
            state.Publish(p1);
            state.Clear(p2);
            Assert.That(ReferenceEquals(state.Current, p1));
        }
        finally
        {
            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
        }
    }
}
