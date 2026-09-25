# Lighting Architecture

The normative ownership and change standard is
[`TERRAIN_LIGHTING_STANDARD.md`](TERRAIN_LIGHTING_STANDARD.md). This document
records the current pipeline and implementation behavior; where current code
differs from the standard, the difference is conformance debt, not a new rule.

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
TerrainLightingFrameSnapshot → LightingEngine.LateUpdate
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

## Статус миграции границы

Gate A: DTO, `ILightingGeometryContributor`, purpose-specific field contexts и
`ITerrainLightingExchange` находятся в `Kern.Contracts`; `TerrainLightingExchange`
зарегистрирован одним VContainer singleton в `GameLifetimeScope`. Проверка
`TerrainLightingExchangeTests` компилирует production contract/exchange без Unity
Editor, проверяет ровно одну DI binding-декларацию, VContainer singleton identity,
границы `Kern.Contracts`, поколения, последовательности, ack/retry, reset и
неизменяемость значений. Gate A source/tests готовы; Unity assembly/DI compile не выполнялся.

Gate B подключил Terrain journal к Lighting-owned pending invalidation state.
Terrain публикует region и named full-reset записи после успешной обработки
геометрии; Lighting принимает их перед текущим solve-вызовом и подтверждает
каждую запись только после переноса в своё состояние. Прямые вызовы
`LightingEngine.InvalidateRegion` и `InvalidateStaticCache` удалены из Terrain.
Тесты pump/applier проверяют retry, contiguous acknowledgement, world replacement,
полное сбрасывание старых участков, сохранение соседних sequence identities,
stable-region filtering и forced-full-reanchor policy.

Gate C source integration подключает требования до Terrain planning, committed
change/frame publication, generation-scoped frame snapshots, Lighting-owned
LateUpdate tick, success-only acknowledgements и output publication. Terrain protocol
sequence/pending-reset ownership вынесено в `TerrainLightingFramePublisher`; оно не
меняет очередь и порядок Terrain commit/plan/process. `dotnet test
tools/Kern.WorldLightingExchangeTests/Kern.WorldLightingExchangeTests.csproj
--no-restore` прошёл: 21 tests. Production Unity compile/PlayMode проверки frame
order, teleport presentation, GPU output rect и material binding не запускались,
поэтому acceptance Gate C остаётся pending.

Gate D boundary rule добавлено и автоматически обнаруживается default rule catalog;
fixtures для namespace imports, alias chains, fully qualified references, neutral
contract, nested UXML localization, и relocated composition roots проходят: 10 linter
tests passed. Targeted boundary lint на production source завершился с 0 нарушений.
Localization scanner теперь учитывает вложенные UXML: 202 ложных dead-key findings
исчезли без удаления ключей. Pattern rule применяет те же точечные исключения к
перенесённым файлам в `Core/Bootstrap/Scopes`: семь ложных Bootstrap findings
исчезли без изменения scope кода. Полный architecture linter проверил 54 rules и
завершился с exit code 0, 0 errors и 15 warnings. Полный вывод и классификация
сохранены в
`docs/architecture/evidence/terrain-lighting-architecture-linter-2026-09-24.txt` и
`docs/architecture/evidence/terrain-lighting-linter-classification-2026-09-24.md`.
Начальные 209 findings сохранены отдельно в
`docs/architecture/evidence/terrain-lighting-architecture-linter-initial-2026-09-24.txt`.
Boundary, naming, forbidden API и full-linter checks проходят; Gate D принят. Остаток
15 warnings перечислен в classification evidence.

## Terrain/Lighting lifecycle evidence (`TL-LIFE`)

| Owner | Lifecycle transition | Method and resources/publication |
| --- | --- | --- |
| `TerrainRenderer` | uninitialized → ready | `Awake` binds mesh/material owners; `Start` captures camera; `EnsureSubscriptions` binds world and texture notifications. |
| `TerrainRenderer` | ready → active | `LateUpdate` consumes Lighting requirements, commits/presents Terrain, then publishes committed change/frame snapshots. |
| `TerrainRenderer` | active → disposing → disposed | `OnDestroy` disposes subscriptions, presentation mesh state, diagnostics, and Terrain window resources. |
| `LightingEngine` | uninitialized → ready | `EnsureInitialized` creates Lighting resources and publishes Terrain planning requirements. |
| `LightingEngine` | ready → active | `LateUpdate` processes only a fresh exchange frame; successful coordinator update is followed by change/frame acknowledgement and output publication. |
| `LightingEngine` | active → disposing → disposed | `OnDestroy` releases coordinator/GPU lifecycle resources and prevents further tick work. |
| `TerrainLightingExchange` | scope creation → active → scope disposal | `GameLifetimeScope` registers exactly one singleton; it stores values and watermarks only and owns no Unity/GPU resources or callbacks. |

