#nullable enable

using Kern.Core;
using Kern.World.Lighting.Diagnostics;

namespace Kern.World.Lighting;

/// <summary>
/// Граф объектов освещения, собранный целиком и один раз.
/// </summary>
///
/// Порядок сборки здесь не свободный: исполнитель кадра владеет солверами,
/// жизненный цикл GPU владеет исполнителем, координатор — всем сразу. Пока это
/// жило восемью ленивыми свойствами движка, порядок приходилось восстанавливать
/// по цепочке `??=`, а каждое обращение к любому из них могло создать половину
/// графа как побочный эффект.
///
/// Граф строится не в поле, а по первому требованию: его вход — внедрённые
/// зависимости (реестр геометрии, телеметрия), а их у MonoBehaviour на момент
/// инициализации полей ещё нет.
///
/// То, что граф не создан, — значимое состояние: значит, GPU-ресурсов тоже нет
/// и освобождать нечего. Движок проверяет это перед освобождением.
internal sealed class LightingComposition
{
    public LightingComposition(
        LightingResourceManager resources,
        LightingRuntimeState runtimeState,
        DynamicLightManager dynamicLightManager,
        LightingGeometryRegistry geometryRegistry,
        IFrameTelemetry telemetry,
        LightingInvalidationJournal journal)
    {
        var geometrySolver = new GeometryLightingSolver(resources);
        var staticSolver = new StaticLightingSolver(resources, telemetry);
        var dynamicSolver = new DynamicLightingSolver(
            resources, dynamicLightManager, new DynamicLightTileCache());
        var indirectSolver = new IndirectLightingSolver(resources);

        FrameExecutor = new LightingFrameExecutor(
            resources,
            geometrySolver,
            staticSolver,
            dynamicSolver,
            indirectSolver,
            dynamicLightManager,
            geometryRegistry,
            telemetry);
        GPULifecycle = new LightingGPULifecycle(resources, FrameExecutor);
        Presentation = new LightingPresentation(resources);
        UpdateCoordinator = new LightingUpdateCoordinator(
            resources,
            runtimeState,
            GPULifecycle,
            FrameExecutor,
            Presentation,
            geometryRegistry,
            dynamicLightManager,
            telemetry,
            journal);
    }

    public LightingFrameExecutor FrameExecutor { get; }

    public LightingGPULifecycle GPULifecycle { get; }

    public LightingPresentation Presentation { get; }

    public LightingUpdateCoordinator UpdateCoordinator { get; }
}
