#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Kern.Core;

public sealed class ShaderWarmupService : IShaderWarmupService
{
    private static readonly (string ShaderName, string[]? Keywords)[] _ShadersToWarm =
    [
        (ProjectRuntimeContracts.ShaderNames.Terrain, ["KERN_WORLD_LIGHTING"]),
        (ProjectRuntimeContracts.ShaderNames.WorldSurface, ["KERN_SURFACE_REDROCK", "KERN_SURFACE_TRANSIT", "KERN_SURFACE_PERSPECTIVE"]),
        (ProjectRuntimeContracts.ShaderNames.WorldEntity, ["KERN_WORLD_LIGHTING"]),
        (ProjectRuntimeContracts.ShaderNames.PlanetSurface, null),
        (ProjectRuntimeContracts.ShaderNames.PlanetAtmosphere, null),
        (ProjectRuntimeContracts.ShaderNames.Starfield, null),
        (ProjectRuntimeContracts.ShaderNames.MenuLineUnlit, null),
        (ProjectRuntimeContracts.ShaderNames.UnpremultiplyAlpha, null),
    ];

    private static readonly string[] _WorldLightingKernels =
    [
        "SolveCascade",
        "ScrollRadianceAtlas",
        "ResolveDirect",
        "ResolveTransmissionDebug",
        "SolveDiffuseBounce",
        "CompositeLighting",
    ];

    private static readonly string[] _PostProcessKernels =
    [
        "BloomPrefilter",
        "BloomDownsample",
        "BloomUpsample",
        "CompositeFinal",
        // Прогревались не все: проход дисплея и запекание таблицы грейда
        // отсутствовали, то есть два из шести ядер постпроцесса впервые
        // разрешались прямо в первом кадре игры.
        "DisplayFinal",
        "BakeGradeLut",
    ];

    public async UniTask WarmupAsync(
        Action<string, float>? progressCallback,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int totalSteps = _ShadersToWarm.Length + _WorldLightingKernels.Length + _PostProcessKernels.Length;
        int currentStep = 0;
        int warmedPasses = 0;
        int warmedKernels = 0;

        RenderTexture dummyTarget = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGB32);
        CommandBuffer cmd = new() { name = "Kern.ShaderWarmup" };

        try
        {
            for (int i = 0; i < _ShadersToWarm.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (shaderName, keywords) = _ShadersToWarm[i];
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
                        if (Application.isPlaying)
                        {
                            Object.Destroy(material);
                        }
                        else
                        {
                            Object.DestroyImmediate(material);
                        }
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
                for (int i = 0; i < _WorldLightingKernels.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string kernelName = _WorldLightingKernels[i];
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
                currentStep += _WorldLightingKernels.Length;
            }

            var postProcessCompute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);
            if (postProcessCompute != null)
            {
                for (int i = 0; i < _PostProcessKernels.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string kernelName = _PostProcessKernels[i];
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
                currentStep += _PostProcessKernels.Length;
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
