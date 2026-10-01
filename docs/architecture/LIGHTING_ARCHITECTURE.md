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

Geometry, static albedo/emission, static/dynamic direct and the final lightmap
default to 32 texels per cell, independent of camera zoom and sparse cascade
probe budgets. The explicit `LightingConfigHolder.DefaultQuality` data block in
VisualTuning sets the defaults. `LightingQualityTuningController` owns validated
session overrides; fields share its `FieldPixelsPerCell`, while the world raster
and contact AO retain 32 pixels per cell. `CascadeProbePixelsPerCell` controls
only the requested first-cascade probe density. Layouts store probe spacing in dense
field texels; static DDA reads dense material/emission and ResolveDirect
bilinearly interpolates first-cascade entries to dense receivers without DDA.
AO also calculates 32 texels per cell directly in its terrain fragment pass.
Allocation limits fail explicitly instead of lowering these field densities.

Field row order has one owner, `LightingFieldOrientation` (Kern.Contracts).
Every field raster — terrain cells, `LightingGeometryRegistry` contributors
(World Surface, World Entity) — binds `BindRaster` and transforms with
`LightingFieldRaster.hlsl`; no field pass reads camera matrices or calls
`SetViewProjectionMatrices`, so Unity's implicit projection conversion never
participates. The projection is the unflipped device projection with an explicit
z slab of ±1 around the field mesh (z 0…0.1; no depth buffer, ZTest Always).
Memory row 0 therefore holds the top of the world rect exactly when
`RowsTopDown` (`graphicsUVStartsAtTop`). Readers take the order from the same
owner: `_MaterialYFlip` for compute, `_KernFieldRowsTopDown` for terrain AO,
`RowsTopDown` for bloom/post emission UVs and `MemoryRow` for readbacks.
`LightingFieldOrientationValidator` rasterizes a top-half quad with the field
transform once per domain and reads its rows with compute `Load`; disagreement
throws instead of publishing mirrored fields. A mirrored field moves opposite
to the camera on every region reanchor or zoom-driven region resize.

Two lattices share one world rect. The transport lattice (`_FieldSize`,
`FieldPixelsPerCell`) holds material/albedo/emission, the cell proof mask, every
DDA traversal, cascade probes and dynamic polar fans. The receiver lattice
(`_LightSize`, `LightPixelsPerCell`) holds static direct, dynamic direct and its
per-source tiles, the surface-air cache and the published lightmap.
`LightPixelsPerCell` is a power of two not above `FieldPixelsPerCell`, so
`_FieldTexelsPerLightTexel` is an integer and each receiver centre
(`LightPxCenterToFieldPx`) is one exact transport position. ResolveDirect,
SolveDynamicLighting, ComposeDynamicLighting, ResolveTransmissionDebug,
BuildSurfaceAirCache and CompositeLighting dispatch over the receiver lattice;
receiver rectangles, the dynamic compose union and the partial composite rect
are light texels, while polar ray lengths stay in field texels. Lowering the
receiver density reduces receiver work quadratically and never lowers the
material that rays traverse.
Unchanged coverage/settings reuse textures and the selected probe layout;
a replaced material resource invalidates the solve even at unchanged dimensions.


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

The lighting material field samples atlas albedo using the same geometry/UV
resolver and filtering as the visible terrain, but pins animated atlas selection
to its authored first frame and does not apply color animation (crystal shimmer,
faceted glints, pulse, or rainbow). Those are presentation effects, not static
transport inputs: rebuilding a region at another `_Time.y` must not produce a
different albedo snapshot or shift static indirect lighting. Terrain decals and
relief remain part of the material-field albedo as spatially stable inputs. The
material/emission fragment rejects absent polygon coverage and atlas alpha before
applying decals. Expanded raster carriers cannot publish albedo or decal emission
outside the visible material silhouette.
The
same fixed atlas frame supplies AO-field alpha/cutout, so an animated atlas
cannot make static contact occupancy blink on a field rebuild either.

