#nullable enable

using System;
using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.World;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Размер мировых меток при зуме камеры.
///
/// Ник и облако локального чата живут в панели UI Toolkit, и USS задаёт их
/// кегль в пикселях панели. Панель от зума не зависит — камера меняет только
/// orthographicSize, — поэтому раньше текст оставался прежних 12 px, пока
/// робот на экране сжимался: при отдалении ник и облако «разрастались».
/// Теперь кегль, а с ним поля, рамка, скругление и предел ширины облака,
/// умножаются на ReferenceOrthographicSize / orthographicSize, и метка держит
/// постоянный размер в мире.
///
/// Проверка идёт по настоящему пути: игрок телепортирован в 1315:15, рядом
/// с ним в соседней клетке стоит второй робот с ником, зум крутится колесом
/// мыши через тот же UI/ScrollWheel, который читает CameraFollow, а облако
/// приходит обычным пакетом локального чата в ChatEventGateway. Никаких
/// подставных Label в панели и никаких прямых записей в Camera: иначе тест
/// проверял бы сам себя.
///
/// Ожидаемые числа посчитаны здесь, а не вызваны из production-кода: база
/// 12px — это --size-sm из ThemeTokens.uss, 17.5 — ReferenceOrthographicSize,
/// а якорь облака проверяется по боксу элемента в панели и по проекции камеры,
/// без знания о формуле, которой WorldLabels пишет translate.
/// </summary>
[TestFixture]
public sealed class WorldLabelZoomScalePlayModeTests
{
    private const ushort AnchorX = 1315;
    private const ushort AnchorY = 15;
    private const ushort ProbeX = AnchorX + 1;
    private const ushort ProbeY = AnchorY;
    private const uint ProbeBotId = 9_900_001;
    private const string ProbeNickname = "ZoomProbe";
    private const string ProbeMessage = "привет";

    private const string NicknameClass = "world-label-name";
    private const string ChatClass = "world-label-chat";

    // Базовый кегль 12px взят из ThemeTokens.uss (--size-sm), опорный зум 17.5 —
    // из контрактов камеры. Оба значения продублированы намеренно: оракул
    // проверки не должен считать ожидаемый результат тем же кодом, который
    // его производит.
    private const float ThemeFontSizePx = 12f;
    private const float ReferenceZoom = 17.5f;

    private const float FontSizeTolerancePx = 0.5f;
    private const float PanelAnchorTolerancePx = 1f;
    private const float CellSettleTolerance = 0.01f;
    private const int ZoomFrameBudget = 600;

    // Колесо вниз отдаляет камеру, вверх — приближает: CameraFollow вычитает
    // прокрутку из целевого зума, поэтому вверх — меньший orthographicSize.
    private const int WheelDown = -1;
    private const int WheelUp = 1;

