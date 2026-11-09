#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using Kern.Bootstrap.Scenes;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Раскладка настоящего облака локального чата — макет visual/kern-ui (.wl-chat):
/// рамка 1 px, поля 2/6, строка высотой 16, хвост 1×6 в 5 px от левого края.
///
/// Меряет элемент, созданный через IWorldLabels, а не собранный вручную из USS:
/// тест обязан видеть то, что видит игрок.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatBubbleLayoutPlayModeTests
{
    private const float BorderPx = 1f;
    private const float PaddingTopPx = 2f;
    private const float PaddingSidePx = 6f;
    private const float LinePx = 16f;
    private const float TailLeftPx = 5f;
    private const float TailHeightPx = 6f;

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

    [UnityTest]
    public IEnumerator BubbleFrame_IsTheMockupFrame()
    {
        yield return ShowBubble("привет");
        IResolvedStyle style = Frame().resolvedStyle;
        Debug.Log($"[Bubble] border={style.borderLeftWidth} color={style.borderLeftColor} " +
                  $"padding={style.paddingTop}/{style.paddingLeft} bg={style.backgroundColor}");

        Assert.That(style.borderLeftWidth, Is.EqualTo(BorderPx).Within(0.01f));
        Assert.That(style.borderBottomWidth, Is.EqualTo(BorderPx).Within(0.01f));
        Assert.That(style.paddingTop, Is.EqualTo(PaddingTopPx).Within(0.01f));
        Assert.That(style.paddingLeft, Is.EqualTo(PaddingSidePx).Within(0.01f));
        Assert.That(style.backgroundColor.a, Is.GreaterThan(0.85f), "The bubble must sit on dense graphite.");
        Assert.That(style.backgroundImage.sprite, Is.Null, "The bubble frame is drawn by styles, not a sprite.");
        Assert.That(style.backgroundImage.texture, Is.Null, "The bubble frame is drawn by styles, not a texture.");
    }

    [UnityTest]
    public IEnumerator BubbleText_StartsInsideThePadding()
    {
        yield return ShowBubble("привет");
        Label text = FindText();
        float scale = scaleOf();
        float offsetTop = (text.worldBound.y - Frame().worldBound.y) / scale;

        Assert.That(text.resolvedStyle.marginTop, Is.EqualTo(0f).Within(0.01f),
            "The base theme's Label margin-top must be reset.");
        Assert.That(offsetTop, Is.EqualTo(BorderPx + PaddingTopPx).Within(1f));
        Assert.That(text.worldBound.height / scale, Is.EqualTo(LinePx).Within(0.5f));
    }

    [UnityTest]
    public IEnumerator BubbleFrame_StaysOneLineForALongMessage()
    {
        yield return ShowBubble("привет");
        VisualElement frame = Frame();
        float height = frame.resolvedStyle.height;

        _label!.SetText(
            "Очень длинное сообщение локального чата, которое обязано быть обрезано по ширине, "
            + "а не перенесено на вторую строку");
        yield return Frames(3);

        Assert.That(frame.resolvedStyle.height, Is.EqualTo(height).Within(0.5f),
            "The bubble height must not depend on the message.");
        Assert.That(height, Is.EqualTo(LinePx + (2f * (PaddingTopPx + BorderPx))).Within(0.5f));
    }

    [UnityTest]
    public IEnumerator BubbleTail_HangsUnderTheFrameNearItsLeftEdge()
    {
        yield return ShowBubble("привет");
        VisualElement frame = Frame();
        VisualElement tail = frame.Q<VisualElement>(className: "world-label-chat__tail")
            ?? throw new AssertionException("The chat bubble has no tail.");
        float scale = scaleOf();
        Rect f = frame.worldBound;
        Rect t = tail.worldBound;

        Assert.That((t.x - f.x) / scale, Is.EqualTo(TailLeftPx).Within(0.5f));
        Assert.That(t.width / scale, Is.EqualTo(1f).Within(0.1f));
        Assert.That(t.height / scale, Is.EqualTo(TailHeightPx).Within(0.1f));
        Assert.That(t.yMax, Is.GreaterThan(f.yMax), "The tail must reach below the frame.");
    }

    private float scaleOf() =>
        GameplayCameraDefaults.ReferenceOrthographicSize / _camera.orthographicSize;

    // Раскладка считается в LateTick, а показать пузырь можно только после
    // кадра с ним перед камерой: иначе метку срежет отсечение по видимости.
    private IEnumerator ShowBubble(string text)
    {
        _label = _labels.Create(WorldLabelKind.ChatBubble);
        _label.SetText(text);
        _label.SetVisible(true);
        yield return Frames(3);
    }

    private VisualElement Frame() => FindFrame();

    private IEnumerator Frames(int count)
    {
        Vector3 inView = _camera.transform.position + (_camera.transform.forward * 5f);
        for (int i = 0; i < count; i++)
        {
            _label!.SetPosition(inView);
            yield return null;
        }
    }

    private VisualElement FindFrame() =>
        _document.rootVisualElement.Q<VisualElement>(className: "world-label-chat")
        ?? throw new AssertionException("The chat bubble frame is not in the panel.");

    private Label FindText() =>
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