The displaced silhouette has one geometric predicate in
`TerrainGeometry.hlsl`: four corner vertices for regular cells, or those corners
plus four bend vertices for organic cells. Stored corners and derived bend
vertices are quantized to the 1/32-cell geometry grid before raster coverage.
Visible/material coverage tests the continuous raster sample against that
polygon; per-cell fragment quantization would move opposite sides of a shared
edge apart and open gaps. Relief-rim distance and AO field contact also retain
continuous fragment positions and measure the same quantized polygon; AO fades
over half a cell from its signed distance. The crystal phase map uses those same
pixel centers. The organic carrier quad gets its bounds from the same corner
and bend point functions used by the polygon predicate, so there is no second bend,
pivot, or rounding implementation that can clip the polygon it encloses. During
the AO-field draw only, the carrier expands by half a cell plus half an AO-field
texel in world units, converted to cell-local units by the terrain shader. This
keeps the full contact falloff available at extreme corners;
material/emission and visible passes use zero carrier padding.

The cell shape encoding has two explicit owners at the CPU/GPU boundary:
`TerrainCellGeometry` encodes four organic edge bends and their Meta bytes on
the CPU; `TerrainGeometryContract.hlsl` owns shader-side 1/32 quantization and
Meta decoding. `TerrainQuadBuilder` selects the four edge values but does not
own their wire format, and `TerrainCellDataPacker` transports the contract but
does not define it. `TerrainCellData.hlsl` consumes decoded geometry metadata;
`TerrainGeometry.hlsl` evaluates the polygon and signed distance, while
`TerrainContour.hlsl` owns contour and relief consumers. This keeps
carrier construction, raster coverage, relief, AO, and crystal sampling on the
same cell-local geometry convention.

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
includes redrock and the transition strip. The camera-dependent perspective
strip and horizon are visible-only, unlit meshes; camera motion changes their
UV projection without invalidating the static material or AO fields. Their
texture uses explicit horizontal UV repetition and a repeating U sampler; the
perspective projection follows the active camera every frame. The horizon
uses the source client's separate procedural ridge and sky shader; it samples
the perspective texture only below the computed ridge and exponentially fades
the sky with altitude. The field is sized from the lighting cell grid and AO
texture-dimension limit; there is no scratch target, compute kernel, dispatch,
or mip generation. The target is
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

Source edits invalidate upload-set evaluation, not the whole composite. After
reach/capacity filtering, an unchanged uploaded set leaves the GPU result valid;
the coordinator acknowledges the source state without recording a lighting frame.
Movement, color/intensity changes, entry and removal that change GPU inputs still
invalidate dynamic radiance. An explicit `CompositeDirty` refresh always covers
the full field, even when dynamic movement/removal occurs in the same frame;
the dynamic union is retained for subsequent partial updates. Managed decision
tests and remaining production gates are recorded in
[the invalidation evidence](LIGHTING_DYNAMIC_INVALIDATION_EVIDENCE.html).

The moving one-cell emitter is sampled by a fixed `N×N` grid in the continuous
complete source-square bounds, including its part outside the lighting field.
`DynamicEmitterPoint` uses the same continuous bounds as `DynamicRadianceFromPolar` when assigning receiver
rays to emitter samples. Do not round those bounds to field-texel centres: that
kept emitter samples stationary across sub-texel robot motion, then jumped them
when an edge crossed a texel. A transport regression checks the expected
sub-texel displacement against the production HLSL function. Outside-field material
is explicitly air: DDA integrates the emitter's outside portion analytically,
before field entry or after complete field exit. It never clamps the source to an
edge texel. The production Metal test `OutsideFieldSource_KeepsCompleteEmitter`
compares five boundary positions against an independent continuous-square integral.

