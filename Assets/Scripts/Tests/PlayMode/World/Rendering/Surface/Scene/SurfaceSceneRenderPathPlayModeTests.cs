#nullable enable

using System;
using System.Collections;
using System.IO;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Rendering.PostProcessing;
using Kern.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

using Kern.AssetPipeline.Loading;
using Kern.Core.Rendering;
using Kern.Diagnostics.Artifacts;

namespace Kern.Tests.PlayMode;

/// <summary>Production Renderer2D/world-grid/composition path with a deterministic presentation fixture.</summary>
public sealed class SurfaceSceneRenderPathPlayModeTests
{
    [UnityTest]
    [Timeout(90_000)]
    public IEnumerator Surface_RealMeshPass_PreservesHdrAndProducesDepthLayers()
    {
        Debug.Log("[SurfaceSceneProof] Isolated render fixture starting.");
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear),
            "The production output curve must receive scene-linear color.");
        GameObject root;
#if UNITY_EDITOR
        root = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags("Surface production render fixture", HideFlags.DontSave);
#else
        throw new InvalidOperationException("This production capture fixture requires the headless Unity Editor.");
#endif
        root.layer = 31;
        using var objects = new FixtureFactory(root.transform);
        GameObject cameraObject = objects.Create("Surface production camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 30f;
        camera.aspect = 1.6f;
        camera.transform.position = new Vector3(256f, 253f, -10f);
        camera.cullingMask = 1 << 31;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        camera.allowHDR = true;
        camera.backgroundColor = Color.black;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.volumeLayerMask = 1 << 31;
        Camera? previousCamera = GameplayCamera.Resolve();
        Camera? previousDiagnostic = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        GameplayCamera.BindPersistent(camera);
        PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
        var volume = root.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = float.MaxValue;
        var volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = volumeProfile;
        Tonemapping tonemapping = volumeProfile.Add<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.Neutral);
        volumeProfile.Add<ColorAdjustments>().postExposure.Override(1f);
        volumeProfile.Add<EigengrauComponent>().intensity.Override(0.01f);
        using var scene = new SurfaceSceneRenderer(objects, null!, new FixtureCamera(camera));
        SurfaceSceneProfile profile = Resources.Load<SurfaceSceneProfile>(SurfaceSceneProfile.ResourcePath);
        scene.Initialize(root.transform, profile);
        scene.UpdateView(camera, 512, 256);
        var target = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGBHalf);
        Assert.That(target.Create(), Is.True);
        camera.targetTexture = target;
        BloomComponent bloom = volumeProfile.Add<BloomComponent>();
        bloom.intensity.Override(0.05f);
        var emptyGlow = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf);
        Assert.That(emptyGlow.Create(), Is.True);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = emptyGlow;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = previousActive;
        Texture? previousGlow = Shader.GetGlobalTexture("_WorldGlowTexture");
        Vector4 previousLightRect = Shader.GetGlobalVector("_WorldLightRect");
        Shader.SetGlobalTexture("_WorldGlowTexture", emptyGlow);
        Shader.SetGlobalVector("_WorldLightRect", new Vector4(0, 0, 512, 512));
        float hdrPeak = 0f;
        bool observed = false;
        bool complete = false;
        PostProcessRuntimeState.DiagnosticBloomImage = (commands, stage, texture, rect) =>
        {
            if (observed || stage != "before") { return; }
            observed = true;
            commands.RequestAsyncReadback(texture, 0, TextureFormat.RGBAFloat, request =>
            {
                Assert.That(request.hasError, Is.False);
                var pixels = request.GetData<Color>();
                foreach (Color pixel in pixels) { hdrPeak = Mathf.Max(hdrPeak, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b))); }
                complete = true;
            });
        };
        RTHandle? importedDepth = null;
        bool? originalDepthStencil = null;
        try
        {
            Debug.Log("[SurfaceSceneProof] Awaiting production frames.");
            for (int frame = 0; frame < 15; frame++) { yield return null; }
            Assert.That(observed, Is.True, "Must observe the actual scene-linear world-grid input before bloom and URP tonemapping.");
            Assert.That(complete, Is.True, "Production HDR readback must complete.");
            Assert.That(PostProcessRuntimeState.DiagnosticDisplayFrame, Is.GreaterThanOrEqualTo(Time.frameCount - 2));
            string directory = DiagnosticArtifactPaths.GetDirectory("surface-scene");
            Save("surface-3d");
            float neutralOutputLuminance = ReadOutputLuminance();
            // DisplayFinal receives URP-tonemapped light. A second numerical capture disables
            // that output curve explicitly to observe the same scene shader's unbounded radiance.
            tonemapping.mode.Override(TonemappingMode.None);
            observed = false;
            complete = false;
            hdrPeak = 0f;
            for (int frame = 0; frame < 15; frame++) { yield return null; }
            Assert.That(complete, Is.True);
            Assert.That(hdrPeak, Is.GreaterThan(1f), "Scene radiance must survive above paper white before the output curve.");
            float uncompressedOutputLuminance = ReadOutputLuminance();
            Assert.That(Mathf.Abs(uncompressedOutputLuminance - neutralOutputLuminance),
                Is.GreaterThan(neutralOutputLuminance * 0.01f),
                "Switching the native output curve must change the static scene. Clipped solar peaks alone cannot distinguish SDR output curves.");
            File.WriteAllText(Path.Combine(directory, "output-compression.txt"),
                $"colorSpace={QualitySettings.activeColorSpace}; neutralLuminance={neutralOutputLuminance:R}; " +
                $"uncompressedLuminance={uncompressedOutputLuminance:R}; scenePeak={hdrPeak:R}");
            tonemapping.mode.Override(TonemappingMode.Neutral);
            Debug.Log("[WorldDepthProof] Preparing display/world regression.");
            // Reproduce the reported display/world depth mismatch inside the
            // actual Renderer2D graph, before production sprite rasterization.
            target.Release();
            target.width = 3420;
            target.height = 1890;
            Assert.That(target.Create(), Is.True);
            camera.orthographicSize = 17.5f;
            camera.aspect = 1786f / 1120f;
            int depthlessFrames = 0;
            originalDepthStencil = SetRendererDepthStencil(false);
            PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup = (graph, frame) =>
            {
                Assert.That(frame.Get<UniversalResourceData>().cameraDepth.IsValid(), Is.False,
                    "Renderer2D must exercise its actual depthless resource allocation path.");
                depthlessFrames++;
            };
            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(depthlessFrames, Is.GreaterThan(0));
            PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup = null;
            SetRendererDepthStencil(originalDepthStencil.Value);
            originalDepthStencil = null;
            int repairedFrames = 0;
            importedDepth = RTHandles.Alloc(BuiltinRenderTextureType.Depth,
                name: "ReportedDepthWithoutCreationDescriptor");
            PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup = (graph, frame) =>
            {
                var resources = frame.Get<UniversalResourceData>();
                // ImportResourceInfo deliberately provides attachment facts only:
                // GetDescriptor throws for this valid handle, just as reported.
                resources.cameraDepth = graph.ImportTexture(importedDepth, new RenderTargetInfo
                {
                    width = 3420,
                    height = 1890,
                    volumeDepth = 1,
                    msaaSamples = 1,
                    format = CoreUtils.GetDefaultDepthStencilFormat(),
                }, new ImportResourceParams());
            };
            PostProcessRuntimeState.DiagnosticWorldRasterTarget = (renderedCamera, dimensions) =>
            {
                Assert.That(renderedCamera, Is.SameAs(camera));
                // The float aspect 1786f/1120f is slightly greater than the
                // exact ratio. Outward rounding adds a pixel on both X edges,
                // followed by the explicit guard on both edges.
                Assert.That(dimensions, Is.EqualTo(new Vector2Int(1790, 1122)));
                repairedFrames++;
            };
            for (int frame = 0; frame < 3; frame++)
            {
                Debug.Log($"[WorldDepthProof] Regression frame {frame}.");
                yield return null;
            }
            Assert.That(repairedFrames, Is.GreaterThan(0));
            // Exercise the other selector state: a valid display depth with a
            // small camera color. The previous cameraDepth-only repair missed it.
            PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup = (graph, frame) =>
            {
                var resources = frame.Get<UniversalResourceData>();
                TextureHandle displayDepth = graph.ImportTexture(importedDepth, new RenderTargetInfo
                {
                    width = 3420,
                    height = 1890,
                    volumeDepth = 1,
                    msaaSamples = 1,
                    format = CoreUtils.GetDefaultDepthStencilFormat(),
                }, new ImportResourceParams());
                const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var backbuffer = typeof(UniversalResourceData).GetProperty("backBufferDepth", Flags);
                var selector = typeof(UniversalResourceData).GetProperty("activeDepthID", Flags);
                Assert.That(backbuffer, Is.Not.Null);
                Assert.That(selector, Is.Not.Null);
                backbuffer!.SetValue(resources, displayDepth);
                selector!.SetValue(resources, Enum.Parse(selector.PropertyType, "BackBuffer"));
                Assert.That(resources.activeDepthTexture, Is.EqualTo(displayDepth));
            };
            int cameraDepthFrames = repairedFrames;
            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(repairedFrames, Is.GreaterThan(cameraDepthFrames),
                "The actual Renderer2D graph must also render with initially selected display depth.");
            Debug.Log("[WorldDepthProof] Capturing matched attachments.");
            Save("display-world-depth-regression");
            File.WriteAllText(Path.Combine(directory, "depth-attachments.txt"),
                $"display=3420x1890; geometry=1790x1122; guard=1; injectedDepth=3420x1890; depthlessFrames={depthlessFrames}; matchedFrames={repairedFrames}");
            PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup = null;
            PostProcessRuntimeState.DiagnosticWorldRasterTarget = null;
            target.Release();
            target.width = 1280;
            target.height = 800;
            Assert.That(target.Create(), Is.True);
            camera.orthographicSize = 30f;
            camera.aspect = 1.6f;
            int rebuilds = scene.RebuildCount;
            for (int frame = 0; frame < 30; frame++)
            {
                scene.UpdateView(camera, 512, 256);
                yield return null;
            }
            Assert.That(scene.RebuildCount, Is.EqualTo(rebuilds));
            yield return VerifyReservedGroundBand();
            yield return VerifyGameplayPlaneVisibility();
            camera.transform.position += Vector3.right * 33f;
            scene.UpdateView(camera, 512, 256);
            for (int frame = 0; frame < 5; frame++) { yield return null; }
            Save("surface-3d-adjacent");
            camera.orthographicSize = 10f;
            camera.transform.position = new Vector3(256f, 260f, -10f);
            scene.UpdateView(camera, 512, 256);
            for (int frame = 0; frame < 5; frame++) { yield return null; }
            Save("surface-3d-near");
            File.WriteAllText(Path.Combine(directory, "render-path.txt"),
                $"Unity={Application.unityVersion}; GPU={SystemInfo.graphicsDeviceName}; HDR peak={hdrPeak}; " +
                $"stable rebuild delta=0; segments={scene.ActiveSegmentCount}; triangles={scene.TriangleCount}. " +
                "Production Renderer2D, world-grid shader projection, URP Neutral/exposure, DisplayFinal; isolated surface fixture, no terrain data.");
            Debug.Log($"[SurfaceSceneProof] Production render path passed; HDR peak={hdrPeak}; captures={directory}");

            IEnumerator VerifyReservedGroundBand()
            {
                MeshRenderer[] bands = Array.FindAll(root.GetComponentsInChildren<MeshRenderer>(),
                    renderer => renderer.gameObject.name.StartsWith("Surface layer", StringComparison.Ordinal));
                var previousObserver = PostProcessRuntimeState.DiagnosticBloomImage;
                Color[][] samples = new Color[2][];
                Vector4[] rectangles = new Vector4[2];
                int[] widths = new int[2];
                int[] heights = new int[2];
                try
                {
                    for (int sample = 0; sample < 2; sample++)
                    {
                        foreach (MeshRenderer band in bands) { band.enabled = sample == 1; }
                        yield return null;
                        bool requested = false;
                        bool completed = false;
                        int capturedSample = sample;
                        PostProcessRuntimeState.DiagnosticBloomImage = (commands, stage, texture, rect) =>
                        {
                            if (requested || stage != "before") { return; }
                            requested = true;
                            rectangles[capturedSample] = rect;
                            commands.RequestAsyncReadback(texture, 0, TextureFormat.RGBAFloat, request =>
                            {
                                Assert.That(request.hasError, Is.False);
                                widths[capturedSample] = request.width;
                                heights[capturedSample] = request.height;
                                samples[capturedSample] = request.GetData<Color>().ToArray();
                                completed = true;
                            });
                        };
                        for (int frame = 0; frame < 60 && !completed; frame++) { yield return null; }
                        Assert.That(completed, Is.True, "Must read the real scene before bloom.");
                    }
                    Assert.That(widths[1], Is.EqualTo(widths[0]));
                    Assert.That(heights[1], Is.EqualTo(heights[0]));
                    Assert.That(rectangles[1], Is.EqualTo(rectangles[0]));
                    Vector4 worldRect = rectangles[0];
                    int checkedPixels = 0;
                    float maximumDelta = 0f;
                    for (int y = 0; y < heights[0]; y++)
                    {
                        float worldY = worldRect.y + (y + 0.5f) / heights[0] * worldRect.w;
                        if (worldY < 255.125f || worldY > 259.875f) { continue; }
                        for (int x = 0; x < widths[0]; x++)
                        {
                            Color delta = samples[1][y * widths[0] + x] - samples[0][y * widths[0] + x];
                            maximumDelta = Mathf.Max(maximumDelta,
                                Mathf.Max(Mathf.Abs(delta.r), Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b))));
                            checkedPixels++;
                        }
                    }
                    Assert.That(checkedPixels, Is.GreaterThan(100));
                    Assert.That(maximumDelta, Is.LessThan(0.0001f),
                        "Decorative geometry must change no RGB samples within the original four-cell surface band.");
                    File.WriteAllText(Path.Combine(directory, "ground-band.txt"),
                        $"Production scene before bloom: ground-band samples={checkedPixels}; maximum RGB delta={maximumDelta}. " +
                        "Same camera/sky; all decorative meshes disabled then enabled; independent world-Y region 255.125..259.875.");
                }
                finally
                {
                    foreach (MeshRenderer band in bands) { band.enabled = true; }
                    PostProcessRuntimeState.DiagnosticBloomImage = previousObserver;
                }
            }

            IEnumerator VerifyGameplayPlaneVisibility()
            {
                // Real World Entity pass, at the production batch plane z=0.
                // Quality Off is explicit; the oracle is a solid green fixture,
                // independent of surface hash, mesh builders and shader functions.
                bool lightingEnabled = Shader.IsKeywordEnabled("KERN_WORLD_LIGHTING");
                Shader entityShader = Shader.Find("Kern/World Entity");
                Assert.That(entityShader, Is.Not.Null);
                Assert.That(entityShader.isSupported, Is.True);
                GameObject entity = objects.Create("Gameplay plane visibility fixture");
                entity.layer = 31;
                var mesh = new Mesh { name = "Gameplay plane production entity fixture" };
                mesh.vertices = new[]
                {
                    new Vector3(253f, 260.1f, 0f), new Vector3(253f, 261.4f, 0f),
                    new Vector3(259f, 260.1f, 0f), new Vector3(259f, 261.4f, 0f),
                };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
                mesh.colors = new[] { Color.green, Color.green, Color.green, Color.green };
                mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
                mesh.RecalculateBounds();
                var material = new Material(entityShader);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetColor("_Color", Color.white);
                entity.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer entityRenderer = entity.AddComponent<MeshRenderer>();
                entityRenderer.sharedMaterial = material;
                entityRenderer.sortingOrder = -1;
                Texture2D readback = RuntimeTextureFactory.CreateRGBAFloatNoMip(target.width, target.height,
                    "Gameplay plane visibility readback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Shader.DisableKeyword("KERN_WORLD_LIGHTING");
                    for (int frame = 0; frame < 5; frame++) { yield return null; }
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    readback.Apply();
                    Color[] pixels = readback.GetPixels();
                    // Independent orthographic projection, inset from fixture edges.
                    int minX = Mathf.CeilToInt((0.5f + (253.2f - 256f) / 96f) * target.width);
                    int maxX = Mathf.FloorToInt((0.5f + (258.8f - 256f) / 96f) * target.width);
                    int minY = Mathf.CeilToInt((0.5f + (260.25f - 253f) / 60f) * target.height);
                    int maxY = Mathf.FloorToInt((0.5f + (261.25f - 253f) / 60f) * target.height);
                    int visible = 0;
                    int total = 0;
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Color pixel = pixels[y * target.width + x];
                            if (pixel.g > (pixel.r + pixel.b) * 2f) { visible++; }
                            total++;
                        }
                    }
                    Assert.That((float)visible / total, Is.GreaterThan(0.98f),
                        "The production entity plane must remain visible over every decorative surface depth sample.");
                    Save("surface-entity-visibility");
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (lightingEnabled) { Shader.EnableKeyword("KERN_WORLD_LIGHTING"); }
                    Object.Destroy(entity);
                    Object.Destroy(material);
                    Object.Destroy(mesh);
                    Object.Destroy(readback);
                }
            }

            float ReadOutputLuminance()
            {
                RenderTexture previous = RenderTexture.active;
                Texture2D texture = RuntimeTextureFactory.CreateRGBAHalfNoMip(target.width, target.height,
                    "Output curve readback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
                try
                {
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    texture.Apply();
                    double luminance = 0.0;
                    foreach (Color pixel in texture.GetPixels())
                    {
                        luminance += 0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b;
                    }

                    return (float)(luminance / (target.width * target.height));
                }
                finally
                {
                    RenderTexture.active = previous;
                    Object.Destroy(texture);
                }
            }

            void Save(string name)
            {
                RenderTexture previous = RenderTexture.active;
                Texture2D texture = RuntimeTextureFactory.CreateRGBAFloatNoMip(target.width, target.height, "Surface float readback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
                Texture2D png = RuntimeTextureFactory.CreateRGBA32NoMip(target.width, target.height, "Surface PNG readback", RuntimeTextureColorSpace.Srgb, FilterMode.Point, TextureWrapMode.Clamp);
                try
                {
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    texture.Apply();
                    Color[] pixels = texture.GetPixels();
                    Color upper = pixels[target.height * 3 / 4 * target.width + target.width / 2];
                    Assert.That(upper.r + upper.g + upper.b, Is.GreaterThan(0.03f));
                    for (int index = 0; index < pixels.Length; index++) { pixels[index] = pixels[index].gamma; }
                    png.SetPixels(pixels);
                    png.Apply();
                    File.WriteAllBytes(Path.Combine(directory, name + ".png"), png.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    Object.Destroy(texture);
                    Object.Destroy(png);
                }
            }
        }
        finally
        {
            if (originalDepthStencil.HasValue)
            {
                SetRendererDepthStencil(originalDepthStencil.Value);
            }

            importedDepth?.Release();
            Time.timeScale = previousTimeScale;
            PostProcessRuntimeState.DiagnosticWorldRasterDepthSetup = null;
            PostProcessRuntimeState.DiagnosticWorldRasterTarget = null;
            PostProcessRuntimeState.DiagnosticWorldImage = null;
            PostProcessRuntimeState.DiagnosticBloomImage = null;
            Shader.SetGlobalTexture("_WorldGlowTexture", previousGlow);
            Shader.SetGlobalVector("_WorldLightRect", previousLightRect);
            emptyGlow.Release();
            Object.Destroy(emptyGlow);
            PostProcessRuntimeState.DiagnosticOffscreenCamera = previousDiagnostic;
            if (previousCamera != null) { GameplayCamera.BindPersistent(previousCamera); }
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            scene.Dispose();
            Object.Destroy(root);
            Object.Destroy(volumeProfile);
            Object.Destroy(cameraObject);
        }
    }

    private static bool SetRendererDepthStencil(bool enabled)
    {
#if UNITY_EDITOR
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        ScriptableRendererData data = pipeline.rendererDataList[0];
        using var serialized = new UnityEditor.SerializedObject(data);
        UnityEditor.SerializedProperty property = serialized.FindProperty("m_UseDepthStencilBuffer");
        Assert.That(property, Is.Not.Null, "The production fixture requires Renderer2D depth settings.");
        bool previous = property.boolValue;
        property.boolValue = enabled;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        data.SetDirty();
        return previous;
#else
        throw new InvalidOperationException("Renderer2D allocation regression requires the headless Editor.");
#endif
    }

    private sealed class FixtureCamera : IGameplayCamera
    {
        public FixtureCamera(Camera camera) => Camera = camera;

        public Camera Camera { get; }
    }

    private sealed class FixtureFactory : ISceneObjectFactory, IDisposable
    {
        private readonly IDisposable _container;
        private readonly SceneObjectFactory _factory;
        private SceneObjectFactory Factory => _factory;

        public FixtureFactory(Transform root)
        {
            var container = new ContainerBuilder().Build();
            _container = container;
            _factory = new SceneObjectFactory(root, root, root, root, root, root, container);
        }
        public Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General) => Factory.GetOwner(owner);
        public GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General)
        {
            GameObject value = Factory.Create(name, owner);
            value.layer = Factory.GetOwner(owner).gameObject.layer;
            return value;
        }

        public T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General) where T : MonoBehaviour =>
            Factory.Create<T>(name, owner);

        public void Dispose() => _container.Dispose();
    }
}
