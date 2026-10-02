using System.Reflection;
using Kern.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Kern.FrameHarness.Tests;

/// <summary>Managed decision-path proof. GPU submission boundaries are spies, not a renderer.</summary>
[TestFixture]
public sealed class LightingInvalidationSourceTests
{
    private Assembly _production = null!;

    [OneTimeSetUp]
    public void CompileProductionDecisionPath() => _production = Compile(injectCompositeFault: false);

    [TestCase("culled", 0, 0, 0)]
    [TestCase("capacity", 0, 0, 0)]
    [TestCase("stable", 0, 0, 0)]
    [TestCase("move", 1, 1, 0)]
    [TestCase("enter", 1, 1, 0)]
    [TestCase("leave", 1, 1, 1)]
    [TestCase("explicit", 1, 0, 0)]
    [TestCase("remove", 1, 1, 1)]
    [TestCase("zero", 1, 1, 1)]
    [TestCase("remove-culled", 0, 0, 0)]
    [TestCase("color", 1, 1, 0)]
    [TestCase("intensity", 1, 1, 0)]
    public void OnlyChangedGPUInputsOrExplicitRefreshSubmit(
        string scenario, int submissions, int changedInputs, int clears)
    {
        int[] result = Run(_production, scenario);
        Assert.That(result, Is.EqualTo(new[] { submissions, submissions, 0, changedInputs, clears, 1 }));
    }

    [Test]
    public void CompositeFaultInjectionReproducesCulledSourceSubmission()
    {
        // The old defect is injected only into an in-memory compilation.
        // Both assemblies execute the same production manager and coordinator.
        Assembly faulty = Compile(injectCompositeFault: true);
        Assert.That(Run(faulty, "culled")[0], Is.EqualTo(2));
        Assert.That(Run(_production, "culled")[0], Is.Zero);
    }

    [TestCase("refresh-move")]
    [TestCase("refresh-remove")]
    public void ExplicitRefreshCoversWholeFieldEvenWithDynamicChanges(string scenario)
    {
        int[] result = Run(_production, scenario);
        Assert.That(result[0], Is.EqualTo(1));
        Assert.That(_production.GetType("Kern.World.Lighting.IndirectLightingSolver")!
            .GetField("LastPartial")!.GetValue(null), Is.Null);
    }

    [Test]
    public void OrdinaryMovementKeepsPartialComposite()
    {
        Run(_production, "move");
        Assert.That(_production.GetType("Kern.World.Lighting.IndirectLightingSolver")!
            .GetField("LastPartial")!.GetValue(null), Is.Not.Null);
    }

    [TestCase("outside-journal", 0)]
    [TestCase("padding-journal", 1)]
    [TestCase("unjournaled", 1)]
    public void SpatialJournalScopesTerrainRebuildWithoutIgnoringUnknownRevisions(string scenario, int rebuilds)
    {
        int[] result = Run(_production, scenario);
        Assert.That(result[0], Is.EqualTo(rebuilds));
        Assert.That(result[2], Is.EqualTo(rebuilds));
    }

    [Test]
    public void RemovalAfterFullRefreshRetainsThePreviousDynamicRegion()
    {
        Run(_production, "remove-after-refresh");
        Assert.That(_production.GetType("Kern.World.Lighting.IndirectLightingSolver")!
            .GetField("LastPartial")!.GetValue(null), Is.Not.Null);
    }

    private static int[] Run(Assembly assembly, string scenario) =>
        (int[])assembly.GetType("DecisionProbe")!.GetMethod("Run")!.Invoke(null, new object[] { scenario })!;