**Dynamic transport stage contract (`TL-STAGE`):** `TraceDynamicPolar` reads
`_MaterialField`, `_CellSolidMask`, `_DynamicLights`, `_WorldRect`, and
`_FieldSize`; it writes per-light optical depth to `_DynamicPolar`. Its dispatch is
`ceil(rayFan/64) × DynamicEmitterPointCount × 1`; the polar target is
`(rayFan+2) × rayLength × (capacity × DynamicEmitterPointCount)` Tex2DArray, with
one layer per emitter point and two angular wrap columns in each layer.
`rayLength` rows are receiver (light-lattice) texels: the march visits every
transport texel but stores one radial depth sample per light texel
(`_FieldTexelsPerLightTexel` transport texels per row), so a denser material
field no longer multiplies the polar storage and writes. The far gather blends
the two neighbouring rays by transmission, not by optical depth
(`PolarTransmission`): a blocked or sealed ray beside an open one yields a
penumbra instead of blackening the whole angular gap between them.
Equal RGB extinction uses RFloat (one exact float32 optical depth); unequal RGB extinction uses ARGBFloat. The lookup replicates the scalar depth before applying RGB source radiance. No half-depth conversion or HDR clamp is used. `DynamicLightTileCache` owns and releases this scratch resource. A quarter-capacity slot shrink also releases the obsolete polar layers. Radius samples
are never shortened to fit stacked emitters; an unsupported requested length
fails explicitly. `VisualTuning.MaximumDynamicPolarRayWorkUnits` controls the
existing shared angular work budget. `SolveDynamicLighting` reads those outputs plus
`_StaticEmissionField` and writes a per-source Tex2DArray layer, or
`_DirectTexture` for one light. It dispatches `ceil(rect.width/8) ×
ceil(rect.height/8) × 1` over the source reach rect. Receivers come from that CPU rect:
the analytic air reach (weakest extinction) intersected with the camera coverage.
Inside it, `TraceDynamicPolar` also writes an angular horizon
(`_DynamicHorizon`, `[slot][emitter point][direction]`): the radius past which
that ray's transmitted light, with the source-entry depth and chord-weight bounds
added back, is below `InvisibleDynamicRadiance`, or where the ray is sealed. Each
fan thread owns one entry and overwrites it on every trace — no CPU clear and no
atomics. `SolveDynamicLighting` reads it through the read-only binding
`_DynamicHorizonInput` and skips a far-zone receiver only when it lies beyond the
horizon of every ray its gather can blend, over all emitter points (angle widened
by the emitter-point spread, one radial row of interpolation added); the near zone
is never skipped. The former scheme — a `SetBufferData` clear of the slot followed
by `InterlockedMax` from the fan — read back zeros: light ended at the near-zone
square (and, with the earlier single reach radius, at the 10.5-cell floor circle).
The buffer lives with the polar texture in `DynamicLightTileCache`. `ComposeDynamicLighting`
reads cached light layers, 32-byte tile descriptors and source colors and writes
`_DirectTexture` over
the union rect with the same 8×8 groups. The solver records `LightingDdaSegments`,
`LightingDdaTexelVisits`, ray work units, and dispatch pixels; stable frames with
unchanged sources issue no dynamic transport dispatch. Source motion changes only
the sub-texel emitter coordinates and does not add dispatches, ray samples, or
buffer/texture storage.

The tile array has `roundUp64(maxReceiverWidth) × roundUp64(maxReceiverHeight) ×
capacity` texels; source slot is its layer index, independent of uploaded-list order.
It does not multiply viewport dimensions by the number of tile packing columns.
Equal RGB extinction uses RFloat brightest-channel radiance (float32), reconstructed
with the source RGB/brightest-channel ratio in composition; unequal RGB extinction
and the explicit vector reference use ARGBHalf RGB radiance. Final direct output
remains ARGBHalf. Linear HDR values are never capped. One source keeps a 1×1×1
binding and writes directly to the RGB output. Format/layout changes invalidate both
receiver and optical-depth caches. Width, height, array support, writable format and
layer limits are checked explicitly. Composition avoids only the exact zero domain
already imposed by the receiver writer; it performs no DDA.

