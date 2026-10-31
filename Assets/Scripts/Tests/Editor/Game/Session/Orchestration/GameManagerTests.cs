#nullable enable

using System;
using System.Text.RegularExpressions;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Kern.Game.Session;

namespace Kern.Tests.Core;

[TestFixture]
public sealed class GameManagerTests
{
    [Test]
    public void SetState_ThrowingObserverDoesNotPreventOtherObservers()
    {
        GameManager manager = new(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        GameState? observedState = null;
        manager.OnGameStateChanged += _ => throw new InvalidOperationException("observer failed");
        manager.OnGameStateChanged += state => observedState = state;

        LogAssert.Expect(
            LogType.Exception,
            new Regex("InvalidOperationException: observer failed"));

        manager.SetState(GameState.InGame);

        Assert.That(manager.CurrentState, Is.EqualTo(GameState.InGame));
        Assert.That(observedState, Is.EqualTo(GameState.InGame));
    }
}