Frame-order, teleport presentation, shader output rect, binding validation and GPU
resource lifecycle remain unverified until a specifically authorized Unity runtime
operation is run.

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

## Terrain texture addressing and diagnostics

`TerrainSampling.hlsl` is the shared atlas-addressing path for the visible
terrain and lighting-field albedo. Displaced carriers recover per-cell atlas
UVs from the decoded cell geometry. Continuous sheet materials instead use the
fragment's actual cell-space position, so the texture follows distortion and
stays continuous across displaced cell boundaries. If a displaced fragment
crosses a cell edge, the resolver applies the periodic address available from
the current material layout. A grouped autotile retains its per-cell variant
because the neighbor's distinct descriptor and UV transform are not part of
the fragment contract; at a displaced edge, its transformed UV is clamped to
the selected variant edge instead of wrapping to the opposite side and making
a seam. The visible pass, material/emission field, AO field, and tile-identity
diagnostic use this same resolver.

Animation masks and terrain decals use the geometry-recovered per-cell UV.
Decals and faceted masks remain attached to the cell; continuous-sheet albedo
uses the actual displaced cell-space position. Neither path uses the carrier's
interpolated UV: carrier bounds expand for displaced cells, so that coordinate
stretches independently of the rendered terrain surface. The material/emission
field reconstructs the per-cell UV once and reuses it for animation and decals,
while the shared albedo resolver also receives the displaced cell-space
position.
The screen and field passes also choose the same point/linear atlas sampler from
`_PixelArtFiltering`; otherwise alpha cutouts and edge texels can disagree even
when their UVs match.

`TerrainDebugView.BackgroundTileIdentity` clips foreground fragments and colors
the resolved background atlas tile from its packed atlas slot and absolute
32-pixel atlas-grid coordinate. This is an injective 17-bit key for eight
atlases and the configured 4096-pixel atlas limit; animated frames are
classified from the final sampled UV. An invalid or out-of-range address is
magenta. Culling of fully covered background cells is bypassed only in the
visible terrain pass while this view is active. Material/emission and AO field
passes keep their normal culling and occupancy inputs.

The other terrain debug categories clip foreground fragments to the same
displaced silhouette as the visible pass. Coverage alone retains the full
foreground carrier to expose its cutouts; background quads remain rectangular
in all views.

Background autotile descriptors are computed from the resolved flood-fill
background types, not borrowed from the foreground descriptor. The metadata
warmup includes the one-cell neighborhood required by the eight-neighbor tile
group mask. Regional background rebuilds therefore need their adjacent fill
types warmed before parallel quad generation.

## Lighting stages and contracts

### GeometryField / GeometryCache

**Reads**

- terrain/contributor geometry;
- world region.

**Writes**

- `_MaterialField`;
- `_StaticEmissionField`;
- `_CellSolidMask`;
- `_AmbientOcclusionField` (quality-independent contact falloff field for terrain AO);
- bounce geometry caches.

The Standard graphics preset records only `_AmbientOcclusionField` when terrain
or contributor geometry or the stable world region changes. It allocates no
radiance atlas, material field, or lightmap and publishes a neutral white light
texture with the AO field. Overdrive records the same AO field in the full
lighting frame. Both presets therefore use the same terrain AO pass and shader
sample, while only Overdrive solves radiance transport.

Terrain mesh lighting metadata has one encoder,
`TerrainLightingData.cs`, and one shader decoder,
`TerrainLightingData.hlsl`. `TerrainAmbientOcclusion.hlsl` owns both AO sampling
and the receiver rule: every non-physical terrain surface receives AO, while
physical foreground mass does not darken itself. AO uses one spatial source:
the contact falloff field, sampled once at the receiver's transformed world position.
That field is rasterized from the same displaced cell coverage as the visible
terrain, including organic bends. Atlas alpha rejects transparent source texels;
internal alpha holes do not yet have a distance-based contact falloff. The cell-neighbor
mask is not combined into AO, so nominal grid directions cannot add shadows at
locations where displaced geometry no longer touches the receiver.

The displaced silhouette has one geometric predicate in
`TerrainContour.hlsl`: four corner vertices for regular cells, or those corners
plus four bend vertices for organic cells. Stored corners and derived bend
vertices are quantized to the 1/32-cell geometry grid before raster coverage;
fragment positions are not quantized again. The visible pass uses a hard mask
from that predicate, relief-rim distance measures those same sides from the
continuous fragment position, and AO
field contact fades over half a cell from the same signed distance.
The organic carrier quad uses the same bend strength, pivot, and snapped bend
vertices so rasterization cannot clip the polygon it is meant to cover. During
the AO-field draw only, the carrier expands by half a cell plus half an AO-field
texel in world units, converted to cell-local units by the terrain shader. This
keeps the full contact falloff available at extreme corners;
material/emission and visible passes use zero carrier padding.

