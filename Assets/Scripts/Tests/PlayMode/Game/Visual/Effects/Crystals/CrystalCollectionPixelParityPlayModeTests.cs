#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Kern.Core.Lifecycle;
using Kern.Game;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using Kern.Diagnostics.Artifacts;
using Kern.Game.Visual.Effects.Events.Playback;
using Kern.Game.Visual.Effects.Sprites;

namespace Kern.Tests.PlayMode;

[TestFixture]
public sealed class CrystalCollectionPixelParityPlayModeTests
{
    private const int CaptureSize = 128;
    private const int PairedPreviewScale = 4;
    private const float CaptureCameraOrthographicSize = 2f;
    private const int PixelsPerWorldUnit = CaptureSize / (int)(CaptureCameraOrthographicSize * 2f);
    private const int ReferenceLayer = 30;
    private const int ProductionLayer = 31;
    private const float FixedDeltaTime = 1f / 60f;
    private const int MaximumFrames = 120;
    private const float SourcePixelsPerUnit = 16f;
    private const float SourceSpriteWorldSize = 12f / SourcePixelsPerUnit;
    private const string SourceAtlasSha256 = "4cf030610b04288b708b3068faf290ff45370d654db44dac7b6e0c7da138a26c";

    private static readonly (string Color, int SpriteIndex)[] s_colors =
    {
        ("g", 0),
        ("r", 1),
        ("v", 2),
        ("b", 3),
        ("w", 4),
        ("c", 5),
    };

