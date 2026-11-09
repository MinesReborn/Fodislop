#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

/// <summary>
/// Контракт облака локального чата — макет visual/kern-ui (css/events.css .wl-chat, systems.css):
/// рамка стилями (1 px seam-strong, плотный графит, поля 2/6), строка 12 px высотой 16,
/// под рамкой хвост — отдельный элемент 1×6 в 5 px от левого края. Облако вешается
/// за нижний конец хвоста и масштабируется целиком от левого верхнего угла.
///
/// Числа раскладки проверяют PlayMode-тесты: в EditMode resolvedStyle нельзя читать
/// до прохода стилей, поэтому здесь только контракт исходников.
///
/// Отдельно охраняется порядок импортов в KernTheme: при равной специфичности
/// выигрывает объявленное позже, и .unity-label из базовой темы перебивал
/// правила подписей мира.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatStyleTests
{
    private const string BubbleRulesPath = "Assets/Resources/UI/WorldLabels/Styles/WorldLabels.uss";
    private const string BubbleClass = ".world-label-chat";
    private const string BubbleTextClass = ".world-label-chat-text";
    private const string BubbleTailClass = ".world-label-chat__tail";
    private const string ThemePath = "Assets/UI/Toolkit/Themes/Kern/KernTheme.tss";
    private const string BaseThemeImport = "unity-theme://default";
    private const string WorldLabelsImport = "UI/WorldLabels/Styles/WorldLabels.uss";
    private const string WorldLabelsSource = "Assets/Scripts/UI/WorldLabels/Presentation/WorldLabels.cs";

    [Test]
    public void BubbleFrame_IsDrawnByStylesAsInTheMockup()
    {
        // Рамка — стилями: спрайта больше нет, слайсы и тон не нужны.
        string frame = ReadClass(BubbleClass);
        Assert.That(frame, Does.Match(@"border-width:\s*1px"));
        Assert.That(frame, Does.Contain("border-color: var(--seam-strong)"));
        Assert.That(frame, Does.Contain("background-color:"));
        Assert.That(frame, Does.Match(@"padding:\s*2px 6px"));
        Assert.That(frame, Does.Match(@"transform-origin:\s*0% 0%"));

        string worldLabels = StripComments(ReadSource(WorldLabelsSource));
        Assert.That(worldLabels, Does.Not.Contain("Resources.Load<Sprite>"));
        Assert.That(worldLabels, Does.Not.Contain("unitySlice"));
        Assert.That(worldLabels, Does.Not.Contain("unityBackgroundImageTintColor"));
    }

    [Test]
    public void BubbleText_IsOneLightLineOnTheGraphite()
    {
        string text = ReadClass(BubbleTextClass);
        Assert.That(text, Does.Match(@"height:\s*16px"));
        Assert.That(text, Does.Contain("font-size: var(--t-4)"));
        Assert.That(text, Does.Contain("color: var(--bone)"));
        Assert.That(text, Does.Match(@"margin:\s*0"));
    }

    [Test]
    public void BubbleTail_IsAThinLineUnderTheFrame()
    {
        string tail = ReadClass(BubbleTailClass);
        Assert.That(tail, Does.Contain("position: absolute"));
        Assert.That(tail, Does.Match(@"top:\s*100%"));
        Assert.That(tail, Does.Match(@"width:\s*1px"));
        Assert.That(tail, Does.Match(@"height:\s*6px"));

        string worldLabels = ReadSource(WorldLabelsSource);
        Assert.That(worldLabels, Does.Contain("tail.AddToClassList(BubbleTailClass)"));
        Assert.That(worldLabels, Does.Contain("frame.Add(tail)"));
    }

    [Test]
    public void BubbleStyle_DoesNotFreezeWidthOrHeightInTheOldWay()
    {
        // Прежний контракт требовал переноса и роста высоты под текст. Он больше
        // не верен: высота облака постоянна по эталону, а текст обрезается.
        // min-width отсутствует по той же причине — иначе короткое сообщение
        // растягивало бы облако.
        string rules = StripComments(ReadBubbleRules());
        Assert.That(rules, Does.Not.Contain("white-space: normal"));
        Assert.That(rules, Does.Not.Contain("min-width"));
        Assert.That(rules, Does.Not.Contain("min-height"));
    }

    [Test]
    public void BubbleText_IsClippedInsideTheFrameInsteadOfWrapping()
    {
        // Текст лежит вложенным элементом и обрезается: перенос растил бы число
        // строк, а высота рамки постоянна, и вторая строка ушла бы под хвост.
        // Это то, что делает оригинал через VerticalFit = Unconstrained.
        string text = ReadClass(BubbleTextClass);
        Assert.That(text, Does.Contain("white-space: nowrap"));
        Assert.That(text, Does.Contain("overflow: hidden"));
        Assert.That(text, Does.Contain("text-overflow: ellipsis"));
        Assert.That(text, Does.Match(@"max-width:\s*\d+px"));
        Assert.That(text, Does.Not.Match(@"white-space:\s*normal"));
    }

    [Test]
    public void Theme_ImportsBaseThemeBeforeProjectStyleSheets()
    {
        // Настоящая причина, почему облако не переносило текст. Базовая тема
        // должна идти раньше проектных листов, иначе .unity-label перебивает
        // правила с тем же весом классового селектора.
        string theme = ReadSource(ThemePath);
        int baseTheme = theme.IndexOf(BaseThemeImport, StringComparison.Ordinal);
        int worldLabels = theme.IndexOf(WorldLabelsImport, StringComparison.Ordinal);

        Assert.That(baseTheme, Is.GreaterThanOrEqualTo(0), $"{BaseThemeImport} is not imported.");
        Assert.That(worldLabels, Is.GreaterThanOrEqualTo(0), $"{WorldLabelsImport} is not imported.");
        Assert.That(
            baseTheme,
            Is.LessThan(worldLabels),
            "The base theme must be imported before project style sheets, otherwise its " +
            ".unity-label rules win over project classes of the same specificity.");
    }

    [Test]
    public void BubbleAnchor_HangsTheTailOverTheRobotsCellTop()
    {
        // Облако вешается за нижний конец хвоста, а не за центр бокса: хвост стоит
        // в 5 px от левого края, поэтому нижний центр увёл бы его в сторону от робота.
        string bubble = ReadSource("Assets/Scripts/UI/Chat/Floating/Presentation/FloatingChatBubble.cs");
        Assert.That(bubble, Does.Contain("CellTopOffset"));
        Assert.That(bubble, Does.Contain("_target.position.y + CellTopOffset"));
        Assert.That(bubble, Does.Not.Contain("TargetOffsetX"));
        Assert.That(bubble, Does.Contain("_labels.Create(WorldLabelKind.ChatBubble)"));
    }

    [Test]
    public void WorldLabels_ScalesTheWholeBubbleAndAnchorsItByTheTail()
    {
        // Облако масштабируется одним множителем через style.scale, а не кеглем
        // и полями по отдельности: иначе на отдалении рамка и хвост разошлись бы с текстом.
        //
        // Якорь по хвосту: translate ведёт нижнюю грань визуального бокса в
        // верхнюю грань клетки, а по горизонтали облако сдвинуто так, чтобы
        // хвост встал в позицию робота.
        string worldLabels = ReadSource(WorldLabelsSource);
        Assert.That(worldLabels, Does.Contain("WorldLabelKind.ChatBubble"));

        // Масштаб и якорь ставятся на рамку: текст и хвост — её дети.
        Assert.That(worldLabels, Does.Contain("Frame.style.scale = new Scale(new Vector3(scale, scale, 1f))"));
        Assert.That(worldLabels, Does.Contain("Frame.style.translate = new Translate(offset.x, offset.y)"));
        Assert.That(worldLabels, Does.Contain("Frame.style.opacity = Mathf.Clamp01(opacity)"));

        // Рамка, текст и хвост — разные элементы.
        Assert.That(worldLabels, Does.Contain("label.AddToClassList(BubbleTextClass)"));
        Assert.That(worldLabels, Does.Contain("frame.Add(label)"));

        Assert.That(worldLabels, Does.Contain("position.x - (TailAnchorX * _lastScale)"));
        Assert.That(worldLabels, Does.Contain("position.y - ((size.y + TailHeight) * _lastScale)"));
        Assert.That(
            worldLabels,
            Does.Contain("Vector3 offset = _kind == WorldLabelKind.ChatBubble"));
        Assert.That(worldLabels, Does.Contain(": position;"));

        // Прежняя схема нижнего центра для облака больше не существует.
        Assert.That(worldLabels, Does.Not.Contain("position.x - (size.x * 0.5f)"));
    }

    [Test]
    public void WorldLabelKind_HasNicknameAndChatBubble()
    {
        // Метка создаётся по виду, а не по bool-флагу: двум состояниям
        // хватало флага, но флаг не различал якорь, из-за чего облако уезжало.
        Assert.That(
            Enum.GetNames(typeof(WorldLabelKind)),
            Is.EquivalentTo(new[] { "Nickname", "ChatBubble" }));
        Assert.That(
            ReadSource("Assets/Scripts/UI/WorldLabels/Contracts/IWorldLabels.cs"),
            Does.Contain("IWorldLabel Create(WorldLabelKind kind)"));
    }

    // Комментарии удаляются, чтобы упоминание свойства в комментарии не выдавалось
    // за правило. USS несёт только /* */, исходники C# — ещё и //.
    private static string StripComments(string source) =>
        Regex.Replace(
            Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//[^\n]*",
            string.Empty);

    private static string ReadSource(string assetPath) =>
        File.ReadAllText(Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length)));

    private static string ReadBubbleRules()
    {
        string rules = ReadSource(BubbleRulesPath);
        int start = rules.IndexOf(BubbleClass, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{BubbleClass} not found in {BubbleRulesPath}.");
        return rules[start..];
    }

    // Тело правил одного класса.
    //
    // Ищется селектор, после которого сразу идёт «{»: иначе подстрока попала бы
    // в общий селектор ".world-label-name, .world-label-chat", где правил облака
    // ещё нет. Комментарии срезаны заранее, чтобы упоминание свойства в
    // комментарии не выдавалось за правило.
    private static string ReadClass(string className)
    {
        string rules = StripComments(ReadSource(BubbleRulesPath));
        int open = -1;
        int from = 0;
        while (true)
        {
            int hit = rules.IndexOf(className, from, StringComparison.Ordinal);
            if (hit < 0)
            {
                break;
            }

            int brace = rules.IndexOf('{', hit);

            // Селектор должен начинаться с начала строки: иначе подстрока попала бы
            // в общий селектор ".world-label-name, .world-label-chat", где правил
            // облака ещё нет, и рамка была бы прочитана как три строки позиционирования.
            bool startsLine = hit == 0 || rules[hit - 1] == '\n';
            bool selectorEndsHere = brace >= 0 && rules[hit..brace].TrimEnd() == className;
            if (startsLine && selectorEndsHere)
            {
                open = brace;
                break;
            }

            from = hit + className.Length;
        }

        Assert.That(open, Is.GreaterThanOrEqualTo(0), $"{className} has no own rule in {BubbleRulesPath}.");
        int close = rules.IndexOf('}', open);
        Assert.That(close, Is.GreaterThan(open), $"{className} has no closed rule body.");
        return rules[(open + 1)..close];
    }
}
