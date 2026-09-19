#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

// Клавиатура игрока в живой игре: меню и чат забирают ввод целиком, и пока
// они открыты, ни одно нажатие не уходит серверу как игровое действие.
[TestFixture]
public sealed class UIInputPlayModeTests
{
    private const string TestDummyToken = "playmode-ui-input-token";
    private const int HoldFrames = 30;
    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private VirtualKeyboard? _keyboard;
    private DummyConnection? _dummy;
    private UIInputManager _uiInput = null!;
    private int _movementPackets;

    private VirtualKeyboard _Keyboard => _keyboard ?? throw new AssertionException("Virtual keyboard is not set up.");

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        _uiInput = PlayModeHarness.RequireInGame<UIInputManager>();
        _keyboard = new VirtualKeyboard();
        _dummy = _bootstrap.Container.Resolve<DummyConnection>();
        _dummy.ClientPacketSent += CountMovement;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_dummy != null)
        {
            _dummy.ClientPacketSent -= CountMovement;
        }

        _keyboard?.Dispose();
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator Escape_OpensAndClosesPauseMenu()
    {
        IInputBlocker blocker = PlayModeHarness.RequireInGame<IInputBlocker>();

        yield return _Keyboard.Tap(Key.Escape);
        Assert.That(_uiInput.IsPauseMenuOpen, Is.True, "Escape did not open the pause menu.");
        Assert.That(blocker.IsInputBlocked, Is.True);

        yield return _Keyboard.Tap(Key.Escape);
        Assert.That(_uiInput.IsPauseMenuOpen, Is.False, "Escape did not close the pause menu.");
        Assert.That(blocker.IsInputBlocked, Is.False);
    }

    [UnityTest]
    public IEnumerator OpenPauseMenu_BlocksMovementUntilClosed()
    {
        yield return _Keyboard.Tap(Key.Escape);
        Assert.That(_uiInput.IsPauseMenuOpen, Is.True);

        _movementPackets = 0;
        yield return HoldMovement();
        Assert.That(_movementPackets, Is.Zero, "Movement reached the server while the pause menu was open.");

        yield return _Keyboard.Tap(Key.Escape);
        Assert.That(_uiInput.IsPauseMenuOpen, Is.False);

        _movementPackets = 0;
        yield return HoldMovement();
        Assert.That(_movementPackets, Is.GreaterThan(0), "Movement did not resume after closing the pause menu.");
    }

    [UnityTest]
    public IEnumerator FocusedChat_SwallowsGameplayHotkeys()
    {
        MapModeState map = PlayModeHarness.RequireInGame<MapModeState>();

        yield return _Keyboard.Tap(Key.T);
        yield return PlayModeHarness.WaitUntil(
            () => _uiInput.IsChatFocused,
            PlayModeHarness.UITimeoutSeconds,
            "T did not focus the chat input.");

        yield return _Keyboard.Tap(Key.M);
        Assert.That(map.IsOpen, Is.False, "M toggled the map while the player was typing.");

        _movementPackets = 0;
        yield return HoldMovement();
        Assert.That(_movementPackets, Is.Zero, "Typing in chat moved the robot.");

        // Escape закрывает чат и не должен тем же нажатием открыть меню паузы.
        yield return _Keyboard.Tap(Key.Escape);
        Assert.That(_uiInput.IsChatFocused, Is.False, "Escape did not leave the chat.");
        Assert.That(_uiInput.IsPauseMenuOpen, Is.False, "Escape closed the chat and opened the pause menu at once.");

        yield return _Keyboard.Tap(Key.M);
        Assert.That(map.IsOpen, Is.True, "M did not open the map after leaving the chat.");
        yield return _Keyboard.Tap(Key.M);
        Assert.That(map.IsOpen, Is.False, "M did not close the map.");
    }

    // Робот может упираться в стену с одной стороны, поэтому зажимаются обе.
    private IEnumerator HoldMovement()
    {
        foreach (Key key in new[] { Key.D, Key.A })
        {
            _Keyboard.Hold(key);
            yield return PlayModeHarness.Frames(HoldFrames);
            _Keyboard.Release(key);
            yield return null;
        }
    }

    private void CountMovement(ClientPacket packet)
    {
        if (packet.Data is ActionClientPacket { Payload: MovePacket or RotatePacket })
        {
            _movementPackets++;
        }
    }
}