    private static Assembly Compile(bool injectCompositeFault)
    {
        string root = FindRoot();
        string lighting = Path.Combine(root, "Assets/Scripts/World/Lighting");
        SyntaxNode engine = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(lighting, "Core/LightingEngine.cs"))).GetRoot();
        string setter = engine.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "SetDynamicLight").ToFullString();
        if (injectCompositeFault)
        {
            Assert.That(setter, Does.Contain("_runtimeState.HasRenderedLightState = false;"));
            setter = setter.Replace("_runtimeState.HasRenderedLightState = false;", "_runtimeState.CompositeDirty = true;");
        }
        string debugView = engine.DescendantNodes().OfType<EnumDeclarationSyntax>()
            .Single(type => type.Identifier.Text == "DebugView").ToFullString();
        string wrapper = "using UnityEngine; namespace Kern.World.Lighting { public class LightingEngine { " +
            "private readonly DynamicLightManager _dynamicLightManager; private readonly LightingRuntimeState _runtimeState; " +
            "internal LightingEngine(DynamicLightManager lights, LightingRuntimeState state) { _dynamicLightManager = lights; _runtimeState = state; } " +
            debugView + setter + " } }";
        string[] productionFiles =
        [
            "Dynamic/DynamicLightManager.cs", "Core/LightingRuntimeState.cs", "Core/LightingUpdateCoordinator.cs",
            "Core/LightingFrameTypes.cs", "Core/LightingInvalidationFlags.cs", "Core/LightingRuntimeInvalidation.cs",
            "Core/LightingAmbientOcclusionUpdatePolicy.cs", "Quality/Contracts/LightingQualityMode.cs",
        ];
        var trees = productionFiles.Select(file => CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(lighting, file)), path: file)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(wrapper, path: "ProductionSetter.cs"));
        trees.Add(CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(lighting, "Core/LightingFrameExecutor.cs"))
                .Replace("LightingFrameExecutor", "ProductionFrameExecutor"), path: "ProductionFrameExecutor.cs"));
        trees.Add(CSharpSyntaxTree.ParseText(_BoundarySpies, path: "BoundarySpies.cs"));
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platform.Append(typeof(FrameTelemetry).Assembly.Location).Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("LightingDecisionProof" + Guid.NewGuid().ToString("N"),
            trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(result.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning),
            Is.Empty, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(result.Success, Is.True);
        return Assembly.Load(stream.ToArray());
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private const string _BoundarySpies = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using Kern.Core;
        using Kern.World.Lighting;
        using Kern.World.Lighting.Quality;
        using UnityEngine;
        using UnityEngine.Rendering;
        namespace UnityEngine
        {
            public class Object { }
            public class Camera { public bool orthographic = true; }
            public class RenderTexture { }
            public class ComputeBuffer { }
            public readonly record struct Vector2(float x, float y)
            { public static Vector2 operator *(Vector2 v, float f) => new(v.x*f, v.y*f); }
            public readonly record struct Vector2Int(int x, int y) { public static Vector2Int zero => default; }
            public readonly record struct Vector4(float x, float y, float z, float w);
            public readonly record struct Color(float r, float g, float b, float a = 1)
            { public static Color clear => default; }
            public readonly record struct RectInt(int x, int y, int width, int height)
            { public int xMin => x; public int yMin => y; public int xMax => x+width; public int yMax => y+height; }
            public static class Mathf
            {
                public static int RoundToInt(float f) => (int)MathF.Round(f);
                public static int Clamp(int v, int min, int max) => Math.Clamp(v,min,max);
                public static float Max(float a, float b) => MathF.Max(a,b);
                public static float Log(float f) => MathF.Log(f);
            }
            public static class Shader { public static void EnableKeyword(string keyword) { } }
            public static class Graphics { public static int Submissions; public static void ExecuteCommandBuffer(CommandBuffer buffer) => Submissions++; }
        }
        namespace UnityEngine.Rendering
        {
            public readonly struct AsyncGPUReadbackRequest { }
            public sealed class CommandBuffer
            {
                public int sizeInBytes => 0;
                public void SetComputeBufferParam(object shader, int kernel, string name, ComputeBuffer buffer) { }
                public void CopyTexture(RenderTexture source, RenderTexture destination) { }
                public void RequestAsyncReadback(RenderTexture source, Action<AsyncGPUReadbackRequest> callback) =>
                    throw new InvalidOperationException("GPU readback is outside this CPU decision fixture.");
                public void Clear() { }
                public void BeginSample(string s) { }
                public void EndSample(string s) { }
                public void SetBufferData<T>(ComputeBuffer b, T[] data, int start, int offset, int count) { }
                public void SetRenderTarget(RenderTexture texture) { }
                public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor) { }
            }
        }
        namespace Unity.Profiling
        {
            public readonly struct ProfilerMarker(string name)
            { public string Name => name; public Kern.Core.Interfaces.Diagnostics.Scope Auto() => new(); }
        }
        namespace Kern.Core
        { public static class ProjectRuntimeContracts { public static class World { public const float CellSize = 1; } } }
        namespace Kern.Core
        { public sealed class SettingLabelAttribute(string label) : Attribute { public string Label => label; } }
        namespace Kern.Core.Interfaces.Diagnostics
        {
            public readonly struct Scope : IDisposable { public void Dispose() { } }
            public static class AllocationLedger
            { public sealed class Entry { } public static Entry Register(string n) => new(); public static Scope Measure(Entry e) => new(); }
            public static class FrameEventLog { public static void Record(string s) { } }
        }
        namespace Kern.Core.Interfaces.WorldLighting
        { public interface ILightingGeometryContributor { ulong LightingGeometryRevision { get; } } }
        namespace Kern.Rendering { public struct GraphicsQualitySettings { public int LightingMinimumPixelsPerCell; } }
        namespace Kern.World.Lighting.Diagnostics
        { internal sealed class LightingInvalidationJournal { public void Record(ulong count, LightingInvalidationFlags flags, string reason, List<string> stages, string[] extra) { } } }
        namespace Kern.World.Lighting
        {
            [Flags] public enum LightingFeatureFlags { StaticRC = 1, DynamicLights = 2 }
            public static class LightingConfigHolder
            { public static float EmissionScale => 1; public static LightingFeatureFlags EnabledFeatures => LightingFeatureFlags.StaticRC | LightingFeatureFlags.DynamicLights; public static int AmbientOcclusionPixelsPerCell => 8; }
            public static class LightingQualityTuningController { public const int FieldPixelsPerCell = 32; }
            public static class LightingComputeBinder
            {
                public const float InvisibleDynamicRadiance = 0.001f; public static float ResolveMinimumExtinction() => 1;
                public static bool UpdateInvisibleDynamicRadiance(int sources) => false;
                public static void BindSharedParameters(CommandBuffer c, object shader, int width, int height,
                    int lightWidth, int lightHeight, Vector4 rect,
                    float cell, LightingEngine.DebugView view, RenderTexture material, RenderTexture emission,
                    int solve, int resolve, int composite, int gridWidth, int gridHeight) { }
            }
            internal sealed class LightingResourceManager
            {
                public CommandBuffer LightingCommandBuffer = new();
                public void EnsureReanchorFields() { } public void EnsureReanchorChangeBinding() { }
                public ComputeBuffer ReanchorChanges = new();
                public RenderTexture ReanchorMaterial = new();
                public RenderTexture ReanchorEmission = new();
                public RenderTexture StaticEmissionField = new(); public RenderTexture StaticDirectTexture = new();
                public ComputeBuffer DynamicLightBuffer = new(); public RenderTexture DirectTexture = new();
                public object LightingCompute => new(); public RenderTexture MaterialField => new();
                public int FieldWidth => 64; public int FieldHeight => 64; public int LightWidth => 64; public int LightHeight => 64; public int CellGridWidth => 64; public int CellGridHeight => 64;
                public int SolveCascadeKernel => 0; public int ResolveDirectKernel => 1; public int CompositeLightingKernel => 2;
                public long EstimatedCascadeRayWorkUnits => 0; public long EstimatedCascadeDispatchThreads => 0;
            }
            internal sealed class LightingGPULifecycle
            {
                public void ReleaseResources() { } public void EnsurePipeline() { }
                public bool EnsureResources(int w, int h, Camera c, in Kern.Rendering.GraphicsQualitySettings s,
                    out bool textureLimited, out bool cascadeLimited, out int pixels)
                { textureLimited = cascadeLimited = false; pixels = 1; return false; }
            }
            internal sealed class LightingPresentation
            {
                public const string WorldLightingKeyword = "fixture";
                public bool IsDisabledStatePublished => false;
                public void PublishDisabled() { } public void MarkEnabled() { }
                public void Publish(LightingEngine.DebugView view, Vector4 rect, float cell) { }
            }
            internal sealed class LightingGeometryRegistry { public ulong GeometryRevision => 1; }
            internal sealed class GeometryLightingSolver
            {
                public void RecordAmbientOcclusionField(CommandBuffer c, Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor g, LightingGeometryRegistry r, Vector4 rect, RectInt? rasterRect = null) { }
                public void RecordMaterialField(CommandBuffer c, Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor g, LightingGeometryRegistry r, Vector4 rect) { }
                public void PrepareCaches(CommandBuffer c, bool materialFieldRebuilt) { }
            }
            internal sealed class StaticLightingSolver
            {
                public bool CanReuseStaticAtlas(Vector2Int d) => false;
                public void RecordTrace(CommandBuffer c, RenderTexture e, bool reuse, Vector2Int d, IReadOnlyList<RectInt> regions, bool mask, Vector4 rect) { }
                public void RecordResolve(CommandBuffer c, LightingEngine.DebugView v, RenderTexture e, RenderTexture direct) { }
            }
            internal sealed class DynamicLightingSolver
            {
                public void Release() { } public void InvalidateTiles() { }
                public bool LastSolveUnchanged => false;
                public void Record(CommandBuffer c, int count, Vector4 rect, float cell, bool rebuild,
                    LightingEngine.DebugView v, IFrameTelemetry t, out RectInt dirty, RectInt receiverRect) => dirty = new(4,4,8,8);
            }
            internal static class LightingReceiverCoverage
            {
                // This fixture exercises invalidation decisions with fixed coverage;
                // production GPU tests supply actual camera/world-grid coverage.
                public static RectInt GetRect(Camera camera, Vector4 rect, int width, int height, float cellSize) => new(0,0,width,height);
            }
            internal sealed class IndirectLightingSolver
            {
                public static RectInt? LastPartial;
                public void RecordComposite(CommandBuffer c, RectInt? partial, Vector4 rect, float cell, IFrameTelemetry t) => LastPartial = partial;
            }
            internal static class LightingRegionCalculator
            {
                // All fixtures hold the field fixed; this spy deliberately cannot prove reanchor behavior.
                public static Vector4 GetStableLightingRegion(int x, int y, int w, int h, Vector4 previous) => previous;
            }
            internal static class LightingRegionInvalidationPolicy
            { public static void OnRegionChanged(LightingRuntimeState state, bool changed, bool reuse, Vector4 region) { if (changed) { throw new InvalidOperationException(); } } }
            internal sealed class LightingAmbientOcclusionUpdater
            {
                public LightingAmbientOcclusionUpdater(LightingResourceManager r, LightingRuntimeState s, LightingFrameExecutor e,
                    LightingPresentation p, LightingGeometryRegistry g, IFrameTelemetry t,
                    Diagnostics.LightingInvalidationJournal j, List<string> stages) { }
                public void Update(int x, int y, int w, int h, Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor g,
                    Kern.Rendering.GraphicsQualitySettings s) => throw new InvalidOperationException("AO is outside this fixture.");
            }
            internal sealed class LightingFrameExecutor(DynamicLightManager lights)
            {
                private readonly ProductionFrameExecutor _production = new(new(), new(), new(), new(), new(), lights, new(), new FrameTelemetry());
                public int Records, Rebuilds, Changed, Clears;
                public bool CanReuseStaticAtlas(Vector2Int delta) => throw new InvalidOperationException("Scroll must stay dormant.");
                public void InvalidateDynamicTiles() { }
                public int UploadDynamicLights(CommandBuffer cmd, Vector4 rect, float cell, out bool changed) =>
                    lights.UploadDynamicLights(cmd, new ComputeBuffer(), rect, cell, out changed);
                public void ConfigureSharedComputeParameters(CommandBuffer cmd, Vector4 r, float c, RenderTexture t, LightingQualityMode q, LightingEngine.DebugView v) { }
                public LightingFrameResult Record(CommandBuffer cmd, LightingFrameRequest request,
                    Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor g, RenderTexture e, RenderTexture d)
                {
                    Records++; if(request.RebuildFields) { Rebuilds++; } if(request.DynamicLightsChanged) { Changed++; }
                    if(request.ClearDynamicRadiance) { Clears++; }
                    return _production.Record(cmd, request, g, e, d);
                }
            }
        }
        public static class DecisionProbe
        {
            private sealed class Geometry : Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
            { public ulong LightingGeometryRevision { get; set; } = 1; }
            public static int[] Run(string scenario)
            {
                var lights = new DynamicLightManager(); lights.EnsureCapacity(1);
                var state = new LightingRuntimeState { FieldDirty = false, CompositeDirty = false, HasRenderedLightState = true,
                    LastDynamicReceiverRect = new(0,0,64,64),
                    HasStaticRadianceState = true, LastVisibleRegion = new(0,0,64,64), LastTerrainGeometryRevision = 1, LastContributorGeometryRevision = 1 };
                var engine = new LightingEngine(lights, state);
                var executor = new LightingFrameExecutor(lights);
                var geometry = new Geometry();
                var coordinator = new LightingUpdateCoordinator(new(), state, new(), executor, new(), new(), lights, new FrameTelemetry(), new());
                void Tick() => coordinator.Update(0,0,10,10,new Camera(),geometry,new(), LightingQualityMode.PerPixel, LightingEngine.DebugView.FinalLighting,false,false);
                void Set(int id, float x) => engine.SetDynamicLight(id, new Vector2(x,4),new Color(1,1,1),1);
                if (scenario is "stable" or "move" or "leave" or "capacity" or "remove" or "zero" or "color" or "intensity" or "refresh-move" or "refresh-remove" or "remove-after-refresh") { Set(1,4); Tick(); }
                else if (scenario is "enter" or "remove-culled") { Set(1,10000); Tick(); }
                Graphics.Submissions = executor.Records = executor.Rebuilds = executor.Changed = executor.Clears = 0;
                switch (scenario)
                {
                    case "culled": Set(1,10000); Tick(); Set(1,10001); break;
                    case "capacity": Set(2,5); break;
                    case "stable": Set(1,4); break;
                    case "move": Set(1,4.125f); break;
                    case "enter": Set(1,4); break;
                    case "leave": Set(1,10000); break;
                    case "explicit": state.CompositeDirty = true; break;
                    case "remove": lights.RemoveDynamicLight(1); break;
                    case "zero": engine.SetDynamicLight(1,new Vector2(4,4),new Color(1,1,1),0); break;
                    case "remove-culled": lights.RemoveDynamicLight(1); break;
                    case "color": engine.SetDynamicLight(1,new Vector2(4,4),new Color(0.5f,1,1),1); break;
                    case "intensity": engine.SetDynamicLight(1,new Vector2(4,4),new Color(1,1,1),2); break;
                    case "refresh-move": state.CompositeDirty = true; Set(1,4.125f); break;
                    case "refresh-remove": state.CompositeDirty = true; lights.RemoveDynamicLight(1); break;
                    case "remove-after-refresh": state.CompositeDirty = true; Set(1,4.125f); Tick(); lights.RemoveDynamicLight(1); break;
                    case "outside-journal": geometry.LightingGeometryRevision = 2; state.StagedTerrainGeometryRevision = 2; break;
                    case "padding-journal": geometry.LightingGeometryRevision = 2; state.StagedTerrainGeometryRevision = 2;
                        state.QueueRegionInvalidation(new RectInt(50,30,1,1)); break;
                    case "unjournaled": geometry.LightingGeometryRevision = 2; break;
                    default: throw new ArgumentOutOfRangeException(nameof(scenario));
                }
                Tick();
                return [Graphics.Submissions, executor.Records, executor.Rebuilds, executor.Changed, executor.Clears, state.HasRenderedLightState ? 1 : 0];
            }
        }
        """;
}
