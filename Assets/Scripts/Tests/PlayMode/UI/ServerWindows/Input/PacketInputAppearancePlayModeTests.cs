#nullable enable

// Тест редакторский: тема грузится через AssetDatabase. Guard нужен на случай
// player-сборки с включёнными PlayMode-тестами — там UnityEditor недоступен,
// а ассембль тестов собирается под все платформы.
#if UNITY_EDITOR
using System.Collections;
using Kern.UI.Controls;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Контракт вида серверных контролов пакета: текстовое поле и слайдер.
///
/// Поле: класс .sci-fi-input вешается на КОРЕНЬ TextField, а внутренний
/// .unity-base-text-field__input — отдельный элемент, который базовая тема
/// Unity заливает светлым rgb(240,240,240). Без нейтрализации внутреннего
/// фона каждое серверное текстовое поле рисовалось белой плашкой во всю
/// ширину окна (репорт «слайдеры — белые полосы»: полосами оказались
/// TextBoxPacket от билда сервера, сделанного ДО перевода кристаллов на
/// SliderPacket).
///
/// Слайдер: .packet-slider обязан держать высоту 16px и видимую дорожку —
/// линия читается и без ручки. Ручка-кругляш появляется только у живого
/// слайдера (--filled, MaxValue > MinValue — класс ставит SliderPacketBuilder):
/// строка без диапазона остаётся голой линией. Репорты: жёлтый квадрат сыпался
/// и на пустые строки; ручка нужна, если кристаллов > 0, и не нужна при нуле.
///
/// В EditMode resolvedStyle не читается (прохода стилей нет), поэтому
/// проверка — PlayMode: панель на KernTheme, кадр на раскладку, разбор
/// resolvedStyle каждого узла.
/// </summary>
public sealed class PacketInputAppearancePlayModeTests
{
    private static readonly Color s_textPrimary = new(240f / 255f, 246f / 255f, 248f / 255f, 1f);
    private static readonly Color s_accentGold = new(245f / 255f, 197f / 255f, 66f / 255f, 1f);

    private PanelSettings _panel = null!;
    private GameObject _host = null!;
    private VisualElement _root = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _panel = ScriptableObject.CreateInstance<PanelSettings>();
        // В Unity 6 свойства theme больше нет — тему назначает themeStyleSheet.
        _panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/Toolkit/Themes/Kern/KernTheme.tss");
        Assert.That(_panel.themeStyleSheet, Is.Not.Null, "KernTheme.tss обязан грузиться: без темы контракт вида бессмыслен");

        _host = new GameObject("PacketInputAppearance");
        var document = _host.AddComponent<UIDocument>();
        document.panelSettings = _panel;
        yield return null;
        _root = document.rootVisualElement;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_host != null)
        {
            Object.Destroy(_host);
        }

        if (_panel != null)
        {
            Object.Destroy(_panel);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator TextField_InnerInput_DoesNotShowDefaultLightBox()
    {
        var field = new RegexTextField { value = "0" };
        field.AddToClassList("sci-fi-input");
        _root.Add(field);
        yield return null;

        VisualElement? inner = field.Q(className: "unity-base-text-field__input");
        Assert.That(inner, Is.Not.Null,
            "у TextField обязан быть внутренний unity-base-text-field__input — класс-структура Unity 6");

        Color background = inner!.resolvedStyle.backgroundColor;
        TestContext.Out.WriteLine($"inner input background = {background}");
        Assert.That(IsLight(background), Is.False,
            ".sci-fi-input обязан гасить светлый фон внутреннего input: иначе поле — белая плашка дефолтной темы");

        Assert.That(inner.resolvedStyle.color, Is.EqualTo(s_textPrimary).Within(0.02f),
            "текст внутреннего input обязан наследовать --text-primary, а не дефолтный тёмный цвет темы Unity");
    }

    [UnityTest]
    public IEnumerator PacketSlider_KeepsCompactHeight_VisibleTrack_BareDraggerWithoutFilledMark()
    {
        var slider = new Slider(0f, 359f) { value = 12f };
        slider.AddToClassList("packet-slider");
        var track = new VisualElement();
        track.AddToClassList("packet-slider__track");
        track.pickingMode = PickingMode.Ignore;
        slider.Insert(0, track);
        _root.Add(slider);
        yield return null;

        Assert.That(slider.resolvedStyle.height, Is.EqualTo(16f).Within(0.5f),
            ".packet-slider держит фиксированную высоту — от неё считается центр дорожки");

        VisualElement? dragger = slider.Q(className: "unity-base-slider__dragger");
        Assert.That(dragger, Is.Not.Null,
            "у Slider обязан быть unity-base-slider__dragger — на него цепляется арт ручки из пакета");
        Assert.That(dragger!.resolvedStyle.backgroundColor.a, Is.EqualTo(0f),
            "без метки --filled ручка обязана быть невидимой: слайдер без диапазона — голая линия");

        Assert.That(IsLight(track.resolvedStyle.backgroundColor), Is.False,
            "дорожка — тёмная утопленная полоса, а не светлая плашка");
    }

    [UnityTest]
    public IEnumerator PacketSliderFilled_ShowsGoldKnob()
    {
        var slider = new Slider(0f, 359f) { value = 12f };
        slider.AddToClassList("packet-slider");
        slider.AddToClassList("packet-slider--filled");
        _root.Add(slider);
        yield return null;

        VisualElement? dragger = slider.Q(className: "unity-base-slider__dragger");
        Assert.That(dragger, Is.Not.Null,
            "у Slider обязан быть unity-base-slider__dragger — на него цепляется арт ручки из пакета");
        Assert.That(dragger!.resolvedStyle.backgroundColor, Is.EqualTo(s_accentGold).Within(0.02f),
            "у живого слайдера (--filled, MaxValue > MinValue) кругляш-ручка обязан быть виден: " +
            "репорт — «он должен быть, если кристаллов > 0»");
    }

    private static bool IsLight(Color color) =>
        color.a > 0.5f && color.r > 0.85f && color.g > 0.85f && color.b > 0.85f;
}
#endif
