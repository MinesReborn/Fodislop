#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

/// <summary>
/// Контракт облака локального чата.
///
/// Рамка рисуется спрайтом LocalChatBubble с 9-slice, а не стилями: спрайт
/// назначается кодом вместе с ScaleMode.ScaleToFit, иначе он ужимается в бокс
/// целиком, хвост превращается в полосу, а текст оказывается под ней. Поэтому
/// в USS у облака нет ни подложки-цвета, ни border-*: любая из них перекрыла бы
/// спрайт и рамка разошлась бы с текстом.
///
/// Границы 9-slice лежат в ассете (spriteBorder в импортере) и не дублируются
/// ни в USS, ни в C#. Это проверяется здесь и в PlayMode-тесте, который
/// сверяет resolvedStyle с sprite.border.
///
/// Числа раскладки проверяет WorldLabelChatLayoutPlayModeTests: в EditMode
/// resolvedStyle нельзя читать до прохода стилей, поэтому здесь только
/// контракт исходников — то, что не должно тихо поехать при правках.
///
/// Отдельно охраняется порядок импортов в KernTheme: при равной специфичности
/// выигрывает объявленное позже, и .unity-label из базовой темы перебивал
/// white-space у .world-label-chat. Облако не переносило текст и не росло.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatStyleTests
{
    private const string BubbleRulesPath = "Assets/Resources/UI/WorldLabels/Styles/WorldLabels.uss";
    private const string BubbleClass = ".world-label-chat";
    private const string BubbleTextClass = ".world-label-chat-text";
    private const string ThemePath = "Assets/UI/Toolkit/Themes/Kern/KernTheme.tss";
    private const string BaseThemeImport = "unity-theme://default";
    private const string WorldLabelsImport = "UI/WorldLabels/Styles/WorldLabels.uss";
    private const string WorldLabelsSource = "Assets/Scripts/UI/WorldLabels/Presentation/WorldLabels.cs";

    [Test]
    public void BubbleStyle_HasNoCssFrameUnderTheBubbleSprite()
    {
        // Рамку рисует спрайт. Подложка-цвет или контур из темы встали бы
        // поверх него, и облако снова стало бы боксом темы, а не спрайтом.
        //
        // Проверяется тело правил рамки, а не весь файл: свойства текста
        // (background-image-tint-color) не должны ловиться подстрокой.
        string frame = ReadClass(BubbleClass);
        Assert.That(frame, Does.Not.Contain("background-color"));
        Assert.That(frame, Does.Not.Contain("background-image:"));
        Assert.That(frame, Does.Not.Contain("border-color"));
        Assert.That(frame, Does.Not.Contain("border-width"));
        Assert.That(frame, Does.Not.Contain("border-radius"));
    }

    [Test]
    public void BubbleStyle_DoesNotHardcodeSliceBorders()
    {
        // Границы 9-slice принадлежат ассету. Повторённые в USS числа
        // разъедутся с ним при первом же перерисовывании хвоста — молча,
        // потому что спрайт продолжит рисоваться, но уже неправильно.
        string rules = StripComments(ReadBubbleRules());
        Assert.That(rules, Does.Not.Contain("-unity-slice-"));
        Assert.That(rules, Does.Not.Contain("-unity-background-scale-mode"));

        // В коде границы обязаны быть, но читаться из sprite.border: без них
        // ScaleToFit вписывает весь спрайт в бокс с сохранением пропорций, и
        // облако растягивается по вертикали, оставаясь узким по горизонтали.
        string worldLabels = ReadSource(WorldLabelsSource);
        Assert.That(worldLabels, Does.Contain("frame.style.unitySliceLeft = (int)border.x"));
        Assert.That(worldLabels, Does.Contain("frame.style.unitySliceBottom = (int)border.y"));
        Assert.That(worldLabels, Does.Contain("frame.style.unitySliceRight = (int)border.z"));
        Assert.That(worldLabels, Does.Contain("frame.style.unitySliceTop = (int)border.w"));
        Assert.That(worldLabels, Does.Contain("Vector4 border = sprite.border;"));

        // Слайсы и размер фона ставятся на рамку, а не на текст: у элемента
        // с текстом рамка растягивалась бы по переносу, а высота облака постоянна.
        //
        // Размер фона обязателен и задаётся 100% 100%: без него спрайт вписывается
        // в бокс целиком, и 9-slice не растягивает ни одну ось. Устаревший
        // unityBackgroundScaleMode в Unity 6 игнорируется, поэтому ScaleToFit
        // больше не годится.
        Assert.That(
            worldLabels,
            Does.Contain("frame.style.backgroundSize = new BackgroundSize("));
        Assert.That(worldLabels, Does.Contain("Length.Percent(100f)"));
        // Упоминание устаревшего свойства остаётся только в комментарии, где объяснена
        // причина. В коде его быть не должно: Unity 6 его игнорирует.
        Assert.That(StripComments(worldLabels), Does.Not.Contain("unityBackgroundScaleMode"));
        Assert.That(worldLabels, Does.Contain("private static void ApplyBubbleFrame(VisualElement frame)"));
    }

    [Test]
    public void BubbleStyle_UsesDarkTextOnTheWhiteSprite()
    {
        // Заливка спрайта белая (RGB 255,255,255), поэтому светлый
        // --text-primary на ней не читался. Текст задан отдельному правилу, а не
        // рамке: иначе он уехал бы вложенному Label.
        string text = ReadClass(BubbleTextClass);
        Assert.That(text, Does.Contain("color: var(--text-on-gold)"));
        Assert.That(text, Does.Not.Contain("color: var(--text-primary)"));
        Assert.That(text, Does.Not.Contain("color: black"));
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
    public void BubbleFrame_HasFixedSpriteHeightAndNoPadding()
    {
        // Высота рамки равна высоте спрайта (32 px). При ней вертикальный
        // 9-slice не растягивается: 7 px сверху и 22 px снизу берутся из ассета,
        // а полоса между ними совпадает с исходными 3 px, и хвост держит форму.
        // Любая другая высота либо растянет хвост, либо срежет его.
        //
        // Поля на рамке отсутствуют намеренно: отступ создаёт сама рамка, а в
        // USS-полях на элементе с текстом рассинхрон с хвостом проявлялся как
        // лишние 6 px снизу (в play mode 21 px вместо 15 при коэффициенте панели
        // 0.711462).
        string frame = ReadClass(BubbleClass);
        Assert.That(frame, Does.Match(@"height:\s*32px"));
        Assert.That(frame, Does.Not.Contain("padding"));
    }

    [Test]
    public void BubbleFrame_TintIsSetInCodeNotInUss()
    {
        // LocalChat.prefab: m_Color = {1,1,1, a=0.6156863} — белый тон с альфой
        // 157/255. Тон задаётся стилем, а не альфой в ассете, чтобы PNG оставался
        // белым и переиспользуемым.
        //
        // Именно в коде, а не в USS: background-image назначается кодом, и
        // правило unity-background-image-tint-color без фона в USS не
        // разрешалось — замерено в play mode, рамка оставалась RGBA(1,1,1,1).
        string worldLabels = ReadSource(WorldLabelsSource);
        Assert.That(
            worldLabels,
            Does.Contain("new(1f, 1f, 1f, 0.6156863f)"),
            "The frame tint must keep the original client's alpha of 157/255.");
        Assert.That(
            worldLabels,
            Does.Contain("frame.style.unityBackgroundImageTintColor = FrameTint"));
        Assert.That(
            StripComments(ReadClass(BubbleClass)),
            Does.Not.Contain("unity-background-image-tint-color"),
            "A tint in USS does not resolve while background-image comes from code.");
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
    public void BubbleText_SitsAboveTheTail()
    {
        // Тело спрайта занимает верхние 18 px из 32, хвост — оставшиеся 14 px
        // снизу. Текст ограничен телом, иначе он наезжал бы на хвост.
        string text = ReadClass(BubbleTextClass);
        Assert.That(text, Does.Match(@"height:\s*18px"));

        // Базовая тема unity-theme://default задаёт Label поля margin-top и
        // margin-bottom. Без их обнуления текст центрировался по боксу целиком
        // вместо тела и уезжал под хвост (замерено: marginTop 4.27 px в боксе
        // 32 px при теле спрайта 18 px).
        Assert.That(
            text,
            Does.Match(@"margin:\s*0"),
            "The base theme's Label margins must be reset or the text slides under the tail.");
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
    public void BubbleSprite_ComesFromTheAssetWithItsOwnSlices()
    {
        // Спрайт грузится как Sprite, а не как Texture2D: только у спрайта есть
        // border, и именно из него UI Toolkit берёт границы 9-slice. Загрузка
        // текстуры дала бы спрайт без рамки — это и был прежний дефект.
        //
        // Путь лежит в ResourcePaths рядом с остальными ресурсами UI, а сами
        // границы в коде не повторяются: единственный источник — ассет.
        string worldLabels = ReadSource(WorldLabelsSource);
        Assert.That(worldLabels, Does.Contain("Resources.Load<Sprite>"));
        Assert.That(worldLabels, Does.Contain("ResourcePaths.LocalChatBubbleSprite"));
        Assert.That(
            ReadSource("Assets/Scripts/Core/Resources/Contracts/Paths/ResourcePaths.cs"),
            Does.Contain("LocalChatBubbleSprite = \"UI/Chat/Floating/Sprites/LocalChatBubble\""));
    }

    [Test]
    public void BubbleAnchor_HangsTheTailOverTheRobotsCellTop()
    {
        // Облако вешается за точку хвоста, а не за центр бокса: хвост заморожен
        // в нерастяжимом левом тайле 9-slice и всегда стоит в 7..9 px от левого
        // края, поэтому нижний центр увёл бы хвост в сторону от робота.
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
        // и полями по отдельности: тайлы 9-slice не масштабируются, и поля,
        // посчитанные отдельно от рамки, на отдалении стали бы короче хвоста.
        //
        // Якорь по хвосту: translate ведёт нижнюю грань визуального бокса в
        // верхнюю грань клетки, а по горизонтали облако сдвинуто так, чтобы
        // хвост встал в позицию робота.
        string worldLabels = ReadSource(WorldLabelsSource);
        Assert.That(worldLabels, Does.Contain("WorldLabelKind.ChatBubble"));

        // Масштаб и якорь ставятся на рамку: у неё спрайт и постоянная высота.
        Assert.That(worldLabels, Does.Contain("Frame.style.scale = new Scale(new Vector3(scale, scale, 1f))"));
        Assert.That(worldLabels, Does.Contain("Frame.style.translate = new Translate(offset.x, offset.y)"));
        Assert.That(worldLabels, Does.Contain("Frame.style.opacity = Mathf.Clamp01(opacity)"));

        // Рамка и текст — разные элементы, иначе высота не может быть постоянной.
        Assert.That(worldLabels, Does.Contain("label.AddToClassList(BubbleTextClass)"));
        Assert.That(worldLabels, Does.Contain("frame.Add(label)"));

        Assert.That(worldLabels, Does.Contain("position.x - (TailAnchorSourceX * _lastScale)"));
        Assert.That(worldLabels, Does.Contain("position.y - (size.y * _lastScale)"));
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
            Is.EquivalentTo(new[] { "Nickname", "Collection", "ChatBubble" }));
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
