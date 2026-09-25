#nullable enable

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

public sealed class DummyClockContractTests
{
    // Файловый ввод-вывод карты и покадровая подкачка мира не относятся к
    // сценарию: их паузы — повторная попытка открыть файл и бюджет кадра.
    private static readonly string[] _Exempt =
    [
        "Simulation/DummyClock.cs",
        "Simulation/DummyWorldMapArchive.cs",
        "Simulation/DummyWorldSimulationState.cs",
        "Simulation/DummyWorldStreamingCoordinator.cs",
        "Systems/DummyMapStreamer.cs",
    ];

    private static readonly Regex _AmbientTimeOrRandomness = new(
        @"UniTask\.(Delay|Yield)\(|DateTime(Offset)?\.(Utc)?Now|System\.Random|UnityEngine\.Random|new Random\(|Guid\.NewGuid",
        RegexOptions.CultureInvariant);

    [Test]
    public void OfflineServer_TakesTimeAndRandomnessOnlyFromDummyClock()
    {
        string root = Path.Combine(Application.dataPath, "Scripts/Networking/Connection/Client");
        string[] offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(relative => !_Exempt.Contains(relative))
            .Where(relative => _AmbientTimeOrRandomness.IsMatch(File.ReadAllText(Path.Combine(root, relative))))
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "Dummy server code must use IDummyClock for delays, timestamps and randomness.");
    }
}
