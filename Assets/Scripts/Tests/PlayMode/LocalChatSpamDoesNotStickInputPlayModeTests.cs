#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Быстрая отправка локальных сообщений не должна оставлять ввод заблокированным.
///
/// «Работают все хоткеи и весь UI, кроме движения и локального чата» — это
/// ровно два места, которые спрашивают IInputBlocker.IsInputBlocked. Всё
/// остальное открывает панели своими контроллерами и блокировщика не видит,
/// поэтому симптом читается однозначно: залипший IsInputBlocked.
///
/// Тест повторяет сценарий пользователя — T, текст, Enter, снова T — и после
/// каждого цикла печатает полный разбор блокировщика, а не только итоговый
/// флаг. Список компонентов нужен, чтобы видеть, кто именно остался включён.
/// </summary>
[TestFixture]
public sealed class LocalChatSpamDoesNotStickInputPlayModeTests
{
    private const int Iterations = 30;

    private IInputBlocker _blocker = null!;
    private UIInputManager _uiInput = null!;
    private ServerWindowPresenter _windows = null!;
    private UIDocument _document = null!;
    private MapModeState? _map;
    private VirtualKeyboard? _keyboard;
    private VirtualKeyboard Keyboard => _keyboard ?? throw new AssertionException("Virtual keyboard is not set up.");
    private TextField? _field;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // Диагностика: вход в MainGame шумит чужыми исключениями (CoordinateUtils
        // без высоты мира и подобные), они не относятся к проверке. Гасим
        // только на время SetUp.
        LogAssert.ignoreFailingMessages = true;
        yield return PlayModeHarness.StartAtGateway();
        Kern.Core.BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
        LogAssert.ignoreFailingMessages = false;
        _blocker = PlayModeHarness.RequireInGame<IInputBlocker>();
        _uiInput = PlayModeHarness.RequireInGame<UIInputManager>();
        _windows = PlayModeHarness.RequireInGame<ServerWindowPresenter>();
        _document = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame))!;
        _map = PlayModeHarness.ResolveInGame<MapModeState>();
        _keyboard = new VirtualKeyboard();
    }

    [UnityTest]
    public IEnumerator FastLocalMessages_LeaveInputUnblocked()
    {
        bool baseline = _blocker.IsInputBlocked;
        Debug.Log($"[Spam] baseline blocked={baseline} {Dump()}");

        for (int i = 0; i < Iterations; i++)
        {
            yield return Keyboard.Tap(Key.T);
            yield return null;

            _field = _document.rootVisualElement.Q<TextField>(className: "lchat-input");
            if (_field != null)
            {
                _field.value = "спам " + i;
            }

            yield return Keyboard.Tap(Key.Enter);
            yield return null;

            if (_blocker.IsInputBlocked != baseline)
            {
                Debug.Log($"[Spam] BLOCKED at iteration {i}: {Dump()}");

                // Ввод обязан вернуться по Escape. Модалка от троттлинга
                // приходит пустой и нулевого размера, её не видно и нечем
                // закрыть мышью, поэтому единственный выход — клавиатура.
                yield return Keyboard.Tap(Key.Escape);
                yield return null;
                yield return null;

                Debug.Log($"[Spam] after Escape: {Dump()}");
                Assert.That(
                    _blocker.IsInputBlocked,
                    Is.EqualTo(baseline),
                    "Escape did not release the blocked input.");
                yield break;
            }
        }

        Debug.Log($"[Spam] survived {Iterations} iterations, blocked={_blocker.IsInputBlocked}");

        // T обязан работать после всей серии.
        yield return Keyboard.Tap(Key.T);
        yield return null;
        Assert.That(
            _uiInput.IsChatFocused,
            Is.True,
            $"T stopped opening the chat after {Iterations} messages. {Dump()}");
    }

    private string Dump()
    {
        VisualElement overlay = _document.rootVisualElement.Q(className: "modal-overlay");
        string modal = "<none>";
        if (overlay != null)
        {
            IResolvedStyle s = overlay.resolvedStyle;
            var title = overlay.Q<Label>(className: "modal-title");
            modal =
                $"[title=\"{title?.text}\" display={s.display} visibility={s.visibility} " +
                $"opacity={s.opacity} w={s.width} h={s.height} " +
                $"classes={overlay.GetClasses()}]";
        }

        return $"blocked={_blocker.IsInputBlocked} chatFocused={_uiInput.IsChatFocused} " +
               $"modalStack={_uiInput.IsModalOpen} pause={_uiInput.IsPauseMenuOpen} " +
               $"programmator={_uiInput.IsProgrammatorOpen} map={_map?.IsOpen} " +
               $"serverWindows={_windows.HasOpenWindows} topTag={_windows.TopWindowTag ?? "-"} " +
               $"serverModal={_windows.IsModalShowing} modal={modal}";
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _keyboard?.Dispose();
        _keyboard = null;
        yield return PlayModeHarness.Shutdown();
    }
}
