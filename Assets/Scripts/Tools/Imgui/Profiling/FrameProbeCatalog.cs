#nullable enable

using System.Collections.Generic;

namespace Kern.Tools.Imgui.Profiling;

public static class FrameProbeCatalog
{
    // Время видеокарты по этим участкам снимается GPU-рекордером: маркер
    // сам по себе даёт только время записи команд на процессоре.
    public static List<FrameProbe> CreateGpuProbes() =>
    [
        new("Свет — весь блок", "Kern.RadianceCascades", gpu: true),
        new("· поле материалов", "Kern.Lighting.MaterialField", isDetail: true, gpu: true),
        new("· сборка эмиссии", "Kern.Lighting.ComposeEmission", isDetail: true, gpu: true),
        new("· статическая половина", "Kern.Lighting.StaticRadiance", isDetail: true, gpu: true),
        new("· динамическая половина", "Kern.Lighting.DynamicRadiance", isDetail: true, gpu: true),
        new("· каскады", "Kern.Lighting.RadianceCascades", isDetail: true, gpu: true),
        new("· композит", "Kern.Lighting.Composite", isDetail: true, gpu: true),
        new("Террейн — поля", "Kern.Terrain.RenderMaterialFields", gpu: true),
        new("Постпроцесс — композит", "Kern.PostProcess.Composite", gpu: true),
        new("· блум, префильтр", "Kern.PostProcess.Bloom.Prefilter", isDetail: true, gpu: true),
        new("· блум, вниз", "Kern.PostProcess.Bloom.Downsample", isDetail: true, gpu: true),
        new("· блум, вверх", "Kern.PostProcess.Bloom.Upsample", isDetail: true, gpu: true),
        new("· возврат в кадр", "Kern.PostProcess.BlitBack", isDetail: true, gpu: true),
        new("· копия истории", "Kern.PostProcess.HistoryCopy", isDetail: true, gpu: true),
    ];

    // Записи рендера этих же участков на процессоре.
    public static List<FrameProbe> CreateGpuRecordProbes() =>
    [
        new("Свет — запись блока", "Kern.RadianceCascades"),
        new("Террейн — запись полей", "Kern.Terrain.RenderMaterialFields"),
        new("Постпроцесс — запись композита", "Kern.PostProcess.Composite"),
    ];

    public static List<FrameProbe> CreateCpuProbes() =>
    [
        new("Движок — рендер и вывод", "PostLateUpdate.FinishFrameRendering", false, false,
            "RenderPipelineManager.DoRenderLoop_Internal()", "UniversalRenderPipeline.RenderCameraStack"),
        new("· камера URP", "UniversalRenderPipeline.RenderSingleCameraInternal", true, false,
            "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "RenderSingleCamera"),
        new("· запись RenderGraph", "RecordRenderGraph", true, false, "RenderGraph.RecordRenderGraph"),
        new("· исполнение RenderGraph", "ExecuteRenderGraph", true, false, "RenderGraph.Execute"),
        new("· вывод кадра (Present)", "Gfx.PresentFrame", true, false, "PostLateUpdate.PresentAfterDraw"),
        new("· ожидание потока рендера", "Gfx.WaitForPresentOnGfxThread", true, false, "Gfx.WaitForGfxCommandsFromMainThread"),
        new("Скрипты движка", "Update.ScriptRunBehaviourUpdate"),
        new("· FixedUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate", isDetail: true),
        new("· корутины", "Update.ScriptRunDelayedDynamicFrameRate", isDetail: true),
        new("· LateUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", isDetail: true),
        new("· сборка мусора", "GC.Collect", true, false, "GarbageCollector.CollectIncremental"),
        new("Террейн — весь этап", "Kern.Terrain.LateUpdate.CPU"),
        new("· кеш клеток", "Kern.Terrain.Cache", isDetail: true),
        new("· предрасчёт", "Kern.Terrain.Precalculate", isDetail: true),
        new("· заливка фона", "Kern.World.Terrain.BackgroundFloodFill", isDetail: true),
        new("· сборка меша", "Kern.Terrain.MeshBuild", isDetail: true),
        new("· заливка вершин", "Kern.Terrain.MeshUpload", isDetail: true),
        new("Свет — весь этап", "Kern.Lighting.UpdateLighting.CPU"),
        new("· запись команд", "Kern.Lighting.BuildCommands.CPU", isDetail: true),
        new("· выполнение команд", "Kern.Lighting.ExecuteCommands.CPU", isDetail: true),
        new("· загрузка источников", "Kern.Lighting.DynamicLights.Upload.CPU", isDetail: true),
        new("· запись каскадов", "Kern.Lighting.Cascades.Record.CPU", isDetail: true),
        new("· запись разрешения", "Kern.Lighting.Resolve.Record.CPU", isDetail: true),
        new("· запись композита", "Kern.Lighting.Composite.Record.CPU", isDetail: true),
        new("Поверхность", "Kern.Surface.LateUpdate"),
        new("Сущности мира", "Kern.WorldEntities.LateUpdate"),
        new("Постпроцесс", "Kern.PostProcess.LateUpdate"),
        new("Сеть — разбор очереди", "Kern.Net.DrainPacketQueue"),
    ];

    // В редакторе эти маркеры суммируют и перерисовку окон самого редактора.
    public static List<FrameProbe> CreateInterfaceProbes() =>
    [
        new("UI Toolkit — отрисовка панелей", "UIR.DrawChain"),
        new("UI Toolkit — обновление панелей", "UIElementsUpdateRuntimePanels", false, false, "UIElements.UpdateRuntimePanels"),
        new("IMGUI — перерисовка", "GUI.Repaint"),
        new("IMGUI — события", "GUI.ProcessEvents", false, false, "GUIUtility.ProcessEvent"),
    ];

    public static List<FrameProbe> CreateMemoryProbes() =>
    [
        new("Мусор за кадр", "GC Allocated In Frame"),
        new("Аллокаций за кадр", "GC Allocation In Frame Count"),
        new("Куча занята", "GC Used Memory"),
        new("Куча зарезервирована", "GC Reserved Memory"),
        new("Вся память движка", "Total Used Memory"),
        new("Память процесса", "App Resident Memory", false, false, "System Used Memory"),
        new("Графика", "Gfx Used Memory"),
        new("Память текстур", "Texture Memory"),
        new("Render target'ов", "Render Textures Count", false, false, "RenderTextures Count"),
        new("Память render target'ов", "Render Textures Bytes", false, false, "RenderTextures Bytes"),
    ];

    public static List<FrameProbe> CreateRenderCounters() =>
    [
        new("Вызовов отрисовки", "Draw Calls Count", false, false, "Draw Calls", "DrawCalls Count"),
        new("Смен материала", "SetPass Calls Count", false, false, "SetPass Calls", "SetPasses Count"),
        new("Пакетов", "Batches Count", false, false, "SRP Batches Count", "SRP Batches"),
        new("Треугольников", "Triangles Count", false, false, "Triangles"),
        new("Вершин", "Vertices Count", false, false, "Vertices"),
        new("Буферов в работе", "Used Buffers Count", false, false, "Buffers Count"),
    ];
}