Neutral-medium exponential/emission arithmetic has its own `_NeutralExtinction`
flag, separate from the storage-format flag: comparing scalar/vector storage must
not change transport math. Zero emission intervals skip the emission integral while
still integrating material transmission. Reanchor/material changes invalidate parked
sources' ray depth as well as receiver radiance; receiver-only camera coverage changes
retain valid rays. No source is removed on stopping and no time-based update limit is used.

### Bounce

**Kernels**

- `BuildBounceTaps`;
- `BuildBounceFilter`;
- `SolveDiffuseBounce`.

Geometry-dependent cache building may use DDA. Bounce solve only gathers cached data.

### Composite

**Kernel**

- `CompositeLighting`.

Combines static direct, dynamic direct and ambient inputs. It must not perform geometry traversal.

Surface incident-light presentation uses the geometry-owned first-air cache at each 1/32-cell
receiver. `BuildSurfaceAirCache` searches only the authored
`SurfaceReflectionReachCells` support (0.5 cells), instead of a whole cell.
Composite weights the borrowed face light by the receiver's actual distance:
`(1 - smoothstep(0, reach, depth)) * exp(-airExtinction * depth)`.
This fades the face contribution per texel and avoids copying one face intensity through
the entire wall cell. Direct transport is unchanged. Dynamic composite dirty
bounds include the same support plus two rounding texels. Geometry changes
rebuild the cache; source movement updates the composite without rebuilding it.
`SurfaceIncidentLighting` takes the maximum of the center incident light and
weighted exposed-face estimates. Composite blends that estimate with the center
sample by occupancy; it does not add the center sample to itself. The published
lightmap carries incident scene-linear light, without receiver albedo. The visible
terrain pass applies albedo once. Applying albedo inside the lightmap would square
the receiver's color when the terrain samples it and also tint nearby receivers
of that lightmap.
Face depth is measured from the exposed face to the receiver centre,
`(step - 0.5)` receiver texels; the search covers exactly the steps whose centre
lies inside the reach. Measuring to the first air centre made the weight depend
on density: at two texels per cell every face texel sat at the reach and got none.
At density 32 and reach 0.5 this bounds cache building at 64 neighbor material
loads/solid receiver (four directions times sixteen texels), versus 128 before.
Composite retains its four cache entries, eight possible direct-light loads
and existing resources/dispatches; the added distance/fade arithmetic has no
production GPU timing yet.

Dynamic near-field gathering uses a fixed full-circle angular phase while the
receiver is inside the continuous source square. A center direction is needed
only outside it. This avoids `atan2(0,0)` and source-motion-dependent angular
phase at internal receivers; emitter position and continuous source bounds
remain exact. The eight-ray sample count and DDA visitation policy are unchanged.

## Static invalidation policy

Current policy is conservative:

- initial state, resource resize, quality/config changes and geometry changes use full static solve;
- region invalidation can use dependency mask when the region did not move and the cost estimate is favourable;
- with the mask, each cascade dispatches a tight probe rect (`CascadeProbeRects`: dirty bounds expanded by interval reach + margin, 50% fallback to full) instead of the full grid; the per-entry early-out stays as a second net. Telemetry splits `cascadeFullEntries` vs `cascadePartialEntries`;
- region movement with an overlapping integer-scale field and valid static inputs uses `CascadeScrollRecorder.RecordWorldReanchor`. It copies probes on matching world lattice phases, marks newly addressed entries, and runs dependency-masked solves far-to-near. Each local DDA segment is tested against the common old/new field domain, dirty regions, and an exact GPU comparison of the old/new material and emission inputs. Two integral-image scans provide constant-cost conservative ray-box queries. Terrain window movement publishes arriving and departing contributor coverage through the existing exchange. Static DDA uses integer probe anchors plus relative offsets so translation does not round endpoints before subtraction. Changed far intervals and a changed clamped bilinear lookup invalidate near intervals. A phase mismatch clears/recomputes that cascade instead of changing its lattice;
- all pending overlapping edits move into the active solve when the material field is rebuilt on reanchor. They cannot be deferred after publishing geometry against reused transport. Full-reset records invalidate static and dynamic radiance validity as well as the field. Resource resize and global/contributor input changes require a dense solve;
- the old `RecordScroll`/`RecordCascadeMoveTier` band implementation remains dormant. Production does not enable it by changing a flag. `DiagnosticForceDenseReanchor` is an explicit production differential-test reference;
- dynamic light movement does not invalidate static cascades.

