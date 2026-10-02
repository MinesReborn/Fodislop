#nullable enable

namespace Kern.World.Lighting;

/// <summary>
/// Освобождение GPU-ресурсов освещения: конвейер отдельно, ресурсы отдельно.
/// </summary>
///
/// Граф <see cref="LightingComposition"/> строится по требованию, поэтому
/// вызывающий передаёт его как есть — возможно null. Разница существенная: при
/// null освобождать надо менеджер ресурсов напрямую, а не ждать графа, который
/// на этот момент ещё не создавали.
internal static class LightingGPUTeardown
{
    public static void ReleasePipeline(
        LightingComposition? composition,
        LightingResourceManager resources,
        DynamicLightManager dynamicLights)
    {
        if (composition != null)
        {
            composition.GPULifecycle.ReleasePipeline();
            return;
        }

        resources.ReleaseGPUPipeline();
        dynamicLights.ResetUploadState();
    }

    public static void ReleaseResources(
        LightingComposition? composition,
        LightingResourceManager resources,
        DynamicLightManager dynamicLights,
        LightingRuntimeState runtimeState)
    {
        if (composition != null)
        {
            composition.GPULifecycle.ReleaseResources();
        }
        else
        {
            resources.ReleaseResources();
            dynamicLights.ResetUploadState();
        }

        runtimeState.HasStaticRadianceState = false;
        runtimeState.HasDynamicRadianceState = false;
    }
}
