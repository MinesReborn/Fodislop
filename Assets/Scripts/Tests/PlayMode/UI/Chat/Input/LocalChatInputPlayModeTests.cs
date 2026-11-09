#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using Kern.Bootstrap.Scenes;
using Kern.UI.Input;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Путь «нажал T — получил поле локального чата».
///
/// Клавишу читает LocalChatInput, которым владеет FloatingChatManager, и перед
/// показом он спрашивает IInputBlocker. Если вход заблокирован — открытым
/// окном сервера, паузой или модальным окном, — T молча игнорируется, и без
/// дампа состояния это выглядит как «T не работает».
///
/// Состояние блокировки печатается обязательно: по нему видно, сломан путь
/// ввода или вход был занят чем-то посторонним.
/// </summary>
[TestFixture]
public sealed class LocalChatInputPlayModeTests
{
    private const string TestDummyToken = "playmode-local-chat-token";
    private DummyAuthenticationScope _authentication = null!;
    private BootstrapLifetimeScope _bootstrap = null!;
    private UIInputManager _uiInput = null!;
    private IInputBlocker _blocker = null!;
    private UIDocument _document = null!;
    private VirtualKeyboard? _keyboard;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtMainMenu();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        _uiInput = PlayModeHarness.RequireInGame<UIInputManager>();
        _blocker = PlayModeHarness.RequireInGame<IInputBlocker>();
        _document = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(SceneNames.MainGame))!;
        _keyboard = new VirtualKeyboard();
    }

    [UnityTest]
    public IEnumerator T_FocusesTheLocalChatInput()
    {
        TextField field = _document.rootVisualElement.Q<TextField>(className: "lchat-input")
            ?? throw new AssertionException("The local chat input is not in the panel.");

        Debug.Log(
            $"[LocalChatInput] before: blocked={_blocker.IsInputBlocked} modal={_uiInput.IsModalOpen} " +
            $"pause={_uiInput.IsPauseMenuOpen} focused={_uiInput.IsChatFocused} " +
            $"panelDisplay={field.resolvedStyle.display} keyboard={Keyboard.current != null}");

        yield return _keyboard!.Tap(Key.T);
        yield return PlayModeHarness.WaitUntil(
            () => _uiInput.IsChatFocused,
            PlayModeHarness.UITimeoutSeconds,
            "T did not focus the local chat input.");

        IResolvedStyle style = field.resolvedStyle;
        Debug.Log(
            $"[LocalChatInput] after: focused={_uiInput.IsChatFocused} display={style.display} " +
            $"visibility={style.visibility} w={style.width} h={style.height}");

        Assert.That(style.display, Is.Not.EqualTo(DisplayStyle.None),
            "The input reports focus but is still not displayed.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _keyboard?.Dispose();
        _keyboard = null;
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }
}