The stationary-edit dependency-mask path dispatches a tight per-cascade probe rect and early-outs unchanged entries inside it. It reduces DDA work when the mask rejects candidates and dispatch threads when the dirty area is small (far cascades with huge intervals fall back to full grid, where they are cheapest).

Reanchor stage ownership, lifecycle, invalidation proof and validation results are tracked in
[`WORLD_RENDER_GRID_EVIDENCE.html`](WORLD_RENDER_GRID_EVIDENCE.html). Reanchors dispatch every
atlas entry for the candidate checks; they reduce DDA work, not the candidate dispatch domain.
The copy costs 12 bytes read plus 12-byte interval/4-byte mask writes per entry and adds a lazy
12-byte scratch interval per entry. Previous material/emission and two prefix buffers add 20 bytes per field texel; a reanchor copies both fields and records two scan dispatches. Stable frames do not copy, allocate, or run static solves.
The diagnostic transport-counter switch resets and snapshots actual static-solve counters, adding two DDA atomic counters only during explicit captures;
it does not change rays or quality. Production GPU/performance acceptance remains pending until measured.

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

### Single-source retained output

With one uploaded dynamic source, `SolveDynamicLighting` writes directly to
`DirectTexture`; that texture is also the retained source result.
`DynamicLightTileCache` allocates only a one-texel UAV binding for its shared
kernel, and the kernel does not write that binding. Stable one-source frames do
not compose tiles. Multiple-source frames retain their existing per-source
radiance atlas and additive composition. Switching either way replaces the
layout, clears slot identity and invalidates every current source before
publication. Geometry, exact source state, generation and field-layout
invalidation still apply. This removes a duplicate field-sized HDR allocation
and write for one source; it does not reduce density or full ray distance.

### Incremental composition and receiver blocks

With several sources on a retained atlas, the solver remembers which receiver
rectangle each source last contributed to `DirectTexture`. A solve recomposes
only the union of the old and new rectangles of sources that were re-traced,
changed rectangle, appeared or disappeared; every other pixel's sum is
unchanged. `ComposeDynamicLighting` writes every pixel of its rectangle, zeros
included, so a vacated area needs no clear. A solve that changed no rectangle
skips the composite. A replaced atlas, a static re-solve or a single source
keeps the full-union path.

`LightingReceiverCoverage` grows the visible receiver rectangle outward to
blocks of `SnapCells` (8) cells. Sources whose reach crosses a screen edge clip
their receivers to it; following the camera texel by texel retraced almost
every source on every frame of walking, now it happens once per block.

### Visibility bound

`InvisibleDynamicRadiance` (uniform `_InvisibleDynamicRadiance`) is derived from
the active output, published by the display post-process pass through
`DisplayOutputPrecision`: half the first code above black (8-bit sRGB in SDR,
10-bit PQ at the calibrated paper white in HDR), divided by the exposure gain,
the Neutral toe slope (1.07) and the source count rounded up to a power of two.
It never drops below half the smallest float16 (2.98e-8), where the radiance
textures store zero. HDR is the finer output: at 350-nit paper white its step at
black is about 2600× smaller than SDR's, so the bound sits at the float16 floor
and dynamic reach is longer than in SDR. A changed bound re-culls sources and
re-traces every ray, horizon and receiver tile.

