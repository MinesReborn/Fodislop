#nullable enable

using System;
using System.Linq;
using Fodinae.Core;
using Fodinae.World.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Tests.World
{
    [TestFixture]
    public sealed class ContactOcclusionE2ETests
    {
        [Test]
        public void ContactOcclusionKernel_IsAvailable()
        {
            RequireComputeShaders();
            ComputeShader shader = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
            Assert.That(shader, Is.Not.Null);
            Assert.That(
                shader.HasKernel(ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion),
                Is.True);
        }

        [Test]
        public void ContactOcclusionKernel_UsesEightByEightThreads()
        {
            RequireComputeShaders();
            ComputeShader shader = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion);
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out uint z);

            Assert.That(x, Is.EqualTo(8));
            Assert.That(y, Is.EqualTo(8));
            Assert.That(z, Is.EqualTo(1));
        }

        [Test]
        public void EmptyAndSolidCellsHaveWhiteContactOcclusion()
        {
            float[] empty = RunContactOcclusion(static (_, _) => false);
            float[] solid = RunContactOcclusion(static (_, _) => true);

            Assert.That(empty[(16 * 32) + 16], Is.EqualTo(1f).Within(0.001f));
            Assert.That(solid[(16 * 32) + 16], Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void ConcaveCornerIsDarkerThanFlatBoundary()
        {
            float[] flat = RunContactOcclusion(static (x, _) => x < 16);
            float[] corner = RunContactOcclusion(static (x, y) => x < 16 || y < 16);

            float flatValue = flat[(18 * 32) + 18];
            float cornerValue = corner[(18 * 32) + 18];
            Assert.That(cornerValue, Is.LessThan(flatValue));
        }

        [Test]
        public void DiagonalOnlyContactDoesNotLeaveAWhiteGap()
        {
            float[] result = RunContactOcclusion(static (x, y) => x < 16 && y < 16);

            Assert.That(result[(16 * 32) + 16], Is.LessThan(1f));
        }

        [Test]
        public void NarrowCavityIsDarkerThanAnOpenCorner()
        {
            float[] corner = RunContactOcclusion(static (x, y) => x < 16 || y < 16);
            float[] cavity = RunContactOcclusion(static (x, y) =>
                x <= 14 || x >= 18 || y <= 14 || y >= 18);

            Assert.That(cavity[(16 * 32) + 16], Is.LessThan(corner[(18 * 32) + 18]));
        }

        [Test]
        public void DirectRadianceDoesNotChangeWhenContactOcclusionChanges()
        {
            float withoutOcclusion = RunCompositeDebugValue(debugView: 5, ao: 1f);
            float withOcclusion = RunCompositeDebugValue(debugView: 5, ao: 0.1f);

            Assert.That(withOcclusion, Is.EqualTo(withoutOcclusion).Within(0.001f));
        }

        [Test]
        public void TransmissionDoesNotChangeWhenContactOcclusionChanges()
        {
            float withoutOcclusion = RunCompositeDebugValue(debugView: 4, ao: 1f);
            float withOcclusion = RunCompositeDebugValue(debugView: 4, ao: 0.1f);

            Assert.That(withOcclusion, Is.EqualTo(withoutOcclusion).Within(0.001f));
        }

        [Test]
        public void ContactOcclusionDarkensAmbientInFinalComposite()
        {
            Color withoutOcclusion = RunCompositeOutput(debugView: 0, ao: 1f);
            Color withOcclusion = RunCompositeOutput(debugView: 0, ao: 0.25f);

            Assert.That(withOcclusion.r, Is.LessThan(withoutOcclusion.r));
        }

        [Test]
        public void FinalCompositePublishesContactOcclusionInAlpha()
        {
            Color output = RunCompositeOutput(debugView: 0, ao: 0.25f);

            Assert.That(output.a, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void ContactOcclusionDoesNotChangeWhenExtinctionChanges()
        {
            float[] lowExtinction = RunContactOcclusion(
                static (x, y) => x < 16 || y < 16,
                emptyExtinction: 0.01f,
                solidExtinction: 0.1f);
            float[] highExtinction = RunContactOcclusion(
                static (x, y) => x < 16 || y < 16,
                emptyExtinction: 10f,
                solidExtinction: 10f);

            Assert.That(highExtinction, Is.EqualTo(lowExtinction));
        }

        [Test]
        public void ContactOcclusionIsDeterministic()
        {
            float[] first = RunContactOcclusion(static (x, y) =>
                (x is >= 12 and <= 19) && (y is >= 12 and <= 19));
            float[] second = RunContactOcclusion(static (x, y) =>
                (x is >= 12 and <= 19) && (y is >= 12 and <= 19));

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void SolidTenByTenArrayHasNoInternalOcclusionSeams()
        {
            float[] result = RunContactOcclusion(static (x, y) =>
                x is >= 11 and < 21 && y is >= 11 and < 21);

            for (int y = 12; y < 20; y++)
            {
                for (int x = 12; x < 20; x++)
                {
                    Assert.That(result[(y * 32) + x], Is.EqualTo(1f).Within(0.001f));
                }
            }
        }

        [Test]
        public void DynamicLightChangesDoNotRequestContactOcclusionSolve()
        {
            Assert.That(
                LightingEngine.ShouldDispatchContactOcclusion(
                    ambientOcclusionEnabled: true,
                    geometryOrRegionChanged: false,
                    ambientOcclusionSettingsChanged: false),
                Is.False);
        }

        [Test]
        public void GeometryChangesRequestContactOcclusionSolve()
        {
            Assert.That(
                LightingEngine.ShouldDispatchContactOcclusion(
                    ambientOcclusionEnabled: true,
                    geometryOrRegionChanged: true,
                    ambientOcclusionSettingsChanged: false),
                Is.True);
        }

        [Test]
        public void DisabledContactOcclusionDoesNotRequestSolve()
        {
            Assert.That(
                LightingEngine.ShouldDispatchContactOcclusion(
                    ambientOcclusionEnabled: false,
                    geometryOrRegionChanged: true,
                    ambientOcclusionSettingsChanged: true),
                Is.False);
        }

        private static void RequireComputeShaders()
        {
            // В batch/-nographics (в т.ч. CI) compute shaders недоступны —
            // ядро не компилируется, и HasKernel/FindKernel дают false/exception.
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Contact AO GPU tests require compute shaders (unavailable in batch/-nographics mode).");
            }
        }

        private static float[] RunContactOcclusion(
            Func<int, int, bool> isSolid,
            float emptyExtinction = 1f,
            float solidExtinction = 1f)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Contact AO GPU tests require compute shaders and async readback.");
            }

            const int size = 32;
            ComputeShader shader = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion);
            var source = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: true);
            var material = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
            {
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
            };
            var occlusion = new RenderTexture(size, size, 0, RenderTextureFormat.RHalf)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };

            try
            {
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        pixels[(y * size) + x] = new Color(0f, 0f, 0f, isSolid(x, y) ? 1f : 0f);
                    }
                }

                source.SetPixels(pixels);
                source.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                material.Create();
                occlusion.Create();
                Graphics.CopyTexture(source, material);
                material.GenerateMips();

                shader.SetInts("_FieldSize", size, size);
                shader.SetInts("_BounceSize", size, size);
                shader.SetVector("_WorldRect", new Vector4(size, size, size, size));
                shader.SetFloat("_CellSize", 1f);
                shader.SetFloat("_AmbientOcclusionRadiusCells", 3f);
                shader.SetFloat("_AmbientOcclusionStrength", 1.5f);
                shader.SetInt("_EnableContactOcclusion", 1);
                shader.SetVector("_EmptyExtinctionRgb", new Vector4(emptyExtinction, emptyExtinction, emptyExtinction, 0f));
                shader.SetVector("_SolidExtinctionRgb", new Vector4(solidExtinction, solidExtinction, solidExtinction, 0f));
                shader.SetInt("_MaterialYFlip", 0);
                shader.SetTexture(kernel, "_MaterialField", material);
                shader.SetTexture(kernel, "_ContactOcclusionTexture", occlusion);
                shader.Dispatch(kernel, size / 8, size / 8, 1);

                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                    occlusion,
                    0,
                    TextureFormat.RFloat);
                AsyncGPUReadback.WaitAllRequests();
                Assert.That(request.done, Is.True, "Async GPU readback did not complete synchronously in the EditMode test.");
                Assert.That(request.hasError, Is.False);
                return request.GetData<float>().ToArray();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                material.Release();
                occlusion.Release();
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(occlusion);
            }
        }

        private static float RunCompositeDebugValue(int debugView, float ao)
        {
            return RunCompositeOutput(debugView, ao).r;
        }

        private static Color RunCompositeOutput(int debugView, float ao)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                Assert.Ignore("Lighting GPU tests require compute shaders and async readback.");
            }

            const int size = 8;
            ComputeShader shader = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting);
            var material = CreateTestTarget(size, RenderTextureFormat.ARGB32);
            var emission = CreateTestTarget(size, RenderTextureFormat.ARGBHalf);
            var direct = CreateTestTarget(size, RenderTextureFormat.ARGBHalf);
            var bounce = CreateTestTarget(size, RenderTextureFormat.ARGBHalf);
            var occlusion = CreateTestTarget(size, RenderTextureFormat.RHalf);
            var result = CreateTestTarget(size, RenderTextureFormat.ARGBHalf);

            try
            {
                ClearTarget(material, Color.clear);
                ClearTarget(emission, Color.clear);
                ClearTarget(direct, new Color(0.6f, 0.2f, 0.1f, 1f));
                ClearTarget(bounce, Color.clear);
                ClearTarget(occlusion, new Color(ao, 0f, 0f, 1f));
                ClearTarget(result, Color.clear);
                shader.SetInts("_FieldSize", size, size);
                shader.SetInts("_BounceSize", size, size);
                shader.SetInt("_MaterialYFlip", 0);
                shader.SetInt("_DebugView", debugView);
                shader.SetInt("_EnableContactOcclusion", ao < 1f ? 1 : 0);
                shader.SetInt("_EnableDiffuseBounce", 0);
                shader.SetVector("_AmbientColor", Color.white);
                shader.SetFloat("_MaximumLightMultiplier", 4f);
                shader.SetTexture(kernel, "_MaterialField", material);
                shader.SetTexture(kernel, "_EmissionField", emission);
                shader.SetTexture(kernel, "_DirectInput", direct);
                shader.SetTexture(kernel, "_StaticDirectInput", direct);
                shader.SetTexture(kernel, "_BounceInput", bounce);
                shader.SetTexture(kernel, "_ContactOcclusionTexture", occlusion);
                shader.SetTexture(kernel, "_Result", result);
                shader.Dispatch(kernel, 1, 1, 1);

                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(
                    result,
                    0,
                    TextureFormat.RGBAFloat);
                AsyncGPUReadback.WaitAllRequests();
                Assert.That(request.done, Is.True, "Async GPU readback did not complete synchronously in the EditMode test.");
                Assert.That(request.hasError, Is.False);
                return request.GetData<Color>()[0];
            }
            finally
            {
                ReleaseTarget(material);
                ReleaseTarget(emission);
                ReleaseTarget(direct);
                ReleaseTarget(bounce);
                ReleaseTarget(occlusion);
                ReleaseTarget(result);
            }
        }

        private static RenderTexture CreateTestTarget(int size, RenderTextureFormat format)
        {
            var target = new RenderTexture(size, size, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };
            target.Create();
            return target;
        }

        private static void ClearTarget(RenderTexture target, Color color)
        {
            var commandBuffer = new CommandBuffer
            {
                name = "Contact AO test clear",
            };
            commandBuffer.SetRenderTarget(target);
            commandBuffer.ClearRenderTarget(false, true, color);
            Graphics.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();
        }

        private static void ReleaseTarget(RenderTexture target)
        {
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
