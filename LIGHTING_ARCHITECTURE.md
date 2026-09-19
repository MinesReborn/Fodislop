# Lighting Architecture

Документ описывает фактическую архитектуру освещения и границу между lighting, terrain и streaming.

## Главный dataflow

```text
Camera / Player
    ↓
TerrainViewportCalculator
    ↓
StreamingGovernor → resident window / target origin
    ↓
MapRegionProcessor → WorldLayer.SetRegion
    ↓
TerrainRenderer cache + mesh + textures
    ↓
LightingEngine.UpdateLighting
    ↓
LightingUpdateCoordinator
    ↓
GeometryField → GeometryCache
    ↓
Static CascadeTrace → CascadeResolve
    ↓
Dynamic Lighting
    ↓
Bounce
    ↓
Composite
    ↓
WorldLightTexture
```

Окно terrain и окно lighting связаны координатами world region, но имеют разные кэши и разные invalidation rules. Новый origin нельзя публиковать в presentation, пока все используемые terrain и lighting resources не готовы.

## Runtime ownership

```text
LightingEngine
  ├── LightingUpdateCoordinator  — причины invalidation и порядок кадра
  ├── LightingGpuLifecycle       — GPU resources
  ├── LightingPresentation       — global shader state / WorldLightRect
  └── LightingFrameExecutor      — порядок lighting stages
        ├── GeometryLightingSolver
        ├── StaticLightingSolver
        ├── DynamicLightingSolver
        └── IndirectLightingSolver

TerrainRenderer
  ├── TerrainViewportCalculator  — размер и origin terrain window
  ├── TerrainCellCache           — CPU ring cache
  ├── TerrainCellBuilder         — mesh/vertex data
  └── TerrainCellDataTextures    — cell/material textures

DummyMapStreamer
  ├── StreamingGovernor          — target window
  ├── WorldLayer.ReadChunk        — chunk source
  └── MapRegion packet            — delivery to client storage
```

## Lighting stages and contracts

### GeometryField / GeometryCache

**Reads**

- terrain/contributor geometry;
- world region.

**Writes**

- `_MaterialField`;
- `_StaticEmissionField`;
- `_CellSolidMask`;
- bounce geometry caches.

**May**

- sample geometry;
- rebuild field textures;
- build geometry-dependent caches.

**Must not**

- run static cascade DDA;
- publish final lighting.

### CascadeTrace

**Kernel**

- `SolveCascade`.

**Reads**

- `_MaterialField`;
- `_StaticEmissionField`;
- farther cascade in `_RadianceAtlas`.

**Writes**

- current cascade in `_RadianceAtlas`.

**May**

- run DDA/radiance transport;
- process cascades far-to-near.

**Must not**

- depend on DynamicLight buffers;
- write final lightmap.

### CascadeResolve

**Kernel**

- `ResolveDirect`.

**Reads**

- `_RadianceAtlas`.

**Writes**

- static direct texture.

**Must not**

- run DDA;
- rebuild geometry;
- sample dynamic light transport.

### DynamicLighting

**Kernels**

- `TraceDynamicPolar`;
- `SolveDynamicLighting`;
- `ComposeDynamicLighting`.

Dynamic lights are recomputed for every exact source position change. They are not tied to a cell transition and are not throttled by a timer.

### Bounce

**Kernels**

- `BuildBounceTaps`;
- `BuildBounceFilter`;
- `SolveDiffuseBounce`.

Geometry-dependent cache building may use DDA. Bounce solve only gathers cached data.

### Composite

**Kernel**

- `CompositeLighting`.

Combines static direct, dynamic direct, bounce, emissive and ambient inputs. It must not perform geometry traversal.

## Static invalidation policy

Current policy is conservative:

- initial state, resource resize, quality/config changes and geometry changes use full static solve;
- region invalidation can use dependency mask when the region did not move and the cost estimate is favourable;
- with the mask, each cascade dispatches a tight probe rect (`CascadeProbeRects`: dirty bounds expanded by interval reach + margin, 50% fallback to full) instead of the full grid; the per-entry early-out stays as a second net. Telemetry splits `cascadeFullEntries` vs `cascadePartialEntries`;
- region movement reuses the overlapping atlas entries (scroll) and solves only the uncovered strips dilated by each tier's interval reach, unioned with the tight dirty rect when edits ride along. Kept entries stay valid unless their rays (up to the tier interval) can touch uncovered strips; far tiers dilate to the whole grid and solve full, where they are cheapest. Tiers whose probe lattice would change phase fall back to a full solve; resizing always goes full. Pending edits inside the new field are retained (not dropped, or kept entries would stay stale) and drain through the regular budgeted activation over the next frames instead of spiking the move frame;
- dynamic light movement does not invalidate static cascades.

The dependency-mask path dispatches a tight per-cascade probe rect and early-outs unchanged entries inside it. It reduces DDA work when the mask rejects candidates and dispatch threads when the dirty area is small (far cascades with huge intervals fall back to full grid, where they are cheapest).

## Streaming policy

`StreamingPolicy.Default` currently uses:

- allocation quantum: 32 cells;
- map window dimension: 128 cells before other sizing/padding rules;
- minimum dimension: 2;
- maximum dimension: 384;
- shrink hysteresis: 2 quanta.

`StreamingGovernor` advances the target origin by the policy quantum when the viewport approaches the prefetch margin. Terrain and lighting therefore still have discrete origin transitions, even though movement itself is continuous.

Terrain requests its actual target plus halo through the optional `IWorldRegionRequester` capability of the offline transport. The player-centred streaming window alone does not guarantee terrain coverage. Cold client chunks are requested with nonblocking `ReadChunk` before readiness is evaluated. Presentation follows the current camera within the committed cache while a new window is pending.

`DummyMapStreamer.SendMapWindowAsync` is shared by player-centred and explicit terrain requests. It starts the missing disk reads before awaiting them and prepares one atomic packet. There is no artificial yield per four prepared chunks; packet delivery and `WorldLayer.SetRegion` remain batch/synchronous. Cancellation or a failed read prevents partial publication. This fixes forced waiting, not the full static-solve cost.

## Non-negotiable invariants

- DDA ownership stays in transport stages only.
- Static lighting must not be limited by frequency or cell transitions.
- Dynamic light tracing follows exact smooth dynamic light position.
- Server Y-down and Unity Y-up conversion goes through `CoordinateUtils`.
- A new terrain/lighting window is published only after its required resources are resident and coherent.
- Dirty regions retain their identity until the consuming stage has used them.
- Telemetry distinguishes frame-local deltas from cumulative counters.
- Every new expensive path must expose dispatch, thread, texture and DDA cost.

## Known active defect

The unresolved movement defect is a synchronization/performance defect at the boundary between:

```text
streaming packet → WorldLayer.SetRegion
             → terrain cache/mesh update
             → lighting invalidation/solve
             → presentation
```

Symptoms are a frame hitch and one-frame square/unloaded areas. The current dump proves repeated full static transport, but does not yet prove that transport is the sole hitch source.

## Safe diagnostic order

1. Capture one movement crossing with frame-local and cumulative counters.
2. Split timings for packet preparation, packet apply, terrain phases and lighting phases.
3. Verify resident-window completeness and resource versions.
4. Fix the first proven expensive or incoherent boundary.
5. Only then revisit partial cascade propagation or atlas reuse.