## Explicit quality tuning (2026-10-01)

VisualTuning contains the seven quality values and cost/unit comments only. The
immutable `LightingQualityTuning` value is in Kern.Contracts; validation, session
state and revision ownership are in LightingQualityTuningController in Kern.World.
The existing «Цена света» tool stages a local draft and applies it with one button.
It can copy the data initializer for permanent source editing; it does not write
assets or user display settings. Defaults and existing artistic intensities remain
as authored. AO and world density are independent of these light-field overrides.

LightingEngine consumes a changed snapshot on the next committed terrain demand.
Field/probe/static-angle changes disable publication, release the old lighting
resources and rebuild before publishing. Dynamic-only changes release source ray
and receiver caches, reupload registered sources, and recompute dynamic/composite
without invalidating material or static transport. ResetUploadState preserves the
source dictionary, including stationary sources. Equal snapshots cause no work.
The renderer receives live requested probe density even if the client preset was
created before the change. Resource identity includes dense field dimensions, authored probe density and
static direction ceiling. Dynamic emitter layers derive from points-per-axis²,
so an emitter-count change cannot reuse an array with the previous addressing.

The cost tool reports requested and actual first-cascade density and per-cascade
angles. Production allocation preserves the authored density and angular ceiling;
the conservative DDA estimate is diagnostic only. The atlas-entry and texture
size limits fail explicitly without lowering quality. GPU per-stage timings
remain unavailable where the profiler does not provide them. Quality overrides
and diagnostic references are recorded in the global frame harness settings.
The manual panel calls `LightingEngine.TryApplyQualityTuning`, which preflights
the same candidate layout as allocation against current world coverage. An
oversized request leaves the quality revision and published resources unchanged
and returns an actionable reason. Allocation validates before releasing resources
and publishes its candidate cascade list after the old fields are released.

## Exact uniform-cell transport and spatial revisions (2026-10-01)

BuildCellSolidMask proves constant occupancy over every mip-zero material texel.
RGBAHalf contains flags only: R proves clean air (zero occupancy and zero
emission in every texel); G proves constant occupancy and no static emission; B proves all texels satisfy
the solid-occupancy threshold; A proves constant occupancy independently of
emission. Traversal loads original UNorm alpha once per proven cell, avoiding
half-precision alpha encoding. Static emission collection uses G; dynamic/optical
depth traversal uses A. Every nonuniform cell still visits every crossed base
texel. Constant extinction integrates exactly to each cell boundary, retaining
closed-corner tests, RGB coefficients, source-square entry/exit and every polar
radius sample. No new texture, pass, frequency cap or automatic quality step is
introduced. Geometry owns the proof cache and invalidates it on material/emission
or field generation/origin changes.

Summed-area tables (`uint2` per cell, `BuildCleanCellRows`/`BuildCleanCellColumns`,
rebuilt with the mask) count cells that are not clean air (x: mask R) and not
clean stone (y: mask G and occupancy exactly 1, i.e. full, uniform, emission-free).
The cascade merge traces up to 16 child paths per entry, from the probe's interval
start to each neighbouring far probe's interval start. When a four-load query
(`NonCleanCellCount`) proves the box containing all of them is one clean medium,
each path is evaluated in closed form (`CleanMediumTransmittance`): zero radiance
and `exp(-σ · length)` with σ of air or stone, the product DDA would accumulate.
Space outside the cell grid is air, so a box crossing the grid edge never proves
stone. Any surface, silhouette edge or emitter in the box keeps the traced path.
Corner seals cannot fire inside one medium (an L-turn needs air on both sides;
an exact lattice-point crossing by a child path between arbitrary probe positions
has measure zero). These merge paths are most of a full static solve; the stone
proof removes them from probes buried in rock as the air proof does in caverns.

