#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

// Фриз в игре: настоящий мир на заглушке сервера, игрок стоит, потом ходит.
//
// Провис — по общему правилу FrameBudget относительно медианы прогона.
// Тест пишет только время кадров: включение множества маркеров профайлера
// внутри самого измерения меняет измеряемую нагрузку.
[TestFixture]
[Category("Performance")]
public sealed class FrameStallPlayModeTests
{
    private const string TestDummyToken = "playmode-frame-stall-token";
    private const int SettleFrames = 180;
    private const int IdleFrames = 300;
    private const int WalkFramesPerKey = 150;
    private const int WalkRounds = 2;
    private const int MeasuredFrames = IdleFrames + (WalkFramesPerKey * 2 * WalkRounds);

    private static readonly Key[] s_walkKeys = [Key.D, Key.A];

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private VirtualKeyboard? _keyboard;
    private bool _runInBackground;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // Иначе окно без фокуса пропускает кадры, и прогон меряет паузы
        // окна, а не игру.
        _runInBackground = Application.runInBackground;
        Application.runInBackground = true;
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _keyboard?.Dispose();
        _keyboard = null;
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
        Application.runInBackground = _runInBackground;
    }

    [UnityTest]
    [Timeout(300_000)]
    public IEnumerator MainGame_StandingAndWalking_HasNoFrameStalls()
    {
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        TerrainRenderer terrain = PlayModeHarness.FindComponentInScene<TerrainRenderer>(
            PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame))
            ?? throw new AssertionException("MainGame has no TerrainRenderer.");
        yield return PlayModeHarness.WaitUntil(
            () => terrain.IsReadyForGameplay,
            PlayModeHarness.WorldTimeoutSeconds,
            "Terrain never became ready.");
        yield return PlayModeHarness.Frames(SettleFrames);

        _keyboard = new VirtualKeyboard();
        var frameMs = new List<double>(MeasuredFrames);
        var phase = new List<string>(MeasuredFrames);
        // Не запускать рекордеры здесь: их массовое создание само создаёт
        // длинный кадр и портит измерение.
        yield return PlayModeHarness.Frames(10);
        for (int frame = 0; frame < IdleFrames; frame++)
        {
            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000.0);
            phase.Add("стоит");
        }

        for (int round = 0; round < WalkRounds; round++)
        {
            foreach (Key key in s_walkKeys)
            {
                _keyboard.Hold(key);
                for (int frame = 0; frame < WalkFramesPerKey; frame++)
                {
                    yield return null;
                    frameMs.Add(Time.unscaledDeltaTime * 1000.0);
                    phase.Add("идёт");
                }

                _keyboard.Release(key);
            }
        }

        string report = Analyze(frameMs, phase, out int stalls);
        DiagnosticReport.Write("Performance", "frame_stall_test", "Провисы в тесте", report);
        Debug.Log($"[FrameStallTest]\n{report}");
        Assert.That(stalls, Is.Zero, report);
    }

    private static string Analyze(
        List<double> frameMs,
        List<string> phase,
        out int stalls)
    {
        int frames = frameMs.Count;
        double median = Median(frameMs);
        var stallFrames = new List<int>();
        for (int index = 0; index < frames; index++)
        {
            if (FrameBudget.IsStall(frameMs[index], median))
            {
                stallFrames.Add(index);
            }
        }

        stalls = stallFrames.Count;
        var text = new StringBuilder(8192);
        text.Append("Кадров ").Append(frames).Append(", медиана ").Append(median.ToString("F2"))
            .Append(" мс, провисов ").Append(stalls).AppendLine();
        foreach (int index in stallFrames)
        {
            text.Append("  кадр ").Append(index).Append(" (").Append(phase[index]).Append("): ")
                .Append(frameMs[index].ToString("F1")).AppendLine(" мс");
        }

        return text.ToString();
    }

    private static double Median(IReadOnlyList<double> source)
    {
        double[] sorted = new double[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            sorted[index] = source[index];
        }

        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
