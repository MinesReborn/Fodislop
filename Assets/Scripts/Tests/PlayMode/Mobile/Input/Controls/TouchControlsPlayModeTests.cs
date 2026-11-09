#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using Kern.Bootstrap.Scenes;
using Kern.Game.Player.Input;
using Kern.UI.Screens;

namespace Kern.Tests.PlayMode.Mobile;

// Пальцы в живой игре: касание идёт с тачскрина Input System через UI
// Toolkit в HUD и через EnhancedTouch в трекер жестов — тем же путём, что
// на телефоне. Экран подменён сенсорной формой того же размера в пикселях.
[TestFixture]
public sealed class TouchControlsPlayModeTests
{
    private VirtualTouchscreen? _touch;
    private UIDocument _game = null!;
    private PlayerInputHandler _input = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return PlayModeHarness.StartAtMainMenu();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);

        UIScreen.OverrideForTests(ScreenShapeRules.Resolve(
            new Vector2Int(Screen.width, Screen.height),
            320f,
            new Rect(0, 0, Screen.width, Screen.height),
            usesPhysicalScale: true,
            isTouch: true));
        _touch = new VirtualTouchscreen();
        _game = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(SceneNames.MainGame))!;
        _input = PlayModeHarness.RequireInGame<PlayerInputHandler>();
        yield return PlayModeHarness.Frames(5);
        _touch.Panel = _game.rootVisualElement.panel;
        _touch.PanelScale = PanelToScreenScale();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _touch?.Dispose();
        _touch = null;
        UIScreen.OverrideForTests(null);
        yield return PlayModeHarness.Shutdown();
    }

    [UnityTest]
    public IEnumerator StickAndDig_WorkWithTwoFingersAtOnce()
    {
        VirtualTouchscreen touch = _touch!;
        Vector2 pad = ScreenCenterOf("MobileMovePad");
        float padRadius = PanelToScreenScale() * _game.rootVisualElement.panel.visualTree.Q("MobileMovePad").worldBound.width * 0.5f;
        Vector2 dig = ScreenCenterOf("MobileDigButton");

        touch.Begin(1, pad);
        yield return null;
        touch.Move(1, pad + new Vector2(padRadius, 0f));
        touch.Begin(2, dig);
        yield return PlayModeHarness.Frames(3);

        Assert.That(_input.MoveInput.x, Is.GreaterThan(0.7f), "The stick finger does not steer right.");
        Assert.That(_input.WantsToDig, Is.True, "The second finger on Dig is not digging while the stick is held.");
        Assert.That(_input.IsShiftPressed, Is.False, "A full stick deflection must move, not only turn.");

        touch.End(2, dig);
        touch.End(1, pad + new Vector2(padRadius, 0f));
        yield return PlayModeHarness.Frames(3);

        Assert.That(_input.MoveInput, Is.EqualTo(Vector2.zero), "Releasing the stick keeps the drone moving.");
        Assert.That(_input.WantsToDig, Is.False, "Releasing Dig keeps digging.");
    }

    [UnityTest]
    public IEnumerator SmallStickDeflection_TurnsInPlace()
    {
        VirtualTouchscreen touch = _touch!;
        Vector2 pad = ScreenCenterOf("MobileMovePad");
        float padRadius = PanelToScreenScale() * _game.rootVisualElement.panel.visualTree.Q("MobileMovePad").worldBound.width * 0.5f;

        touch.Begin(1, pad);
        yield return null;
        touch.Move(1, pad + new Vector2(0f, padRadius * 0.1f));
        yield return PlayModeHarness.Frames(3);

        Assert.That(_input.IsShiftPressed, Is.True, "A small deflection must turn the drone in place (Shift).");
        touch.End(1, pad);
        yield return PlayModeHarness.Frames(2);
    }

    [UnityTest]
    public IEnumerator WorldTap_WithStickHeld_ReachesTheWorldButUiTapDoesNot()
    {
        VirtualTouchscreen touch = _touch!;
        TouchGestureTracker tracker = PlayModeHarness.RequireInGame<TouchGestureTracker>();
        int taps = 0;
        void Count(Vector2 _) => taps++;
        tracker.WorldTapped += Count;

        Vector2 pad = ScreenCenterOf("MobileMovePad");
        Vector2 world = new(Screen.width * 0.5f, Screen.height * 0.5f);
        touch.Begin(1, pad);
        yield return PlayModeHarness.Frames(2);
        touch.Begin(2, world);
        yield return PlayModeHarness.Frames(2);
        touch.End(2, world);
        yield return PlayModeHarness.Frames(2);
        Assert.That(taps, Is.EqualTo(1), "A world tap by the second finger was lost while the first held the stick.");

        touch.End(1, pad);
        yield return PlayModeHarness.Frames(2);
        Assert.That(taps, Is.EqualTo(1), "Releasing the stick finger counted as a world tap.");

        Vector2 menu = ScreenCenterOf("TouchMenuButton");
        touch.Begin(3, menu);
        yield return PlayModeHarness.Frames(2);
        touch.End(3, menu);
        yield return PlayModeHarness.Frames(3);
        tracker.WorldTapped -= Count;
        Assert.That(taps, Is.EqualTo(1), "A tap on a HUD button leaked into the world.");
    }

    [UnityTest]
    public IEnumerator Pinch_ZoomsTheCamera()
    {
        VirtualTouchscreen touch = _touch!;
        Camera camera = PlayModeHarness.RequireInGame<Camera>();
        Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 step = new(Screen.width * 0.05f, 0f);
        yield return PlayModeHarness.Frames(30);
        float before = camera.orthographicSize;

        touch.Begin(1, center - step);
        touch.Begin(2, center + step);
        yield return PlayModeHarness.Frames(3);
        for (int i = 1; i <= 10; i++)
        {
            touch.Move(1, center - (step * (1f + (i * 0.2f))));
            touch.Move(2, center + (step * (1f + (i * 0.2f))));
            yield return null;
        }

        touch.End(1, center - (step * 3f));
        touch.End(2, center + (step * 3f));
        yield return PlayModeHarness.Frames(60);

        Assert.That(camera.orthographicSize, Is.LessThan(before * 0.8f), "Spreading two fingers did not zoom in.");
    }

    private float PanelToScreenScale() => _game.panelSettings.scale;

    // Центр элемента в экранных пикселях: панель в режиме ConstantPixelSize,
    // пиксель панели — scale пикселей экрана, ось Y экрана — снизу вверх.
    private Vector2 ScreenCenterOf(string name)
    {
        VisualElement element = _game.rootVisualElement.panel.visualTree.Q(name) ??
            throw new AssertionException($"{name} is not in the game document.");
        Vector2 panel = element.worldBound.center;
        float scale = PanelToScreenScale();
        return new Vector2(panel.x * scale, Screen.height - (panel.y * scale));
    }
}