Corner sealing is decided by rasterized geometry on the transport lattice, not by
cell centres (`CornerSealed`, DDA.hlsl, used by `TraceLightSegmentLocal` and
`TraceDynamicPolar`). Two solid texels touching at one lattice corner form a closed
wall. A ray is stopped (transmittance 0; polar depth 1e6 for the rest of the ray)
when it crosses exactly through such a corner, or when it goes air → solid M →
air entering M across one axis and leaving across the other while the texel
diagonally opposite M across that corner is solid. Uniform cells take part as
whole regions using their exact boundary lines. Cells that share a grid vertex
but whose displaced or rounded silhouettes leave air at that corner stay open.
The old cell-centre rule fired only on exact cell-vertex crossings, so a ray
cutting the corner of one block passed between two diagonal blocks almost
unattenuated.

Lighting records the latest revision staged through the contiguous terrain change
journal. A matching revision uses its queued spatial facts; an unjournaled revision
still forces a full rebuild. The coordinator activates all edits in the stable
transport region plus its one-cell diagonal halo, including padding outside the
camera viewport. Proven irrelevant changes advance the consumed source revision
without recomputing unchanged fields. Their sequence/revision/region is logged
before acknowledgement. Reanchors compare old/new geometry and intersect every
retained ray dependency against their common field, so discarded outer coverage
cannot silently enter reused intervals. Unknown contributor revisions continue
to invalidate the full field. Standard AO follows the same spatial rule.

Production Metal tests now verify authored cascade density/directions, scalar vs
vector depth over 200704 receiver pixels, continuous/standing sources, per-texel
wall falloff, point-scaled zoom and eight world-region crossings including negative
X. Static atlas reuse matches the independent full-solve production reference
with zero changed packed words and zero static/world image error at every move.
Measured performance acceptance and physical HDR-output coverage are recorded
separately in WORLD_RENDER_GRID_EVIDENCE.html; these image checks alone do not
establish the 5 ms whole-frame target.

## Stationary-source transport invalidation (2026-10-01)

`DynamicLightTileCache.InvalidateAll` invalidates both receiver radiance and
polar optical depth. The latter depends on material/emission geometry, world
field origin, dimensions and generation, not only source pose and colour.
Previously geometry/reanchor invalidation cleared `_slotValid` but retained
`_slotPolarValid`; a parked source consequently reused optical depth from the
old transport domain. Camera receiver-coverage changes within the same field
retain rays through the separate rectangle comparison; they do not call this
full input invalidation. Source pose/colour changes still invalidate through
`NeedsPolarTrace`, without invalidating static cascades.

Production `StationarySource_ReanchorMatchesFreshTransport` holds the exact
source pose and compares retained output against a fresh same-input solve over
four field reanchors. Its right-hand 32×128 strip is newly revealed terrain
outside the original field; the test also requires nonzero recorded polar work
on every reanchor. The fixture explicitly uses a 0.5-cell near zone to exercise
polar lookup, restores session quality on teardown, and leaves raster/field
density 32, probes 4 and directions 64. It cannot pass by comparing only an
unchanged overlap or a dark image. Results and resource dimensions are recorded
in `lighting-parked-revealed-band-results.xml`.

`CommandBufferSampleScope` owns both BeginSample and EndSample; callers create
one scope per stage. Production Metal execution verifies the nesting, including
early returns. This changes profiling ownership, not radiance or pass count.

The traversal benchmark's deterministic image phase sets dithering at the
begin-camera-render boundary before URP captures camera data. The display owner
otherwise re-enables its authored temporal input periodically. The subscription
and original camera setting are restored before timed frames and in finally.
Reference-to-reference drift is asserted separately from reference-to-optimized
error; failing either prevents any performance acceptance.
