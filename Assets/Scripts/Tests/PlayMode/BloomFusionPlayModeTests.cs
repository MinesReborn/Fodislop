#nullable enable

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class BloomFusionPlayModeTests
{
    private readonly List<Object> _owned = [];

    [TearDown]
    public void TearDown()
    {
        foreach (Object resource in _owned)
        {
            if (resource is RenderTexture target)
            {
                target.Release();
            }

            Object.DestroyImmediate(resource);
        }

        _owned.Clear();
    }

    [UnityTest]
    public IEnumerator FusedKernel_PreservesHdrFilterEdgesOddSizesAndAlpha()
    {
        ComputeShader shader = Object.Instantiate(Resources.Load<ComputeShader>("Shaders/PostProcessing/PostProcess"));
        _owned.Add(shader);
        Assert.That(shader, Is.Not.Null);
        foreach (GraphicsFormat format in new[]
        {
            GraphicsFormat.R16G16B16A16_SFloat,
            GraphicsFormat.B10G11R11_UFloatPack32,
            GraphicsFormat.R32G32B32A32_SFloat,
        })
        {
            foreach (int width in new[] { 32, 35, 65 })
            {
                Compare(shader, format, width, width - 3, false);
                Compare(shader, format, width, width - 3, true);
                Compare(shader, format, width, width - 3, true, true);
                yield return null;
            }
        }
    }

    private void Compare(ComputeShader shader, GraphicsFormat format, int width, int height, bool constant, bool impulse = false)
    {
        int halfWidth = width / 2;
        int halfHeight = height / 2;
        Texture2D scene = Input(width, height, constant, 0);
        Texture2D high = Input(halfWidth, halfHeight, constant, 1);
        Texture2D low = Input(halfWidth / 2, halfHeight / 2, constant, 2);
        if (impulse)
        {
            var isolated = new Color[halfWidth * halfHeight];
            isolated[0] = new Color(16f, 8f, 4f, 1f);
            high.SetPixels(isolated);
            high.Apply();
            low.SetPixels(new Color[low.width * low.height]);
            low.Apply();
        }
        RenderTexture up = Target(halfWidth, halfHeight, format);
        RenderTexture reference = Target(width, height, GraphicsFormat.R32G32B32A32_SFloat);
        RenderTexture fused = Target(width, height, GraphicsFormat.R32G32B32A32_SFloat);
        int upKernel = shader.FindKernel("BloomUpsample");
        int compositeKernel = shader.FindKernel("CompositeFinal");
        int fusedKernel = shader.FindKernel("BloomUpsampleComposite");
        shader.SetFloat("_BloomRadius", 1.37f);
        shader.SetFloat("_BloomScatter", 0.5f);
        shader.SetFloat("_BloomIntensity", 0.25f);
        shader.SetVector("_SourceTexelSize", new Vector4(1f / low.width, 1f / low.height, low.width, low.height));
        shader.SetVector("_ScreenSize", new Vector4(halfWidth, halfHeight, 1f / halfWidth, 1f / halfHeight));
        shader.SetTexture(upKernel, "_SourceTex", low);
        shader.SetTexture(upKernel, "_BaseTex", high);
        shader.SetTexture(upKernel, "_DestTex", up);
        shader.Dispatch(upKernel, (halfWidth + 7) / 8, (halfHeight + 7) / 8, 1);
        shader.SetVector("_ScreenSize", new Vector4(width, height, 1f / width, 1f / height));
        shader.SetTexture(compositeKernel, "_InputTex", scene);
        shader.SetTexture(compositeKernel, "_BloomTex", up);
        shader.SetTexture(compositeKernel, "_OutputTex", reference);
        shader.Dispatch(compositeKernel, (width + 7) / 8, (height + 7) / 8, 1);
        shader.SetInt("_BloomStorageFormat", format == GraphicsFormat.R16G16B16A16_SFloat ? 1 :
            format == GraphicsFormat.B10G11R11_UFloatPack32 ? 2 : 0);
        shader.SetTexture(fusedKernel, "_InputTex", scene);
        shader.SetTexture(fusedKernel, "_SourceTex", low);
        shader.SetTexture(fusedKernel, "_BaseTex", high);
        shader.SetTexture(fusedKernel, "_OutputTex", fused);
        shader.Dispatch(fusedKernel, (width + 15) / 16, (height + 15) / 16, 1);
        Color[] expected = Read(reference);
        Color[] actual = Read(fused);
        float maxError = 0f;
        for (int i = 0; i < expected.Length; i++)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                float error = Mathf.Abs(actual[i][channel] - expected[i][channel]);
                maxError = Mathf.Max(maxError, error);
                // ReadPixels stores half; the reference additionally has a typed
                // texture-filter rounding step. Bound both by their numeric ULPs.
                float readbackUlp = Mathf.Pow(2f, Mathf.Floor(Mathf.Log(Mathf.Max(expected[i][channel], 0.000061035f), 2f)) - 10f);
                int bits = format == GraphicsFormat.B10G11R11_UFloatPack32 ? (channel == 2 ? 5 : 6) : 10;
                float bloomUlp = Mathf.Pow(2f, Mathf.Floor(Mathf.Log(Mathf.Max(expected[i][channel] * 4f, 0.000061035f), 2f)) - bits) * 0.25f;
                Assert.That(actual[i][channel], Is.EqualTo(expected[i][channel]).Within(2f * readbackUlp + bloomUlp),
                    $"{format}, {width}x{height}, pixel {i}, channel {channel}, constant={constant}");
            }

            if (constant && !impulse)
            {
                // Independent known-value oracle: source + (high + low * .5) * .25.
                Assert.That(actual[i].r, Is.EqualTo(1.125f).Within(0.00001f));
                Assert.That(actual[i].g, Is.EqualTo(2.25f).Within(0.00001f));
                Assert.That(actual[i].b, Is.EqualTo(4.5f).Within(0.00001f));
                Assert.That(actual[i].a, Is.EqualTo(0.375f).Within(0.00001f));
            }
            else if (impulse)
            {
                // Independent closed-form corner impulse: clamp-to-edge followed
                // by bilinear tent weights, not a call to either production kernel.
                float wx = Mathf.Clamp01(1f - Mathf.Max(0f, ((i % width) + 0.5f) * halfWidth / width - 0.5f));
                float wy = Mathf.Clamp01(1f - Mathf.Max(0f, ((i / width) + 0.5f) * halfHeight / height - 0.5f));
                wx = Mathf.Round(wx * 256f) / 256f;
                wy = Mathf.Round(wy * 256f) / 256f;
                float weight = wx * wy;
                // Packed interpolation has a numeric storage ULP; FP16 readback
                // adds another. The shape/edge oracle remains independent.
                float tolerance = format == GraphicsFormat.B10G11R11_UFloatPack32 ? 0.032f : 0.004f;
                Assert.That(actual[i].r, Is.EqualTo(0.125f + 4f * weight).Within(tolerance));
                Assert.That(actual[i].g, Is.EqualTo(0.25f + 2f * weight).Within(tolerance));
                Assert.That(actual[i].b, Is.EqualTo(0.5f + weight).Within(tolerance));
                Assert.That(actual[i].a, Is.EqualTo(0.375f).Within(0.00001f));
            }
        }

        TestContext.WriteLine($"Bloom fusion {format} {width}x{height}, constant={constant}, impulse={impulse}, max error={maxError:R}");
    }

    private Texture2D Input(int width, int height, bool constant, int layer)
    {
        var texture = RuntimeTextureFactory.CreateRGBAHalfNoMip(width, height, "BloomFusionInput",
            RuntimeTextureColorSpace.Linear, FilterMode.Bilinear, TextureWrapMode.Clamp);
        _owned.Add(texture);
        var colors = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = constant ? 1f : ((x * 17 + y * 31 + layer * 7) % 97) / 96f;
                colors[y * width + x] = layer switch
                {
                    0 => new Color(value * 0.125f, value * 0.25f, value * 0.5f, 0.375f),
                    1 => new Color(value * 2f, value * 4f, value * 8f, 1f),
                    _ => new Color(value * 4f, value * 8f, value * 16f, 1f),
                };
            }
        }

        texture.SetPixels(colors);
        texture.Apply(false, false);
        return texture;
    }

    private RenderTexture Target(int width, int height, GraphicsFormat format)
    {
        var target = new RenderTexture(new RenderTextureDescriptor(width, height)
        {
            graphicsFormat = format,
            depthBufferBits = 0,
            enableRandomWrite = true,
            msaaSamples = 1,
        })
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _owned.Add(target);
        Assert.That(target.Create(), Is.True, $"Cannot create {format}");
        return target;
    }

    private Color[] Read(RenderTexture target)
    {
        var texture = RuntimeTextureFactory.CreateRGBAHalfNoMip(target.width, target.height, "BloomFusionReadback",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        _owned.Add(texture);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            return texture.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }
}
