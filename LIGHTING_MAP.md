# Lighting and Streaming Map

Быстрый lookup для агента. Сначала определить стадию и owner ресурса, затем читать только указанный файл.

## Симптом → где смотреть

| Симптом | Файл | Точка входа |
| - | - | - |
| Фриз при переходе через границу окна | `Assets/Scripts/Networking/Connection/Client/Systems/DummyMapStreamer.cs` | `SendMapChunksAroundAsync` |
| Чанк применяется и вызывает каскад событий | `Assets/Scripts/World/Persistence/WorldLayer.cs` | `SetRegion`, `BeginChunkLoadBatch`, `EndChunkLoadBatch` |
| Packet batch применяется | `Assets/Scripts/Networking/Processors/MapRegionProcessor.cs` | `Process`, `BeginBatch`, `EndBatch` |
| Почему пришла lighting invalidation | `Assets/Scripts/World/Terrain/Core/TerrainRenderer.cs` | `HandleRegionChanged`, `OnCellLayerChunkLoaded` |
| Почему static solve полный | `Assets/Scripts/World/Lighting/Core/LightingUpdateCoordinator.cs` | `regionChanged`, `geometryChanged`, `FieldDirty`, `RecordLightingFrame` |
| Dependency mask | `Assets/Scripts/World/Lighting/Core/StaticLightingSolver.cs` | `ShouldUseDependencyMask`, `RecordCascade` |
| Почему atlas scroll сломал свет | `Assets/Scripts/World/Lighting/Core/StaticLightingSolver.cs` | `RecordScroll`, `RecordCascadeStrips` |
| Scroll kernel | `Assets/Resources/Shaders/Lighting/Cascades/CascadeTrace.hlsl` | `ScrollRadianceAtlas` |
| Как считается resident terrain window | `Assets/Scripts/World/Terrain/Core/TerrainRenderer.cs` | `IsTerrainWindowResident` |
| Размер и origin terrain window | `Assets/Scripts/World/Terrain/Core/TerrainViewportCalculator.cs` | `CalculateDimensions`, `ResolveGridPosition` |
| Governor и quantum | `Assets/Scripts/World/Streaming/StreamingGovernor.cs` | `SelectTargetOrigin`, `Plan` |
| Политика quantum/padding | `Assets/Scripts/World/Streaming/StreamingPolicy.cs` | `Default`, `ResolvePrefetchMarginCells` |
| Ring cache scroll | `Assets/Scripts/World/Terrain/Cache/TerrainCellCache.cs` | `ScrollAndFill` |
| Incremental mesh band | `Assets/Scripts/World/Terrain/Gpu/TerrainCellBuilder.cs` | `ScrollAndBuildBand` |
| Texture upload | `Assets/Scripts/World/Terrain/Gpu/TerrainCellDataTextures.cs` | `Apply`, `ApplyPatch` |
| Terrain dirty patch | `Assets/Scripts/World/Terrain/Core/TerrainRenderer.cs` | `UpdateDirtyCells`, `CoalesceOversizedDirtyRects` |
| Global light texture/origin | `Assets/Scripts/World/Lighting/Core/LightingPresentation.cs` | `Publish` |
| Frame counters | `Assets/Scripts/World/Common/FrameTelemetry.cs` | reset/accumulate semantics |
| Dump current frame | `Assets/Scripts/World/Lighting/Diagnostics/LightingFrameDumper.cs` | config/counters/textures |
| CPU transport oracle | `tools/lighting-tests/LightingOracle.cs` | brute-force reference |
| HLSL transport tests | `tools/lighting-tests/NativeHarness.cs` + `NativeTransport*.cpp` | actual transport functions |
| Layout/binding checks | `tools/lighting-tests/Program.cs` | production source checks |

## Dataflow lookup

```text
Camera
  → TerrainViewportCalculator
  → StreamingGovernor
  → DummyMapStreamer
  → MapRegionProcessor
  → WorldLayer.SetRegion
  → TerrainRenderer cache/mesh/textures
  → LightingEngine.UpdateLighting
  → LightingUpdateCoordinator
  → GeometryField / GeometryCache
  → CascadeTrace / CascadeResolve
  → DynamicLighting
  → Bounce
  → Composite
  → WorldLightTexture
```

## Current facts

- Static cascade atlas scroll/reuse is **disabled** after a visual regression.
- Full static solve still happens on region movement and relevant geometry invalidation.
- Dependency mask exists, but first prototype still launches the full cascade dispatch grid.
- `DummyMapStreamer` yields only while a chunk is still loading (no artificial yield per N chunks).
- Packet apply and terrain update remain synchronous boundaries.
- `IsTerrainWindowResident` samples one cell per intersecting chunk; this is a weak completeness check and a likely place to inspect for one-frame squares.
- `terrainFullPopulateCount = 1` in the latest dump, while terrain rebuilds and chunk loads are repeated.
- Latest dump showed `ScrollTerrain` with a 32-cell Y delta, but lighting atlas scroll counter was zero because that path is disabled.

## Cost rules

- `TraceLightSegment` / `TraceRadianceSegment`: O(crossed texels), geometry traversal.
- `SampleCascadeBilinear`: four atlas reads plus interpolation.
- `SolveDynamicLighting`: polar cache lookup per field pixel.
- `ComposeDynamicLighting`: sum of dynamic tiles per pixel.
- Full static cascade estimate is in dump `estimatedCascadeRayWorkUnits`.
- Do not infer GPU cost from CPU line count alone. Track dispatches, threads, DDA visits, atlas reads/writes and upload bytes.

## Verification commands without Unity

```bash
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- all
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- transport
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- equivalence Assets/Resources/Shaders/Lighting/WorldLighting.compute
dotnet run --project tools/lighting-tests/Kern.LightingTests.csproj -- compile
dotnet test tools/Kern.TerrainTests/Kern.TerrainTests.csproj --no-restore
git diff --check
```

Unity runtime validation is required for the final freeze/artifact verdict, but this repository's agent instructions prohibit launching or controlling Unity without an explicit concrete Unity operation from the user.
