#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

using Kern.AssetPipeline.Loading;
using Kern.Game.Player.Cameras;
using Kern.Diagnostics.Artifacts;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class SurfaceScenePlayModeTests
{
    private DummyAuthenticationScope _authentication = null!;
    private bool _runInBackground;
    private IClientConfigManager? _config;
    private bool _originalDummyConnection;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _runInBackground = Application.runInBackground;
        Application.runInBackground = true;
        _authentication = DummyAuthenticationScope.Seed("surface-scene-production-token");
        yield return PlayModeHarness.StartAtMainMenu();
        Debug.Log("[SurfaceSceneProof] MainMenu ready.");
        _config = PlayModeHarness.FindBootstrap()!.Container.Resolve<IClientConfigManager>();
        _originalDummyConnection = _config.Config.Connection.UseDummyConnection;
        _config.UpdateSection(config => config.Connection, settings => settings.UseDummyConnection = true);
        yield return PlayModeHarness.EnterMainGame(PlayModeHarness.FindBootstrap()!);
        Debug.Log("[SurfaceSceneProof] MainGame ready.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_config != null)
        {
            _config.UpdateSection(config => config.Connection, settings => settings.UseDummyConnection = _originalDummyConnection);
        }
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
        Application.runInBackground = _runInBackground;
    }

    [UnityTest]
    [Timeout(600_000)]
    public IEnumerator Surface_ProductionCompositionCaptures_AndStableLightingRevision()
    {
        MapManager map = PlayModeHarness.RequireInGame<MapManager>();
        SurfaceRenderer surface = PlayModeHarness.RequireInGame<SurfaceRenderer>();
        Camera gameplay = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        CameraFollow follow = PlayModeHarness.RequireInGame<CameraFollow>();
        yield return PlayModeHarness.WaitUntil(() => map.IsWorldInitialized && surface.IsInitialized,
            PlayModeHarness.WorldTimeoutSeconds, "Surface scene initialization failed.");
        Debug.Log("[SurfaceSceneProof] Surface initialized.");
        bool followEnabled = follow.enabled;
        float previousTimeScale = Time.timeScale;
        float previousAspect = gameplay.aspect;
        Time.timeScale = 0f;
        follow.enabled = false;
        using var diagnostic = new FrameBenchmarkPlayModeTests.DiagnosticCameraScope(gameplay,
            PlayModeHarness.RequireInGame<ISceneObjectFactory>());
        Camera camera = diagnostic.Camera;
        Assert.That(surface.transform.Find("SurfaceScene"), Is.Null,
            "The restored gameplay surface must not contain decorative pseudo-terrain.");
        MeshRenderer[] legacy = new[]
            {
                SurfaceRenderer.HorizonObjectName,
                SurfaceRenderer.PerspectiveObjectName,
                SurfaceRenderer.TransitObjectName,
            }
            .Select(name => surface.transform.Find(name).GetComponent<MeshRenderer>()).ToArray();
        string directory = DiagnosticArtifactPaths.GetDirectory("surface-scene");
        var report = new List<string>();
        var gpuRecorder = Recorder.Get("RenderLoop.Draw");
        gpuRecorder.enabled = true;
        var tickRecorder = Recorder.Get("Kern.SurfaceScene.Tick");
        tickRecorder.enabled = true;
        try
        {
            camera.orthographicSize = 30f;
            camera.transform.position = new Vector3(map.WorldWidth * 0.5f, map.WorldHeight - 3f, gameplay.transform.position.z);
            diagnostic.ApplyPoseToGameplay();
            foreach (MeshRenderer renderer in legacy) { renderer.enabled = true; }
            yield return Capture("baseline", 1280, 800, false);
            var before = new List<double>();
            yield return Measure(before);
            foreach (MeshRenderer renderer in legacy)
            {
                Assert.That(renderer.enabled, Is.True, "All original surface layers must remain enabled.");
            }
            yield return Capture("surface", 1280, 800, true);
            var after = new List<double>();
            yield return Measure(after);
            report.Add($"GPU RenderLoop.Draw: before samples={before.Count}, after samples={after.Count}");
            if (before.Count > 0 && after.Count > 0)
            {
                before.Sort();
                after.Sort();
                report.Add($"GPU median: before={before[before.Count / 2]:F4} ms; after={after[after.Count / 2]:F4} ms");
            }
            else
            {
                report.Add("GPU timings unavailable on this batch device; GPU performance result unverified.");
            }

            ulong revision = surface.LightingGeometryRevision;
            yield return PlayModeHarness.Frames(30);
            Assert.That(surface.LightingGeometryRevision, Is.EqualTo(revision));
            report.Add($"Stable frames: rebuilds=0; lighting revision delta=0; tick CPU={tickRecorder.elapsedNanoseconds * 1e-6:F4} ms");
            Mesh horizonMesh = legacy.Single(renderer => renderer.gameObject.name == SurfaceRenderer.HorizonObjectName)
                .GetComponent<MeshFilter>().sharedMesh;
            Assert.That(horizonMesh.vertexCount, Is.EqualTo(4), "The original horizon geometry must be preserved.");
            report.Add($"Original three surface objects; original horizon triangles={horizonMesh.triangles.Length / 3}; no separate SurfaceScene root.");
            yield return VerifyEntityVisibility();
            yield return Capture("wide", 1920, 800, true);
            camera.transform.position += new Vector3(33f, 0f, 0f);
            diagnostic.ApplyPoseToGameplay();
            yield return Capture("segment-crossing", 1280, 800, true);
            camera.orthographicSize = 10f;
            camera.transform.position = new Vector3(map.WorldWidth * 0.5f, map.WorldHeight + 4f, gameplay.transform.position.z);
            diagnostic.ApplyPoseToGameplay();
            yield return Capture("near", 1280, 800, true);
            camera.orthographicSize = 30f;
            camera.transform.position = new Vector3(0.5f, map.WorldHeight - 3f, gameplay.transform.position.z);
            diagnostic.ApplyPoseToGameplay();
            yield return Capture("world-edge", 1280, 800, true);
            camera.transform.position = new Vector3(map.WorldWidth * 0.5f, map.WorldHeight + 60f, gameplay.transform.position.z);
            diagnostic.ApplyPoseToGameplay();
            yield return Capture("above", 1280, 800, false);
            camera.transform.position = new Vector3(map.WorldWidth * 0.5f, map.WorldHeight - 35f, gameplay.transform.position.z);
            diagnostic.ApplyPoseToGameplay();
            yield return Capture("underground", 1280, 800, false);
            report.Add($"Unity={Application.unityVersion}; GPU={SystemInfo.graphicsDeviceName}; API={SystemInfo.graphicsDeviceType}");
            File.WriteAllLines(Path.Combine(directory, "report.txt"), report);
            Debug.Log("[SurfaceSceneProof] " + string.Join("; ", report));
        }
        finally
        {
            gpuRecorder.enabled = false;
            tickRecorder.enabled = false;
            follow.enabled = followEnabled;
            Time.timeScale = previousTimeScale;
            gameplay.aspect = previousAspect;
            PostProcessRuntimeState.DiagnosticWorldImage = null;
        }

        IEnumerator VerifyEntityVisibility()
        {
            bool lightingEnabled = Shader.IsKeywordEnabled("KERN_WORLD_LIGHTING");
            ISceneObjectFactory objects = PlayModeHarness.RequireInGame<ISceneObjectFactory>();
            GameObject probe = objects.Create("Existing surface depth visibility fixture", RuntimeOwner.General);
            probe.layer = legacy[0].gameObject.layer;
            var mesh = new Mesh { name = "Existing surface entity plane oracle" };
            mesh.vertices = new[]
            {
                new Vector3(253f, 260.1f, 0f), new Vector3(253f, 261.4f, 0f),
                new Vector3(259f, 260.1f, 0f), new Vector3(259f, 261.4f, 0f),
            };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
            mesh.colors = new[] { Color.green, Color.green, Color.green, Color.green };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateBounds();
            Shader shader = Shader.Find("Kern/World Entity");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            material.SetColor("_Color", Color.white);
            probe.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = probe.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = -1;
            try
            {
                // Explicit existing Quality Off variant isolates depth from GI.
                Shader.DisableKeyword("KERN_WORLD_LIGHTING");
                yield return Capture("entity-visible", 1280, 800, true);
            }
            finally
            {
                if (lightingEnabled) { Shader.EnableKeyword("KERN_WORLD_LIGHTING"); }
                Object.Destroy(probe);
                Object.Destroy(material);
                Object.Destroy(mesh);
            }
        }

        IEnumerator Measure(List<double> samples)
        {
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
                if (gpuRecorder.gpuElapsedNanoseconds > 0)
                {
                    samples.Add(gpuRecorder.gpuElapsedNanoseconds * 1e-6);
                }
            }
        }

        IEnumerator Capture(string name, int width, int height, bool expectSky)
        {
            Debug.Log($"[SurfaceSceneProof] Capturing {name}.");
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf)
            {
                name = $"Surface production capture {name}",
            };
            Assert.That(target.Create(), Is.True);
            Texture2D image = RuntimeTextureFactory.CreateRGBAFloatNoMip(width, height, "Surface float readback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
            bool rawCaptured = false;
            float rawPeak = 0f;
            RenderTexture? compositionCopy = null;
            PostProcessRuntimeState.DiagnosticWorldImage = (commands, texture, rect) =>
            {
                if (rawCaptured) { return; }
                compositionCopy = new RenderTexture(64, 40, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Assert.That(compositionCopy.Create(), Is.True);
                commands.Blit(texture, compositionCopy);
                rawCaptured = true;
            };
            try
            {
                camera.targetTexture = target;
                camera.aspect = (float)width / height;
                gameplay.aspect = camera.aspect;
                camera.enabled = true;
                yield return PlayModeHarness.Frames(5);
                Debug.Log($"[SurfaceSceneProof] {name}: frames rendered.");
                Assert.That(PostProcessRuntimeState.MainCamera, Is.EqualTo(gameplay));
                Assert.That(PostProcessRuntimeState.DiagnosticDisplayFrame, Is.GreaterThanOrEqualTo(Time.frameCount - 2));
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                Debug.Log($"[SurfaceSceneProof] {name}: final readback completed.");
                Color[] pixels = image.GetPixels();
                if (expectSky)
                {
                    Color upper = pixels[(height * 3 / 4) * width + width / 2];
                    Assert.That(upper.r + upper.g + upper.b, Is.GreaterThan(0.03f), "Production sky must reach the camera.");
                }
                if (name == "entity-visible")
                {
                    int minX = Mathf.CeilToInt((0.5f + (253.2f - 256f) / 96f) * width);
                    int maxX = Mathf.FloorToInt((0.5f + (258.8f - 256f) / 96f) * width);
                    int minY = Mathf.CeilToInt((0.5f + (260.25f - 253f) / 60f) * height);
                    int maxY = Mathf.FloorToInt((0.5f + (261.25f - 253f) / 60f) * height);
                    int visible = 0;
                    int total = 0;
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Color pixel = pixels[y * width + x];
                            if (pixel.g > (pixel.r + pixel.b) * 2f) { visible++; }
                            total++;
                        }
                    }
                    Assert.That((float)visible / total, Is.GreaterThan(0.98f),
                        "Existing horizon depth must not hide the production entity plane.");
                }
                if (name == "above")
                {
                    Color upper = pixels[(height * 3 / 4) * width + width / 2];
                    Assert.That(upper.a, Is.GreaterThan(0.99f),
                        "The original horizon must cover the frame at altitude.");
                    Assert.That(upper.r + upper.g + upper.b, Is.LessThan(0.005f),
                        "The restored horizon retains its authored exponential fade at high altitude.");
                }
                Assert.That(rawCaptured, Is.True, "Capture must observe the real world-grid scene pass.");
                Assert.That(compositionCopy, Is.Not.Null);
                Texture2D compositionImage = RuntimeTextureFactory.CreateRGBAFloatNoMip(
                    compositionCopy!.width, compositionCopy.height, "Owned composition readback",
                    RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
                RenderTexture previousComposition = RenderTexture.active;
                try
                {
                    RenderTexture.active = compositionCopy;
                    compositionImage.ReadPixels(new Rect(0, 0, compositionCopy.width, compositionCopy.height), 0, 0);
                    compositionImage.Apply();
                    Debug.Log($"[SurfaceSceneProof] {name}: composition readback {compositionCopy.width}x{compositionCopy.height} completed.");
                    foreach (Color value in compositionImage.GetPixels())
                    {
                        if (value.r > rawPeak) { rawPeak = value.r; }
                        if (value.g > rawPeak) { rawPeak = value.g; }
                        if (value.b > rawPeak) { rawPeak = value.b; }
                    }
                }
                finally
                {
                    RenderTexture.active = previousComposition;
                    Object.Destroy(compositionImage);
                }
                Texture2D png = RuntimeTextureFactory.CreateRGBA32NoMip(width, height, "Surface PNG readback", RuntimeTextureColorSpace.Srgb, FilterMode.Point, TextureWrapMode.Clamp);
                try
                {
                    // Readback is linear display light; PNG pixels are sRGB encoded exactly once.
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color value = pixels[index];
                        pixels[index] = new Color(EncodeLinearChannel(value.r),
                            EncodeLinearChannel(value.g), EncodeLinearChannel(value.b), value.a);
                    }
                    Debug.Log($"[SurfaceSceneProof] {name}: color conversion completed.");
                    png.SetPixels(pixels);
                    png.Apply();
                    byte[] encoded = png.EncodeToPNG();
                    Debug.Log($"[SurfaceSceneProof] {name}: PNG encoded, {encoded.Length} bytes.");
                    File.WriteAllBytes(Path.Combine(directory, name + ".png"), encoded);
                    Debug.Log($"[SurfaceSceneProof] {name}: PNG saved.");
                }
                finally
                {
                    Object.Destroy(png);
                }
                report.Add($"{name}: {width}x{height}, ortho={camera.orthographicSize}, resampled composition peak after URP={rawPeak:F4}");
            }
            finally
            {
                PostProcessRuntimeState.DiagnosticWorldImage = null;
                camera.enabled = false;
                camera.targetTexture = null;
                Object.Destroy(image);
                if (compositionCopy != null)
                {
                    compositionCopy.Release();
                    Object.Destroy(compositionCopy);
                }
                target.Release();
                Object.Destroy(target);
            }
        }
    }
    // Diagnostic PNG conversion is CPU-only; production URP output is unchanged.
    private static float EncodeLinearChannel(float value) => value <= 0.0031308f
        ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

}