    [UnityTest]
    public IEnumerator TwoCrystalFlight_MatchesSourceSpriteCounterAndTimelinePixelForPixel()
    {
        var ownedObjects = new List<GameObject>();
        var ownedTextures = new List<Texture2D>();
        var ownedSprites = new List<Sprite>();
        var ownedRenderTextures = new List<RenderTexture>();
        SpriteAnimationLibrary? productionLibrary = null;
        string outputDirectory = Path.Combine(
            DiagnosticArtifactPaths.GetDirectory("CrystalPixelParity"),
            Guid.NewGuid().ToString("N"));
        string sourceDirectory = Path.Combine(outputDirectory, "source");
        string productionDirectory = Path.Combine(outputDirectory, "production");
        string pairedDirectory = Path.Combine(outputDirectory, "paired");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(productionDirectory);
        Directory.CreateDirectory(pairedDirectory);
        Debug.Log($"[CrystalPixelParity] Captures will be written to {outputDirectory}.");

        try
        {
            TextAsset atlasAsset = Resources.Load<TextAsset>("Visual/Sprites/Textures/MineralsPanel/MineralsPanelImage")
                ?? throw new InvalidOperationException("The crystal atlas resource is missing.");
            using SHA256 sha256 = SHA256.Create();
            string actualAtlasHash = BitConverter.ToString(sha256.ComputeHash(atlasAsset.bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
            Assert.That(actualAtlasHash, Is.EqualTo(SourceAtlasSha256),
                "The atlas bytes differ from the extracted source PNG.");

            var sourceAtlas = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            ownedTextures.Add(sourceAtlas);
            Assert.That(sourceAtlas.LoadImage(atlasAsset.bytes, markNonReadable: false), Is.True);
            Assert.That(sourceAtlas.width, Is.EqualTo(200));
            Assert.That(sourceAtlas.height, Is.EqualTo(12));
            productionLibrary = new SpriteAnimationLibrary();

            RenderTexture sourceTarget = CreateTarget("Crystal source reference", ownedRenderTextures);
            RenderTexture productionTarget = CreateTarget("Crystal production path", ownedRenderTextures);
            Texture2D sourceReadback = CreateReadback("Crystal source readback", ownedTextures);
            Texture2D productionReadback = CreateReadback("Crystal production readback", ownedTextures);
            Camera sourceCamera = CreateCamera("Crystal source camera", sourceTarget, ReferenceLayer, ownedObjects);
            Camera productionCamera = CreateCamera("Crystal production camera", productionTarget, ProductionLayer, ownedObjects);

            var sceneObjects = new VerificationSceneObjectFactory(ownedObjects);
            Vector3 origin = new(-1.5f, -0.5f, 0f);
            Vector3 destination = new(1.5f, 0.5f, 0f);
            int capturedFrames = 0;
            long totalDifferentPixels = 0;
            int maximumChannelDelta = 0;
            var differingFrames = new List<string>();

            foreach ((string color, int spriteIndex) in s_colors)
            {
                var sourceRoot = CreateRoot($"Source {color}", ReferenceLayer, ownedObjects);
                var productionRoot = CreateRoot($"Production {color}", ProductionLayer, ownedObjects);

                Sprite sourceSprite = Sprite.Create(
                    sourceAtlas,
                    new Rect(spriteIndex * 12, 0, 12, 12),
                    new Vector2(0.5f, 0.5f),
                    SourcePixelsPerUnit);
                Assert.That(sourceSprite.bounds.size.x, Is.EqualTo(SourceSpriteWorldSize).Within(0.00001f),
                    $"Source crystal sprite for {color} must match its serialized 12px/16PPU source dimensions.");
                ownedSprites.Add(sourceSprite);
                sourceSprite.name = $"Source crystal {color}";

                Sprite productionSprite = productionLibrary.GetCrystalCollectionSprite(color);
                Assert.That(productionSprite.bounds.size.x, Is.EqualTo(SourceSpriteWorldSize).Within(0.00001f),
                    $"Production crystal sprite for {color} must match its serialized 12px/16PPU source dimensions.");
                GameObject productionSpriteObject = ServerVisualEvent.CreateSourceCollectionSpriteObject(
                    sceneObjects,
                    productionRoot.transform,
                    productionSprite,
                    Color.white);
                productionSpriteObject.layer = ProductionLayer;

                AddSourcePrefabSpriteRenderer(sourceRoot.transform, sourceSprite, ReferenceLayer);
                CreateSourcePrefabCounter(sourceRoot.transform, "2", ReferenceLayer, ownedObjects);
                GameObject productionCounter = ServerVisualEvent.CreateSourceCollectionCounter(
                    sceneObjects,
                    productionRoot.transform,
                    "2");
                SetLayerRecursively(productionCounter, ProductionLayer);
                PositionCameras(sourceCamera, productionCamera, Vector3.zero);
                sourceRoot.transform.position = origin;
                productionRoot.transform.position = origin;

                Vector3 sourcePosition = origin;
                Vector3 productionPosition = origin;
                bool sourceReturning = false;
                bool productionReturning = false;
                bool sourceCompleted = false;
                bool productionCompleted = false;
                int frameIndex = 0;

                yield return null;
                RenderCaptureCameras(sourceCamera, productionCamera);
                FrameComparisonResult firstFrame = CompareAndSaveFrame(
                    color,
                    frameIndex,
                    sourceTarget,
                    productionTarget,
                    sourceReadback,
                    productionReadback,
                    sourceDirectory,
                    productionDirectory,
                    pairedDirectory);
                AssertCounterPixelBounds(sourceReadback, sourceTarget, "source reference", color);
                AssertCounterPixelBounds(productionReadback, productionTarget, "production", color);
                AssertCrystalSpriteVisible(sourceReadback, sourceTarget, "source reference", color);
                AssertCrystalSpriteVisible(productionReadback, productionTarget, "production", color);
                AddComparison(firstFrame, ref totalDifferentPixels, ref maximumChannelDelta, differingFrames);
                capturedFrames++;

                while (!sourceCompleted && frameIndex < MaximumFrames)
                {
                    sourcePosition = AdvanceSourceOracle(
                        sourcePosition,
                        origin,
                        destination,
                        ref sourceReturning,
                        out sourceCompleted);
                    productionPosition = ServerVisualEvent.AdvanceCrystalFlightSourceLogic(
                        productionPosition,
                        origin,
                        destination,
                        ref productionReturning,
                        FixedDeltaTime,
                        out productionCompleted);

                    if (productionReturning != sourceReturning)
                    {
                        differingFrames.Add($"{color}-frame-{frameIndex:D3}: phase mismatch");
                    }

                    if (productionCompleted != sourceCompleted)
                    {
                        differingFrames.Add($"{color}-frame-{frameIndex:D3}: completion mismatch");
                    }

                    sourceRoot.transform.position = sourcePosition;
                    productionRoot.transform.position = productionPosition;
                    frameIndex++;
                    yield return null;
                    RenderCaptureCameras(sourceCamera, productionCamera);
                    FrameComparisonResult frame = CompareAndSaveFrame(
                        color,
                        frameIndex,
                        sourceTarget,
                        productionTarget,
                        sourceReadback,
                        productionReadback,
                        sourceDirectory,
                        productionDirectory,
                        pairedDirectory);
                    AddComparison(frame, ref totalDifferentPixels, ref maximumChannelDelta, differingFrames);
                    capturedFrames++;
                }

                Assert.That(sourceCompleted, Is.True,
                    $"The source timeline did not complete within {MaximumFrames} fixed frames.");
                Assert.That(productionCompleted, Is.True,
                    $"The production timeline did not complete within {MaximumFrames} fixed frames.");
                Object.Destroy(sourceSprite);
                Object.Destroy(sourceRoot);
                Object.Destroy(productionRoot);
                yield return null;
            }

            string reportPath = Path.Combine(outputDirectory, "report.txt");
            File.WriteAllText(
                reportPath,
                $"Frame pairs: {capturedFrames}{Environment.NewLine}" +
                $"Capture dimensions: {CaptureSize}x{CaptureSize} at {PixelsPerWorldUnit} pixels per world unit{Environment.NewLine}" +
                $"Paired preview scale: {PairedPreviewScale}x nearest-neighbor{Environment.NewLine}" +
                $"Different pixels: {totalDifferentPixels}{Environment.NewLine}" +
                $"Maximum channel delta: {maximumChannelDelta}{Environment.NewLine}" +
                $"Mismatching frames: {differingFrames.Count}{Environment.NewLine}" +
                $"First mismatches: {string.Join(", ", differingFrames.GetRange(0, Math.Min(8, differingFrames.Count)))}{Environment.NewLine}");
            Debug.Log(
                $"[CrystalPixelParity] Captured {capturedFrames} source/production frame pairs at " +
                $"{outputDirectory}; report={reportPath}; {totalDifferentPixels} differing pixels across " +
                $"{differingFrames.Count} mismatching frame checks; max channel delta={maximumChannelDelta}.");
            Assert.That(
                totalDifferentPixels,
                Is.Zero,
                $"{differingFrames.Count} frame checks differ; " +
                $"{totalDifferentPixels} RGBA pixels differ; max channel delta={maximumChannelDelta}. " +
                $"First mismatches: {string.Join(", ", differingFrames.GetRange(0, Math.Min(8, differingFrames.Count)))}. " +
                $"Captures: {outputDirectory}");
        }
        finally
        {
            productionLibrary?.Dispose();
            foreach (GameObject ownedObject in ownedObjects)
            {
                if (ownedObject != null)
                {
                    Object.Destroy(ownedObject);
                }
            }

            foreach (RenderTexture target in ownedRenderTextures)
            {
                if (target != null)
                {
                    target.Release();
                    Object.Destroy(target);
                }
            }

            foreach (Texture2D texture in ownedTextures)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }

            foreach (Sprite sprite in ownedSprites)
            {
                if (sprite != null)
                {
                    Object.Destroy(sprite);
                }
            }
        }
    }

    private static RenderTexture CreateTarget(string name, ICollection<RenderTexture> ownedTextures)
    {
        var target = new RenderTexture(
            CaptureSize,
            CaptureSize,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB)
        {
            name = name,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Point,
        };
        target.Create();
        ownedTextures.Add(target);
        return target;
    }

    private static Texture2D CreateReadback(string name, ICollection<Texture2D> ownedTextures)
    {
        var texture = new Texture2D(CaptureSize, CaptureSize, TextureFormat.RGBA32, false, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        ownedTextures.Add(texture);
        return texture;
    }

    private static Camera CreateCamera(
        string name,
        RenderTexture target,
        int layer,
        ICollection<GameObject> ownedObjects)
    {
        var cameraObject = new GameObject(name);
        ownedObjects.Add(cameraObject);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = true;
        camera.orthographic = true;
        camera.orthographicSize = CaptureCameraOrthographicSize;
        camera.aspect = 1f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << layer;
        camera.targetTexture = target;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        return camera;
    }

    private static GameObject CreateRoot(string name, int layer, ICollection<GameObject> ownedObjects)
    {
        var root = new GameObject(name) { layer = layer };
        ownedObjects.Add(root);
        return root;
    }

    private static void AddSourcePrefabSpriteRenderer(Transform parent, Sprite sprite, int layer)
    {
        var spriteObject = new GameObject("MineralSymbol") { layer = layer };
        spriteObject.transform.SetParent(parent, worldPositionStays: false);
        spriteObject.transform.localPosition = Vector3.zero;
        spriteObject.transform.localRotation = Quaternion.identity;
        spriteObject.transform.localScale = Vector3.one;

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 5;
        renderer.color = Color.white;
        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.maskInteraction = SpriteMaskInteraction.None;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void CreateSourcePrefabCounter(
        Transform parent,
        string count,
        int layer,
        ICollection<GameObject> ownedObjects)
    {
        var canvasObject = new GameObject("Canvas");
        ownedObjects.Add(canvasObject);
        canvasObject.layer = layer;
        canvasObject.transform.SetParent(parent, worldPositionStays: false);
        canvasObject.transform.localPosition = Vector3.zero;
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = Vector2.one;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.planeDistance = 100f;
        canvas.overrideSorting = false;
        canvas.sortingOrder = 100;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        var textObject = new GameObject("Text (TMP)");
        ownedObjects.Add(textObject);
        textObject.layer = layer;
        textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
        textObject.transform.localPosition = Vector3.zero;
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.anchoredPosition = new Vector2(1f, 0.375f);
        textRect.sizeDelta = Vector2.zero;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        text.fontSize = 0.5f;
        text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.text = count;
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
        gameObject.layer = layer;
        foreach (Transform child in gameObject.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void PositionCameras(Camera source, Camera production, Vector3 center)
    {
        Vector3 cameraPosition = center + new Vector3(0f, 0f, -10f);
        source.transform.position = cameraPosition;
        production.transform.position = cameraPosition;
    }

    private static void RenderCaptureCameras(Camera source, Camera production)
    {
        Canvas.ForceUpdateCanvases();
        source.Render();
        production.Render();
    }

    private static Vector3 AdvanceSourceOracle(
        Vector3 current,
        Vector3 origin,
        Vector3 destination,
        ref bool returning,
        out bool completed)
    {
        Vector3 target = returning ? destination : origin + new Vector3(0f, 0.25f, 0f);
        float interpolation = FixedDeltaTime * (returning ? 12f : 8f);
        Vector3 next = Vector3.Lerp(current, target, interpolation);
        completed = false;

        if (Vector2.Distance(next, target) < 0.05f)
        {
            if (returning)
            {
                completed = true;
            }
            else
            {
                returning = true;
            }
        }

        return next;
    }

    private static FrameComparisonResult CompareAndSaveFrame(
        string color,
        int frameIndex,
        RenderTexture sourceTarget,
        RenderTexture productionTarget,
        Texture2D sourceReadback,
        Texture2D productionReadback,
        string sourceDirectory,
        string productionDirectory,
        string pairedDirectory)
    {
        Color32[] sourcePixels = Capture(sourceReadback, sourceTarget);
        Color32[] productionPixels = Capture(productionReadback, productionTarget);
        string filename = $"{color}-frame-{frameIndex:D3}.png";
        File.WriteAllBytes(Path.Combine(sourceDirectory, filename), sourceReadback.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(productionDirectory, filename), productionReadback.EncodeToPNG());
        WritePairedFrame(Path.Combine(pairedDirectory, filename), sourcePixels, productionPixels);

        int differentPixels = 0;
        int firstDifferentPixel = -1;
        int maximumChannelDelta = 0;
        for (int pixelIndex = 0; pixelIndex < sourcePixels.Length; pixelIndex++)
        {
            Color32 sourcePixel = sourcePixels[pixelIndex];
            Color32 productionPixel = productionPixels[pixelIndex];
            if (sourcePixel.Equals(productionPixel))
            {
                continue;
            }

            differentPixels++;
            if (firstDifferentPixel < 0)
            {
                firstDifferentPixel = pixelIndex;
            }

            maximumChannelDelta = Math.Max(
                maximumChannelDelta,
                Math.Max(
                    Math.Max(
                        Math.Abs(sourcePixel.r - productionPixel.r),
                        Math.Abs(sourcePixel.g - productionPixel.g)),
                    Math.Max(
                        Math.Abs(sourcePixel.b - productionPixel.b),
                        Math.Abs(sourcePixel.a - productionPixel.a))));
        }

        int firstX = firstDifferentPixel < 0 ? -1 : firstDifferentPixel % CaptureSize;
        int firstY = firstDifferentPixel < 0 ? -1 : firstDifferentPixel / CaptureSize;
        if (differentPixels > 0)
        {
            WriteDifferenceImage(
                sourceDirectory,
                filename,
                sourcePixels,
                productionPixels);
        }

        return new FrameComparisonResult(filename, differentPixels, maximumChannelDelta, firstX, firstY);
    }

    private static void WritePairedFrame(
        string path,
        Color32[] sourcePixels,
        Color32[] productionPixels)
    {
        const int pairedWidth = (CaptureSize * 2) + 1;
        var capturePixels = new Color32[pairedWidth * CaptureSize];
        var divider = new Color32(255, 205, 64, 255);
        for (int y = 0; y < CaptureSize; y++)
        {
            int rowStart = y * pairedWidth;
            int captureRowStart = y * CaptureSize;
            for (int x = 0; x < CaptureSize; x++)
            {
                capturePixels[rowStart + x] = sourcePixels[captureRowStart + x];
                capturePixels[rowStart + CaptureSize + 1 + x] = productionPixels[captureRowStart + x];
            }

            capturePixels[rowStart + CaptureSize] = divider;
        }

        int previewWidth = pairedWidth * PairedPreviewScale;
        int previewHeight = CaptureSize * PairedPreviewScale;
        var previewPixels = new Color32[previewWidth * previewHeight];
        for (int y = 0; y < CaptureSize; y++)
        {
            for (int x = 0; x < pairedWidth; x++)
            {
                Color32 pixel = capturePixels[(y * pairedWidth) + x];
                int scaledStartX = x * PairedPreviewScale;
                int scaledStartY = y * PairedPreviewScale;
                for (int offsetY = 0; offsetY < PairedPreviewScale; offsetY++)
                {
                    int rowStart = (scaledStartY + offsetY) * previewWidth;
                    for (int offsetX = 0; offsetX < PairedPreviewScale; offsetX++)
                    {
                        previewPixels[rowStart + scaledStartX + offsetX] = pixel;
                    }
                }
            }
        }

        var texture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGBA32, false, false)
        {
            name = "Crystal source and production comparison",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(previewPixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.Destroy(texture);
    }

    private static void AssertCounterPixelBounds(
        Texture2D readback,
        RenderTexture target,
        string captureName,
        string color)
    {
        Color32[] pixels = Capture(readback, target);
        // The fixed camera maps the source root to x=16 and the prefab's
        // 1-unit Canvas to the counter glyphs around x=33..39.
        const int counterRegionMinX = 32;
        const int counterRegionMaxX = 48;
        int minimumY = CaptureSize;
        int maximumY = -1;
        for (int y = 0; y < CaptureSize; y++)
        {
            for (int x = counterRegionMinX; x < counterRegionMaxX; x++)
            {
                if (pixels[(y * CaptureSize) + x].a == 0)
                {
                    continue;
                }

                minimumY = Math.Min(minimumY, y);
                maximumY = Math.Max(maximumY, y);
            }
        }

        Assert.That(maximumY, Is.GreaterThanOrEqualTo(minimumY),
            $"The {captureName} counter is missing for crystal {color}.");
        Assert.That(maximumY - minimumY + 1, Is.GreaterThanOrEqualTo(8),
            $"The {captureName} counter is below the 8-pixel readability floor for crystal {color}.");
        Assert.That(maximumY - minimumY + 1, Is.LessThanOrEqualTo(12),
            $"The {captureName} counter exceeded its 12-pixel world-space bound for crystal {color}.");
    }

    private static void AssertCrystalSpriteVisible(
        Texture2D readback,
        RenderTexture target,
        string captureName,
        string color)
    {
        Color32[] pixels = Capture(readback, target);
        const int spriteRegionMinX = 10;
        const int spriteRegionMaxX = 22;
        // GetPixels32 uses bottom-left row order; the saved PNG is flipped vertically.
        const int spriteRegionMinY = 40;
        const int spriteRegionMaxY = 56;
        int visiblePixelCount = 0;
        for (int y = spriteRegionMinY; y < spriteRegionMaxY; y++)
        {
            for (int x = spriteRegionMinX; x < spriteRegionMaxX; x++)
            {
                if (pixels[(y * CaptureSize) + x].a > 0)
                {
                    visiblePixelCount++;
                }
            }
        }

        Assert.That(visiblePixelCount, Is.GreaterThan(0),
            $"The {captureName} crystal sprite is missing from its expected screen region for {color}.");
    }

    private static void AddComparison(
        FrameComparisonResult result,
        ref long totalDifferentPixels,
        ref int maximumChannelDelta,
        ICollection<string> differingFrames)
    {
        totalDifferentPixels += result.DifferentPixels;
        maximumChannelDelta = Math.Max(maximumChannelDelta, result.MaximumChannelDelta);
        if (result.DifferentPixels > 0)
        {
            differingFrames.Add(
                $"{result.Filename}: {result.DifferentPixels} pixels, max Δ={result.MaximumChannelDelta}, " +
                $"first=({result.FirstX},{result.FirstY})");
        }
    }

    private static void WriteDifferenceImage(
        string sourceDirectory,
        string filename,
        Color32[] sourcePixels,
        Color32[] productionPixels)
    {
        var difference = new Color32[sourcePixels.Length];
        var mismatch = new Color32(255, 0, 255, 255);
        for (int pixelIndex = 0; pixelIndex < difference.Length; pixelIndex++)
        {
            if (!sourcePixels[pixelIndex].Equals(productionPixels[pixelIndex]))
            {
                difference[pixelIndex] = mismatch;
            }
        }

        var differenceTexture = new Texture2D(CaptureSize, CaptureSize, TextureFormat.RGBA32, false, false)
        {
            name = "Crystal pixel mismatch",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        differenceTexture.SetPixels32(difference);
        differenceTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        string outputDirectory = Path.Combine(Path.GetDirectoryName(sourceDirectory)!, "diff");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(
            Path.Combine(outputDirectory, filename),
            differenceTexture.EncodeToPNG());
        Object.Destroy(differenceTexture);
    }

    private static Color32[] Capture(Texture2D readback, RenderTexture target)
    {
        RenderTexture? previousTarget = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, CaptureSize, CaptureSize), 0, 0, recalculateMipMaps: false);
            readback.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return readback.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previousTarget;
        }
    }

    private sealed class VerificationSceneObjectFactory : ISceneObjectFactory
    {
        private readonly ICollection<GameObject> _ownedObjects;

        public VerificationSceneObjectFactory(ICollection<GameObject> ownedObjects)
        {
            _ownedObjects = ownedObjects;
        }

        public Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General) =>
            throw new NotSupportedException("The pixel harness does not create persistent runtime owners.");

        public GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General)
        {
            var gameObject = new GameObject(name);
            _ownedObjects.Add(gameObject);
            return gameObject;
        }

        public T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General)
            where T : MonoBehaviour =>
            throw new NotSupportedException("The pixel harness only creates sprite renderers.");
    }

    private readonly struct FrameComparisonResult
    {
        public FrameComparisonResult(
            string filename,
            int differentPixels,
            int maximumChannelDelta,
            int firstX,
            int firstY)
        {
            Filename = filename;
            DifferentPixels = differentPixels;
            MaximumChannelDelta = maximumChannelDelta;
            FirstX = firstX;
            FirstY = firstY;
        }

        public string Filename { get; }

        public int DifferentPixels { get; }

        public int MaximumChannelDelta { get; }

        public int FirstX { get; }

        public int FirstY { get; }
    }
}
