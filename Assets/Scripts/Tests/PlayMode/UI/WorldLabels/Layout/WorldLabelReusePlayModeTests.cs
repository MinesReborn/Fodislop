#nullable enable

using System.Collections;
using Kern.Core.Interfaces;
using Kern.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using Kern.Bootstrap.Scenes;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Повторный показ мировой метки.
///
/// Метка уходит с экрана через visibility:hidden, а не через display:none, и
/// это не стилистический выбор. При display:none элемент выпадает из раскладки,
/// ширина и высота приходят NaN, и снять скрытие мог только ApplyVisible — а он
/// выходил раньше, по неизвестному размеру. Метка застревала скрытой навсегда:
/// пузырь, переиспользованный из пула после истечения трёх секунд, не показывал
/// сообщение больше никогда, и после первого сообщения с интервалом больше трёх
/// секунд локальный чат замолкал.
///
/// Тест гоняет тот же цикл напрямую через IWorldLabels и отдельно проверяет
/// зависимость от длины текста, потому что исходное наблюдение её утверждало, а
/// дедлок длины не объясняет.
///
/// Метка читается из DOM, а не через IWorldLabel: контракт намеренно не
/// отдаёт наружу Label, и расширять его ради теста не нужно.
/// </summary>
[TestFixture]
public sealed class WorldLabelReusePlayModeTests
{
    private const string LongMessage =
        "Очень длинное сообщение локального чата, которое обязано перенестись на несколько строк "
        + "и поэтому обязано сделать облако заметно выше короткого";

    private IWorldLabels _labels = null!;
    private IWorldLabel? _label;
    private UIDocument _document = null!;
    private Camera _camera = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return PlayModeHarness.StartAtMainMenu();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
        _labels = PlayModeHarness.RequireInGame<IWorldLabels>();
        _camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _document = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(SceneNames.MainGame))!;
        Assert.That(_document, Is.Not.Null, "MainGame has no UIDocument.");
    }

    // Метка отсекается PostLateTick'ом, если её мировая точка вне кадра, поэтому
    // каждый кадр ставим её перед камерой: проверяем показ, а не отсечение.
    private IEnumerator Frames(IWorldLabel label, int count)
    {
        Vector3 inView = _camera.transform.position + (_camera.transform.forward * 5f);
        for (int i = 0; i < count; i++)
        {
            label.SetPosition(inView);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator ReusedLabel_ComesBackAfterHiding()
    {
        IWorldLabel label = _labels.Create(WorldLabelKind.ChatBubble);
        _label = label;
        Label element = FindLabel();

        label.SetText("привет");
        label.SetVisible(true);
        yield return Frames(label, 3);

        float firstWidth = element.resolvedStyle.width;
        Assert.That(element.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));
        Assert.That(firstWidth, Is.GreaterThan(0f));

        // Гасим так же, как гаснет истёкший пузырь, и показываем заново: из
        // пула приходит именно такая метка.
        label.SetVisible(false);
        yield return Frames(label, 3);
        Assert.That(
            element.resolvedStyle.visibility,
            Is.EqualTo(Visibility.Hidden),
            "The label must leave the screen through visibility, not display:none.");

        label.SetText("снова");
        label.SetVisible(true);
        yield return Frames(label, 3);

        IResolvedStyle style = element.resolvedStyle;
        Debug.Log(
            $"[LabelReuse] after reuse w={style.width} h={style.height} " +
            $"display={style.display} visibility={style.visibility}");

        Assert.That(
            style.display,
            Is.Not.EqualTo(DisplayStyle.None),
            "A reused label stayed out of layout and can never be shown again.");
        Assert.That(style.visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(style.width, Is.GreaterThan(0f));
        Assert.That(style.height, Is.GreaterThan(0f));
    }

    // Высота облака постоянна — это высота спрайта, а вертикальный 9-slice при
    // ней не растягивается вовсе, поэтому хвост всегда одной формы. Растёт
    // только ширина: текст обрезается по max-width, а не переносится, и вторая
    // строка ушла бы под хвост.
    [UnityTest]
    public IEnumerator ReusedLabel_HeightIsFixedAndWidthFollowsText()
    {
        IWorldLabel label = _labels.Create(WorldLabelKind.ChatBubble);
        _label = label;
        VisualElement frame = FindFrame();
        Label element = FindLabel();

        label.SetText(LongMessage);
        label.SetVisible(true);
        yield return Frames(label, 3);
        float longHeight = frame.resolvedStyle.height;
        float longWidth = frame.resolvedStyle.width;
        float longTextWidth = element.resolvedStyle.width;

        // Гасим и поднимаем заново уже с коротким текстом: переиспользованная
        // метка обязана честно пересчитать бокс под новое содержимое.
        label.SetVisible(false);
        yield return Frames(label, 2);
        label.SetText("да");
        label.SetVisible(true);
        yield return Frames(label, 3);

        IResolvedStyle style = frame.resolvedStyle;
        Debug.Log(
            $"[LabelReuse] long h={longHeight} w={longWidth} textW={longTextWidth} " +
            $"-> short h={style.height} w={style.width}");

        Assert.That(
            style.height,
            Is.EqualTo(longHeight).Within(0.5f),
            "The bubble height must not depend on the message: the vertical 9-slice is " +
            "meant never to stretch, so the tail keeps one shape.");
        Assert.That(
            longWidth,
            Is.GreaterThan(style.width + 1f),
            "The bubble must grow with the text: a reused label did not recompute its width.");
        Assert.That(style.display, Is.Not.EqualTo(DisplayStyle.None));
        Assert.That(style.visibility, Is.EqualTo(Visibility.Visible));
    }

    // Рамка и текст — разные элементы: постоянную высоту задаёт рамка, а текст
    // лежит в ней вложенным и обрезается по ширине.
    private VisualElement FindFrame() =>
        _document.rootVisualElement.Q<VisualElement>(className: "world-label-chat")
        ?? throw new AssertionException("The chat bubble frame is not in the panel.");

    private Label FindLabel() =>
        _document.rootVisualElement.Q<Label>(className: "world-label-chat-text")
        ?? throw new AssertionException("The chat bubble text is not in the panel.");

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _label?.Dispose();
        _label = null;
        yield return PlayModeHarness.Shutdown();
    }
}