    private ILocalPlayerState _localPlayer = null!;
    private IRobotService _robots = null!;
    private IMapDataProvider _map = null!;
    private ChatEventGateway _chat = null!;
    private Camera _camera = null!;
    private UIDocument _document = null!;
    private VirtualMouse? _mouse;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return PlayModeHarness.StartAtGateway();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);

        _camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _localPlayer = PlayModeHarness.RequireInGame<ILocalPlayerState>();
        _robots = PlayModeHarness.RequireInGame<IRobotService>();
        _map = PlayModeHarness.RequireInGame<IMapDataProvider>();
        _chat = PlayModeHarness.RequireInGame<ChatEventGateway>();
        _document = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame))!;
        Assert.That(_document, Is.Not.Null, "MainGame has no UIDocument.");
        ILocalPlayer player = _localPlayer.Current ??
            throw new AssertionException("The local player was never spawned.");

        _mouse = new VirtualMouse();

        // UpdateServerPosition — тот же вход, которым клиент применяет позицию
        // от сервера. CameraFollow на такой скачок не гонит камеру через всю
        // карту, а ждёт готовности террейна и переставляет её одним кадром,
        // поэтому вид меняется на 1315:15 штатным путём телепорта.
        player.UpdateServerPosition(new Vector2Int(AnchorX, AnchorY));
        Vector3 anchor = CellCenter(AnchorX, AnchorY);
        yield return PlayModeHarness.WaitUntil(
            () => Vector2.Distance(
                (Vector2)_camera.transform.position, (Vector2)anchor) <= 0.05f,
            PlayModeHarness.WorldTimeoutSeconds,
            $"The camera did not reach 1315:15 (it stayed at {_camera.transform.position}).");

        // Второй робот появляется после того, как вид встал на место: иначе он
        // мигал бы в unloaded-чанке и светился бы об ошибках стримера.
        _robots.UpdateRobotMetadata(
            ProbeBotId,
            new RobotMetadata(-1, 0, ProbeNickname, string.Empty, string.Empty));
        _robots.UpdateRobotPosition(ProbeBotId, ProbeX, ProbeY, 0);

        yield return PlayModeHarness.WaitUntil(
            () => RobotSettled(),
            PlayModeHarness.UITimeoutSeconds,
            "The probe robot never settled in cell 1316:15.");
        yield return Settle();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _mouse?.Dispose();
        _mouse = null;
        yield return PlayModeHarness.Shutdown();
    }

    [UnityTest]
    public IEnumerator Nickname_KeepsWorldSizeThroughZoomOutAndBackIn()
    {
        Label? found = null;
        yield return PlayModeHarness.WaitUntil(
            () => (found = FindVisible(NicknameClass, ProbeNickname)) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The probe robot's nameplate never became visible.");
        Label nameplate = found!;

        float startZoom = _camera.orthographicSize;
        float startFont = nameplate.resolvedStyle.fontSize;
        AssertWorldFontSize(startFont, startZoom, "the starting zoom");
        Assert.That(
            nameplate.resolvedStyle.width,
            Is.GreaterThan(0f),
            "The nameplate has no box: nothing was measured.");

        // Отдаление: робот сжимается, и ник обязан сжаться вместе с ним.
        yield return ZoomUntil(zoom => zoom >= startZoom * 1.4f, WheelDown);
        yield return Settle();
        float farZoom = _camera.orthographicSize;
        float farFont = nameplate.resolvedStyle.fontSize;
        AssertWorldFontSize(farFont, farZoom, "the pulled-away camera");
        Assert.That(
            farFont,
            Is.LessThan(startFont - 0.5f),
            "The nickname grew on screen while the camera pulled away: its font size is " +
            "fixed in panel pixels instead of being bound to the zoom.");

        // Приближение: ник растёт вместе с роботом.
        yield return ZoomUntil(zoom => zoom <= farZoom * 0.6f, WheelUp);
        yield return Settle();
        float nearZoom = _camera.orthographicSize;
        float nearFont = nameplate.resolvedStyle.fontSize;
        AssertWorldFontSize(nearFont, nearZoom, "the close-up camera");
        Assert.That(
            nearFont,
            Is.GreaterThan(farFont + 0.5f),
            "The nickname did not grow when the camera came closer.");

        // Отношение не зависит от базового кегля: это и есть постоянство
        // размера в мире при любом зуме.
        Assert.That(
            farFont / nearFont,
            Is.EqualTo(farZoom / nearZoom).Within(0.02f),
            "Nickname font size does not follow the zoom ratio.");
    }

    [UnityTest]
    public IEnumerator ChatBubble_ScalesItsBoxAndKeepsTheCellTopAnchor()
    {
        yield return ShowBubble();
        Label? found = null;
        yield return PlayModeHarness.WaitUntil(
            () => (found = FindVisible(ChatClass, ProbeMessage)) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The local chat bubble never became visible.");
        Label bubble = found!;

        float startZoom = _camera.orthographicSize;
        IResolvedStyle start = bubble.resolvedStyle;
        AssertWorldFontSize(start.fontSize, startZoom, "the starting zoom");
        float startPaddingRatio = start.paddingLeft / start.fontSize;
        Assert.That(start.width, Is.GreaterThan(0f), "The bubble has no box: nothing was measured.");
        AssertAnchorOnCellTop(bubble, "the starting zoom");

        // Приближение: облако растёт вместе с роботом, и его рамка растёт
        // вместе с текстом, а не остаётся прежней.
        yield return ZoomUntil(zoom => zoom <= startZoom * 0.6f, WheelUp);
        yield return ShowBubble();
        yield return PlayModeHarness.WaitUntil(
            () => FindVisible(ChatClass, ProbeMessage) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The bubble did not come back after the zoom.");
        bubble = FindVisible(ChatClass, ProbeMessage)!;

        float nearZoom = _camera.orthographicSize;
        IResolvedStyle near = bubble.resolvedStyle;
        AssertWorldFontSize(near.fontSize, nearZoom, "the close-up camera");
        Assert.That(
            near.fontSize,
            Is.GreaterThan(start.fontSize + 0.5f),
            "The bubble did not grow on zoom in.");
        Assert.That(
            near.paddingLeft / near.fontSize,
            Is.EqualTo(startPaddingRatio).Within(0.01f),
            "The bubble frame did not scale with its text: padding stayed in panel pixels.");
        Assert.That(
            near.borderLeftWidth,
            Is.GreaterThan(start.borderLeftWidth + 0.05f),
            "The bubble border did not scale with the zoom.");
        AssertAnchorOnCellTop(bubble, "the close-up camera");

        // Отдаление: облако уменьшается, якорь остаётся на верхней грани клетки.
        yield return ZoomUntil(zoom => zoom >= nearZoom * 1.5f, WheelDown);
        yield return ShowBubble();
        yield return PlayModeHarness.WaitUntil(
            () => FindVisible(ChatClass, ProbeMessage) != null,
            PlayModeHarness.UITimeoutSeconds,
            "The bubble did not come back after the second zoom.");
        bubble = FindVisible(ChatClass, ProbeMessage)!;

        float farZoom = _camera.orthographicSize;
        IResolvedStyle far = bubble.resolvedStyle;
        AssertWorldFontSize(far.fontSize, farZoom, "the pulled-away camera");
        Assert.That(
            far.fontSize,
            Is.LessThan(near.fontSize - 0.5f),
            "The bubble did not shrink on zoom out.");
        AssertAnchorOnCellTop(bubble, "the pulled-away camera");
    }

    // Проверка кегля против опорного зума: USS задаёт базу 12px на панель, и
    // единственное, что обязано её менять, — масштаб зума.
    private static void AssertWorldFontSize(float measuredFontSize, float zoom, string phase)
    {
        float expected = ThemeFontSizePx * (ReferenceZoom / zoom);
        Assert.That(
            measuredFontSize,
            Is.EqualTo(expected).Within(FontSizeTolerancePx),
            $"Font size at {phase}: orthographicSize={zoom:F3} must render {expected:F2}px " +
            $"({ThemeFontSizePx}px world base at zoom {ReferenceZoom}), measured {measuredFontSize:F2}px.");
    }

    // Нижний центр облака обязан попадать в верхнюю грань клетки робота.
    // Оракул — бокс элемента в панели и проекция той же точки через камеру,
    // то есть ровно то, что видит игрок; формула переноса метки не используется.
    private void AssertAnchorOnCellTop(Label bubble, string phase)
    {
        Assert.That(bubble.panel, Is.Not.Null, $"The bubble has no panel at {phase}.");
        Vector3 cellTop = CellCenter(ProbeX, ProbeY) + new Vector3(0f, 0.5f, 0f);
        Vector2 expected = RuntimePanelUtils.CameraTransformWorldToPanel(
            bubble.panel, cellTop, _camera);
        Rect box = bubble.worldBound;
        Vector2 bottomCenter = new(box.center.x, box.yMin);
        Assert.That(
            Vector2.Distance(bottomCenter, expected),
            Is.LessThan(PanelAnchorTolerancePx),
            $"At {phase} the bubble's bottom centre is {bottomCenter}, but cell top 1316:15 is " +
            $"at {expected}: the anchor was not recomputed for the new box height " +
            $"(zoom={_camera.orthographicSize:F3}, box={box.size}).");
    }

    private IEnumerator ShowBubble()
    {
        // Обычный путь сообщения: тот же пакет, что приходит с сервера, и тот же
        // gateway, из которого его публикует ChatProcessor. Пузырь живёт три
        // секунды, поэтому на каждый замер приходит свежий: заодно проверяется
        // переиспользование пузыря из пула с уже переопределённым кеглем.
        _chat.Publish(new LocalChatMessagePacket(ProbeBotId, ProbeX, ProbeY, ProbeMessage));
        yield return Settle();
    }

    private IEnumerator ZoomUntil(Func<float, bool> reached, int wheel)
    {
        for (int frame = 0; frame < ZoomFrameBudget; frame++)
        {
            if (reached(_camera.orthographicSize))
            {
                yield break;
            }

            _mouse!.Scroll(wheel);
            yield return Pump(1);
        }

        Assert.Fail(
            $"The camera zoom did not reach the target in {ZoomFrameBudget} frames: " +
            $"orthographicSize={_camera.orthographicSize:F3}.");
    }

    private IEnumerator Pump(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            // Сервер присылает позицию каждый тик; без неё prune снёс бы робота
            // через 2.5 с молчания, и ник пропал бы из панели.
            _robots.UpdateRobotPosition(ProbeBotId, ProbeX, ProbeY, 0);
            yield return null;
        }
    }

    // Замер делается только на устоявшемся зуме: CameraFollow сглаживает
    // orthographicSize к целевому значению, и замер на промежуточном кадре
    // сравнивал бы кегль с одним зумом, а фон облака с другим. Плюс два
    // кадра на раскладку панели: кегль переписывает бокс, и только на
    // следующем кадре якорь облака встаёт на новое место.
    private IEnumerator Settle()
    {
        float previous = _camera.orthographicSize;
        for (int frame = 0; frame < ZoomFrameBudget; frame++)
        {
            yield return Pump(1);
            float current = _camera.orthographicSize;
            if (Mathf.Approximately(current, previous))
            {
                yield return Pump(2);
                yield break;
            }

            previous = current;
        }

        Assert.Fail(
            $"The camera zoom never settled: orthographicSize={_camera.orthographicSize:F3}.");
    }

    private bool RobotSettled()
    {
        if (!_robots.TryGetRobot(ProbeBotId, out IRobotView? robot) || robot == null)
        {
            return false;
        }

        Vector3 cell = CellCenter(ProbeX, ProbeY);
        return Vector2.Distance(
            (Vector2)robot.transform.position, (Vector2)cell) <= CellSettleTolerance;
    }

    private Vector3 CellCenter(ushort x, ushort y) =>
        CoordinateUtils.ServerToUnityPos(x, y, _map.WorldHeight);

    private Label? FindVisible(string className, string text)
    {
        foreach (Label label in _document.rootVisualElement
                     .Query<Label>(className: className).ToList())
        {
            if (label.text == text && label.resolvedStyle.visibility == Visibility.Visible)
            {
                return label;
            }
        }

        return null;
    }
}

/// <summary>
/// Виртуальная мышь теста: колесо крутит тест, а не разработчика, и устройство
/// удаляется после прогона. Без неё зум не изменить никак — CameraFollow
/// читает только ввод, и прямой записи ортографического размера в нём нет.
/// </summary>
internal sealed class VirtualMouse : IDisposable
{
    private readonly Mouse _device;

    public VirtualMouse()
    {
        _device = InputSystem.AddDevice<Mouse>("KernTestMouse");
        _device.MakeCurrent();
    }

    public void Scroll(int direction) =>
        InputSystem.QueueStateEvent(_device, new MouseState { scroll = new Vector2(0f, direction) });

    public void Dispose() => InputSystem.RemoveDevice(_device);
}
