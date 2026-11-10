#nullable enable

using System.Collections;
using System.Text;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using Kern.Rendering.PostProcessing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

// Террейн на экране: настоящий мир на заглушке сервера, настоящий кадр.
//
// Проверка по снимку экрана, а не по состоянию объектов: состояние бывает
// «опубликовано, включено, меш на месте», а кадр чёрный. Снимки берутся
// трижды — как есть, без отрисовки террейна и без расчёта света, — и
// разница между ними говорит, что именно пропало: террейн с экрана или свет
// с террейна. При падении в сообщение идёт состояние окна, меша и камеры.
[TestFixture]
[Category("GPU")]
public sealed class TerrainVisibilityPlayModeTests
{
    private const string TestDummyToken = "playmode-terrain-visibility-token";
    private const int SettleFrames = 120;

    // Пиксель «не чёрный», если его яркость выше этой.
    private const float LitLuminance = 0.02f;

    // Террейн занимает большую часть кадра вокруг игрока.
    private const float MinimumLitFraction = 0.25f;

    private DummyAuthenticationScope _authentication = null!;
    private bool _runInBackground;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _runInBackground = Application.runInBackground;
        Application.runInBackground = true;
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtMainMenu();
        yield return PlayModeHarness.EnterMainGame(PlayModeHarness.FindBootstrap()!);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
        Application.runInBackground = _runInBackground;
    }

    [UnityTest]
    [Timeout(180_000)]
    public IEnumerator MainGame_TerrainIsVisibleOnScreen()
    {
        Scene game = PlayModeHarness.Scene(SceneNames.MainGame);
        TerrainRenderer terrain = PlayModeHarness.FindComponentInScene<TerrainRenderer>(game)
            ?? throw new AssertionException("MainGame has no TerrainRenderer.");
        yield return PlayModeHarness.WaitUntil(
            () => terrain.IsReadyForGameplay,
            PlayModeHarness.WorldTimeoutSeconds,
            "Terrain never became ready.");
        yield return PlayModeHarness.Frames(SettleFrames);

        IRuntimeDebugSettings debug = PlayModeHarness.RequireInGame<IRuntimeDebugSettings>();
        Assert.That(debug.BypassTerrainDraw, Is.False, "Terrain draw is bypassed by debug settings.");
        Assert.That(debug.BypassLightingCompute, Is.False, "Lighting compute is bypassed by debug settings.");

        var full = new ScreenStats();
        yield return Capture(full);

        debug.BypassTerrainDraw = true;
        yield return PlayModeHarness.Frames(10);
        var withoutTerrain = new ScreenStats();
        yield return Capture(withoutTerrain);
        debug.BypassTerrainDraw = false;

        debug.BypassLightingCompute = true;
        yield return PlayModeHarness.Frames(10);
        var withoutLighting = new ScreenStats();
        yield return Capture(withoutLighting);
        debug.BypassLightingCompute = false;

        Camera camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        string report = Describe(terrain, camera, full, withoutTerrain, withoutLighting);
        Debug.Log("[TerrainVisibilityTest]\n" + report);
        Assert.That(full.LitFraction, Is.GreaterThanOrEqualTo(MinimumLitFraction), report);
    }

    private sealed class ScreenStats
    {
        public float LitFraction { get; set; }

        public float MeanLuminance { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    private static IEnumerator Capture(ScreenStats stats)
    {
        Texture2D texture;
        if (Application.isBatchMode)
        {
            Camera camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
            RenderTexture target = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight,
                24, RenderTextureFormat.ARGBHalf);
            Camera? previousCamera = PostProcessRuntimeState.DiagnosticOffscreenCamera;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest
                {
                    destination = target,
                });
                texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
            }
            finally
            {
                RenderTexture.active = previousTarget;
                PostProcessRuntimeState.DiagnosticOffscreenCamera = previousCamera;
                RenderTexture.ReleaseTemporary(target);
            }
        }
        else
        {
            yield return new WaitForEndOfFrame();
            texture = ScreenCapture.CaptureScreenshotAsTexture();
        }
        try
        {
            Color32[] pixels = texture.GetPixels32();
            int lit = 0;
            double sum = 0.0;
            foreach (Color32 pixel in pixels)
            {
                float luminance = ((0.2126f * pixel.r) + (0.7152f * pixel.g) + (0.0722f * pixel.b)) / 255f;
                sum += luminance;
                if (luminance > LitLuminance)
                {
                    lit++;
                }
            }

            stats.Width = texture.width;
            stats.Height = texture.height;
            stats.LitFraction = pixels.Length > 0 ? (float)lit / pixels.Length : 0f;
            stats.MeanLuminance = pixels.Length > 0 ? (float)(sum / pixels.Length) : 0f;
        }
        finally
        {
            Object.Destroy(texture);
        }
    }

    private static string Describe(
        TerrainRenderer terrain,
        Camera camera,
        ScreenStats full,
        ScreenStats withoutTerrain,
        ScreenStats withoutLighting)
    {
        var text = new StringBuilder(2048);
        AppendStats(text, "кадр", full);
        AppendStats(text, "без террейна", withoutTerrain);
        AppendStats(text, "без расчёта света", withoutLighting);

        MeshRenderer renderer = terrain.GetComponent<MeshRenderer>();
        MeshFilter filter = terrain.GetComponent<MeshFilter>();
        Mesh? mesh = filter != null ? filter.sharedMesh : null;
        text.Append("террейн: опубликован ").Append(terrain.HasPublishedTerrain)
            .Append(", renderer.enabled ").Append(renderer != null && renderer.enabled)
            .Append(", isVisible ").Append(renderer != null && renderer.isVisible)
            .Append(", слой ").Append(renderer != null ? renderer.sortingLayerName : "-")
            .Append('/').Append(renderer != null ? renderer.sortingOrder : 0).AppendLine();
        text.Append("меш: ").Append(mesh != null ? mesh.name : "нет")
            .Append(", вершин ").Append(mesh != null ? mesh.vertexCount : 0)
            .Append(", bounds ").Append(renderer != null ? renderer.bounds.ToString() : "-").AppendLine();
        if (renderer != null)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                text.Append("материал: ").Append(material != null ? material.shader.name : "нет")
                    .Append(material != null && !material.shader.isSupported ? " (НЕ ПОДДЕРЖАН)" : string.Empty)
                    .AppendLine();
            }
        }

        text.Append("transform ").Append(terrain.transform.position).AppendLine();
        text.Append("камера ").Append(camera.transform.position)
            .Append(", ortho ").Append(camera.orthographicSize)
            .Append(", cullingMask включает слой террейна ")
            .Append((camera.cullingMask & (1 << terrain.gameObject.layer)) != 0).AppendLine();

        AppendCellsUnderCamera(text, camera);
        text.Append("_TerrainCellOrigin ").Append(Shader.GetGlobalVector("_TerrainCellOrigin"))
            .Append(", _TerrainCellViewOffset ").Append(Shader.GetGlobalVector("_TerrainCellViewOffset"))
            .Append(", _TerrainCellGridSize ").Append(Shader.GetGlobalVector("_TerrainCellGridSize"))
            .AppendLine();
        return text.ToString();
    }

    // Что лежит в мире под кадром: пустые клетки дают ровно чёрный кадр
    // при исправном рендере.
    private static void AppendCellsUnderCamera(StringBuilder text, Camera? camera)
    {
        IWorldDataStorage? storage = PlayModeHarness.ResolveInGame<IWorldDataStorage>();
        IMapDataProvider? map = PlayModeHarness.ResolveInGame<IMapDataProvider>();
        if (camera == null || storage == null || map == null)
        {
            text.AppendLine("клетки: нет хранилища или карты");
            return;
        }

        var counts = new System.Collections.Generic.Dictionary<string, int>();
        int missing = 0;
        int centerX = Mathf.FloorToInt(camera.transform.position.x);
        int centerY = Mathf.FloorToInt(camera.transform.position.y);
        for (int dy = -12; dy <= 12; dy++)
        {
            for (int dx = -20; dx <= 20; dx++)
            {
                int x = centerX + dx;
                int serverY = WorldCoordinateConverter.UnityToServerY(centerY + dy, map.WorldHeight);
                if (!storage.TryGetCell(x, serverY, out MinesServer.Data.CellType type))
                {
                    missing++;
                    continue;
                }

                string key = type.ToString();
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        text.Append("клетки под камерой 41×25: нет данных ").Append(missing);
        foreach (var entry in System.Linq.Enumerable.OrderByDescending(counts, pair => pair.Value))
        {
            text.Append(", ").Append(entry.Key).Append(' ').Append(entry.Value);
        }

        text.AppendLine();
    }

    private static void AppendStats(StringBuilder text, string label, ScreenStats stats)
    {
        text.Append(label).Append(": не чёрных ").Append((stats.LitFraction * 100f).ToString("F1"))
            .Append("%, средняя яркость ").Append(stats.MeanLuminance.ToString("F3"))
            .Append(", ").Append(stats.Width).Append('×').Append(stats.Height).AppendLine();
    }
}
