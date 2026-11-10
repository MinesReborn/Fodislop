#nullable enable

using System.Collections;
using Kern.Core.Interfaces;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Kern.Bootstrap.Scenes;
using Kern.UI.Chat;
using Kern.UI.Input;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Ввод не должен залипать после закрытия чата.
///
/// Движение робота и T локального чата — един hotkeys, которые спрашивают
/// IInputBlocker. Всё остальное (карта, инвентарь, пауза) живёт своими
/// контроллерами и этого не проверяет. Поэтому «UI живой, а робот не едет»
/// означает ровно одно: IsInputBlocked залип в true.
///
/// IsInputBlocked входит в него через UIInputManager.IsChatFocused, и дергают
/// этот флаг оба чата. У локального он выведен из состояния, у глобального
/// остался на событиях фокуса, а Hide() флаг не сбрасывает: закрытие панели
/// через display:none не обязано присылать BlurEvent, и тогда флаг остаётся
/// поднятым навсегда.
///
/// Тест открывает и закрывает глобальный чат и требует, чтобы ввод
/// вернулся в исходное состояние.
/// </summary>
[TestFixture]
public sealed class ChatFocusDoesNotStickPlayModeTests
{
    private IInputBlocker _blocker = null!;
    private UIInputManager _uiInput = null!;
    private GlobalChatUI _globalChat = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return PlayModeHarness.StartAtMainMenu();
        Kern.Bootstrap.Scenes.BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
        _blocker = PlayModeHarness.RequireInGame<IInputBlocker>();
        _uiInput = PlayModeHarness.RequireInGame<UIInputManager>();
        _globalChat = PlayModeHarness.RequireInGame<GlobalChatUI>();
    }

    [UnityTest]
    public IEnumerator GlobalChat_OpenThenClose_ReleasesChatFocus()
    {
        bool blockedBefore = _blocker.IsInputBlocked;
        yield return Frames(2);

        _globalChat.Show();
        yield return Frames(3);
        Debug.Log(
            $"[ChatFocus] opened: chatFocused={_uiInput.IsChatFocused} blocked={_blocker.IsInputBlocked}");

        _globalChat.Hide();
        yield return Frames(3);
        Debug.Log(
            $"[ChatFocus] closed: chatFocused={_uiInput.IsChatFocused} blocked={_blocker.IsInputBlocked}");

        Assert.That(
            _uiInput.IsChatFocused,
            Is.False,
            "Closing the global chat left IsChatFocused raised, so movement and the local " +
            "chat are blocked forever while the rest of the UI still works.");
        Assert.That(_blocker.IsInputBlocked, Is.EqualTo(blockedBefore));
    }

    [UnityTest]
    public IEnumerator GlobalChat_ToggleTwice_ReleasesChatFocus()
    {
        // Путь HUD-кнопки: Toggle() туда-сюда, и после второго нажатия ввод
        // обязан вернуться. Именно этим путём окно открывают и закрывают руками.
        _globalChat.Toggle();
        yield return Frames(3);
        _globalChat.Toggle();
        yield return Frames(3);

        Debug.Log(
            $"[ChatFocus] toggled: chatFocused={_uiInput.IsChatFocused} " +
            $"blocked={_blocker.IsInputBlocked}");

        Assert.That(_uiInput.IsChatFocused, Is.False);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return null;
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return PlayModeHarness.Shutdown();
    }
}
