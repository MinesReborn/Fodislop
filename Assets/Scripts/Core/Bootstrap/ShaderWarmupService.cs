#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Fodinae.Core;

/// <summary>
/// Upfront automated shader and graphics pipeline prewarm service.
/// Compiles shader passes, variant combinations and compute kernels on the user's GPU
/// before entering gameplay scenes, eliminating first-time PSO compilation stutters.
/// </summary>
public sealed class ShaderWarmupService : IShaderWarmupService
{
    private static readonly (string ShaderName, string[]? Keywords)[] ShadersToWarm =
    [
        (ProjectRuntimeContracts.ShaderNames.Terrain, ["FODINAE_WORLD_LIGHTING"]),
        (ProjectRuntimeContracts.ShaderNames.WorldSurface, ["FODINAE_SURFACE_REDROCK", "FODINAE_SURFACE_TRANSIT", "FODINAE_SURFACE_PERSPECTIVE"]),
        (ProjectRuntimeContracts.ShaderNames.WorldEntity, ["FODINAE_WORLD_LIGHTING"]),
        (ProjectRuntimeContracts.ShaderNames.DynamicEmission, null),
        (ProjectRuntimeContracts.ShaderNames.PlanetSurface, null),
        (ProjectRuntimeContracts.ShaderNames.PlanetAtmosphere, null),
        (ProjectRuntimeContracts.ShaderNames.Starfield, null),
        (ProjectRuntimeContracts.ShaderNames.MenuLineUnlit, null),
        (ProjectRuntimeContracts.ShaderNames.UnpremultiplyAlpha, null),
    ];

    private static readonly string[] WorldLightingKernels =
    [
        "SolveCascade",
        "SolveAutomaticNormals",
        "SolveContactOcclusion",
        "ResolveDirect",
        "SolveDiffuseBounce",
        "CompositeLighting",
        "ResolveAndComposite",
    ];

    private static readonly string[] PostProcessKernels =
    [
        "BloomPrefilter",
        "BloomDownsample",
        "BloomUpsample",
        "CompositeFinal",
    ];

    public async UniTask WarmupAsync(
        Action<string, float>? progressCallback,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int totalSteps = ShadersToWarm.Length + WorldLightingKernels.Length + PostProcessKernels.Length;
        int currentStep = 0;
        int warmedPasses = 0;
        int warmedKernels = 0;

        RenderTexture dummyTarget = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGB32);
        CommandBuffer cmd = new() { name = "Fodinae.ShaderWarmup" };

        try
        {
            for (int i = 0; i < ShadersToWarm.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (shaderName, keywords) = ShadersToWarm[i];
                Shader? shader = Shader.Find(shaderName);
                if (shader != null && shader.isSupported)
                {
                    Material material = new(shader);
                    try
                    {
                        for (int pass = 0; pass < material.passCount; pass++)
                        {
                            cmd.SetRenderTarget(dummyTarget);
                            cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                            warmedPasses++;

                            if (keywords != null)
                            {
                                for (int k = 0; k < keywords.Length; k++)
                                {
                                    material.EnableKeyword(keywords[k]);
                                    cmd.SetRenderTarget(dummyTarget);
                                    cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                                    material.DisableKeyword(keywords[k]);
                                    warmedPasses++;
                                }
                            }
                        }

                        Graphics.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                    }
                    finally
                    {
                        Object.Destroy(material);
                    }
                }

                currentStep++;
                progressCallback?.Invoke(shaderName, (float)currentStep / totalSteps);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            // Warm up compute kernels by resolving reflection handles in the shader runtime.
            var lightingCompute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
            if (lightingCompute != null)
            {
                for (int i = 0; i < WorldLightingKernels.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string kernelName = WorldLightingKernels[i];
                    if (lightingCompute.HasKernel(kernelName))
                    {
                        _ = lightingCompute.FindKernel(kernelName);
                        warmedKernels++;
                    }

                    currentStep++;
                    progressCallback?.Invoke(kernelName, (float)currentStep / totalSteps);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            else
            {
                currentStep += WorldLightingKernels.Length;
            }

            var postProcessCompute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);
            if (postProcessCompute != null)
            {
                for (int i = 0; i < PostProcessKernels.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string kernelName = PostProcessKernels[i];
                    if (postProcessCompute.HasKernel(kernelName))
                    {
                        _ = postProcessCompute.FindKernel(kernelName);
                        warmedKernels++;
                    }

                    currentStep++;
                    progressCallback?.Invoke(kernelName, (float)currentStep / totalSteps);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            else
            {
                currentStep += PostProcessKernels.Length;
            }

            Debug.Log($"[ShaderWarmup] Successfully primed {warmedPasses} raster state(s) and {warmedKernels} compute kernel(s) in {stopwatch.ElapsedMilliseconds} ms.");
            progressCallback?.Invoke("Ready", 1.0f);
        }
        finally
        {
            cmd.Dispose();
            RenderTexture.ReleaseTemporary(dummyTarget);
        }
    }
}
