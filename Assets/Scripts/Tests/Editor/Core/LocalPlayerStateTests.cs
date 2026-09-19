#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Player.Logic;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Core;

[TestFixture]
public class LocalPlayerStateTests
{
    private LocalPlayerState _state = null!;
    private GameObject _playerGo = null!;
    private PlayerMovementController _player = null!;

    [SetUp]
    public void SetUp()
    {
        _state = new LocalPlayerState();
        _playerGo = new GameObject("LocalPlayerStateTests.Player");
        _playerGo.SetActive(false);
        _player = _playerGo.AddComponent<PlayerMovementController>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_playerGo != null)
        {
            UnityEngine.Object.DestroyImmediate(_playerGo);
        }
    }

    [Test]
    public void Publish_SetsCurrentAndRaisesChanged()
    {
        ILocalPlayer? observed = null;
        _state.Changed += player => observed = player;

        _state.Publish(_player);

        Assert.AreEqual(_player, _state.Current);
        Assert.AreEqual(_player, observed);
    }

    [Test]
    public void Publish_SamePlayerTwice_DoesNotRaiseChangedAgain()
    {
        int events = 0;
        _state.Changed += _ => events++;

        _state.Publish(_player);
        _state.Publish(_player);

        Assert.AreEqual(1, events);
    }

    [Test]
    public void Clear_PublishedPlayer_SetsNullAndRaisesChangedWithNull()
    {
        _state.Publish(_player);
        ILocalPlayer? observed = null;
        _state.Changed += player => observed = player;

        _state.Clear(_player);

        Assert.IsNull(_state.Current);
        Assert.IsNull(observed);
    }

    [Test]
    public void Clear_UnpublishedPlayer_IsNoOp()
    {
        _state.Publish(_player);
        int events = 0;
        _state.Changed += _ => events++;

        var otherGo = new GameObject("LocalPlayerStateTests.Other");
        otherGo.SetActive(false);
        try
        {
            _state.Clear(otherGo.AddComponent<PlayerMovementController>());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(otherGo);
        }

        Assert.AreEqual(_player, _state.Current);
        Assert.AreEqual(0, events);
    }

    [Test]
    public void Interface_IsImplementedByLocalPlayerState()
    {
        Assert.IsTrue(_state is ILocalPlayerState);
    }
}