**AO stage contract (`TL-STAGE`):** `GeometryLightingSolver.RecordAmbientOcclusionField`
records a full `DrawMesh` through `TerrainMeshManager.RenderLightingAmbientOcclusionField`
into one target using the dedicated `LightingAmbientOcclusionField` pass
(`Terrain.shader`). Shared vertex decoding and field-atlas alpha sampling live
in `TerrainLightingFieldCommon.hlsl`; AO owns only the occupancy fragment and
writes alpha to the ARGB32 target. Overlapping masses use Max blending to retain
the strongest contact value. Background and non-physical cell quads are culled
in the field vertex stage before rasterization. Remaining fragments outside
the contact radius or with transparent source alpha do not blend into the target.
The separate `LightingMaterialField` pass
writes the transport material/emission MRT and keeps hard physical occupancy in
material alpha. The visible color pass keeps its pixel-grid silhouette, while
the AO field uses signed distance to the same displaced edges and a smooth
half-cell falloff. This avoids directional copies of the silhouette and their
stepped outer edge. Flat cells use an analytic box distance; displaced cells
traverse polygon edges once and retain the nearest edge point. AO samples
exterior atlas alpha at that point, so a continuous sheet cannot switch to an
unrelated texel outside the displaced silhouette. Fragments beyond the
half-cell support skip the atlas read. Registered
contributors receive only the AO target: emission-only world entities issue no
AO draw, while world surfaces use a dedicated alpha-only occupancy pass. World
surface lighting meshes are rebuilt when the field rect or world dimensions
change and reused by the material and AO draws; the
`Kern.Surface.RebuildLightingMeshes` marker shows this rebuild. The field
is sized from the lighting cell grid and AO texture-dimension limit; there is no
scratch target, compute kernel, dispatch, or mip generation. The target is
cleared to transparent before the full mesh draw. `GeometryLightingSolver` owns
the field; terrain geometry revision or lighting-region/resource change triggers
its rebuild, and a stable frame reuses the published field. `LightingPresentation`
publishes the field and world mapping. The
visible terrain fragment in `Terrain.shader` samples mip zero at
`TransformObjectToWorld(cell.positionOS)` and applies receiver floor/strength.
Cost is one full field raster on rebuild, 4 bytes per AO texel, signed-distance
contact falloff for physical field fragments, and one bilinear sample per
receiving screen fragment; stable frames do not
rasterize the field. Standard-preset rebuilds also write their dimensions and
reason into `FrameEventLog`; `FrameStallMonitor` samples the
`Kern.Lighting.AmbientOcclusionField` marker beside render-thread stalls.
The half-cell carrier expansion grows a regular cell's field footprint from
one cell square to at most four cell squares on an invalidating rebuild; the
`Kern.Terrain.RenderAmbientOcclusionField` marker exposes that draw. AO field
storage remains four bytes per texel, with no additional texture or dispatch.
The surface pass preserves its existing hard physical occupancy; only terrain
geometry currently supplies distance-based contact beyond its visible edge.

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
- **region movement currently always solves the full static atlas.** `LightingUpdateCoordinator` pins `canReuseStaticAtlas = false` (rolled back 2026-09-19: scroll reuse correlated with a heavy playmode FPS drop, cause not isolated). Consequences, all observable in code: pending region invalidations are dropped on a move (`ClearPendingRegionInvalidation`), because the move re-solves everything anyway; the dependency mask is disabled on a move, because `allowStaticDependencyMask` requires `!regionChanged || canReuseStaticAtlas`; and the journal never records `Region moved (scroll)`. `CascadeScrollRecorder` and the strip machinery in `StaticLightingSolver` stay built but dormant;
- the scroll reuse **as designed** (dormant, restore by flipping that flag back to `regionChanged && !resourcesResized`): reuse the overlapping atlas entries and solve only the uncovered strips dilated by each tier's interval reach, unioned with the tight dirty rect when edits ride along. Kept entries stay valid unless their rays (up to the tier interval) can touch uncovered strips; far tiers dilate to the whole grid and solve full, where they are cheapest. Tiers whose probe lattice would change phase fall back to a full solve; resizing always goes full. Pending edits inside the new field are retained (not dropped, or kept entries would stay stale) and drain through the regular budgeted activation over the next frames instead of spiking the move frame;
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
