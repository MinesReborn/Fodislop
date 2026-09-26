# Terrain and Lighting Architecture Standard

**Status: mandatory target standard.** This document governs all new and changed
terrain/lighting code. `LIGHTING_ARCHITECTURE.md` records current implementation
details; it does not override this standard. Existing deviations are debt, not
precedent. A change must not add to them. A PR that crosses one of the gates below
is incomplete until it satisfies the gate or is split into a conforming migration
step.

The words **MUST**, **MUST NOT**, and **ONLY** are requirements. Reviewers must
request a concrete code path, test, counter, or diff location for every applicable
check. “Works in my scene” and comments without executable evidence do not pass.

Every requirement below has an ID prefix (`TL-OWN`, `TL-API`, `TL-LIFE`,
`TL-REV`, `TL-STAGE`, `TL-DATA`, `TL-PERF`, `TL-VERIFY`, or `TL-CHANGE`). PR
evidence MUST cite the applicable IDs. These IDs are stable review references;
they do not claim an automated checker exists.

### Merge decision

Merge is blocked if any applicable requirement lacks its required artifact, any
check fails, a result is marked pending but is required for the claim being made,
or the diff adds a forbidden dependency. Reviewers MUST request changes; they
must not convert a failed gate into a follow-up task or waive it in a comment.
Repository authority rules still govern which checks may run. A required check
that cannot legally run is recorded as pending and blocks claims that depend on
it; this standard does not grant permission to run it. The conformance-debt
allowlist below permits only already-existing references; it is not a general
exception mechanism.

## 1. Domain map and ownership

The allowed dependency direction is:

```text
World data / configuration
        ↓
Terrain domain ── publishes versioned geometry facts and spatial changes ──┐
                                                                          ↓
Lighting domain ── derives and owns light fields / solve caches ──→ presentation
```

| Concern | Sole owner | Other domain may do |
| --- | --- | --- |
| Cell contents, terrain window, residency, terrain mesh and terrain data textures | Terrain | Read published facts through the declared geometry contract |
| Terrain geometry/emission revision and changed world regions | Terrain | Lighting consumes revisions and regions; it cannot mutate terrain state |
| Lighting field dimensions/origin, GPU resources, invalidation policy, solve order, radiance caches, output texture | Lighting | Terrain contributes geometry through the contract; presentation samples published output |
| Composition and subscription wiring | Game composition root | Register each implementation exactly once and own teardown order |
| Screen/display composition | Presentation/post-processing | Read the published lighting result; do not trigger geometry collection or solving |

Terrain MUST NOT hold or resolve `LightingEngine`, a lighting coordinator, solver,
registry, texture pool, compute shader, or lighting-specific invalidation flags.
Lighting MUST NOT name, cast to, locate, or read a concrete terrain renderer,
terrain cache, terrain mesh manager, terrain texture, or terrain revision field.
`GetComponent`, scene/name searches, service-locator lookups, reflection, and
parallel direct references do not count as domain contracts.

The following source-level dependencies are forbidden across the domain
boundary (`Assets/Scripts/World/Terrain/**/*.cs` → Lighting implementation and
`Assets/Scripts/World/Lighting/**/*.cs` → Terrain implementation):

- Terrain → Lighting implementation: `LightingEngine`, `LightingComposition`,
  `LightingUpdateCoordinator`, `LightingFrameExecutor`, `GeometryLightingSolver`,
  `StaticLightingSolver`, `DynamicLightingSolver`, `IndirectLightingSolver`,
  `LightingGeometryRegistry`, `LightingResourceManager`, `LightingTexturePool`,
  `LightingPresentation`, `LightingRuntimeState`, and `LightingInvalidationFlags`.
- Lighting → Terrain implementation: `TerrainRenderer`,
  `TerrainViewportCalculator`, `TerrainCellCache`, `TerrainCellBuilder`,
  `TerrainCellDataTextures`, `TerrainMeshManager`, `TerrainMaterialManager`,
  `TerrainLook`, and any `Kern.World.Terrain` implementation namespace/type.
- Either direction: concrete implementation types, private state, or mutable
  invalidation flags from the other domain; `GetComponent`, `Find*`, scene/name
  lookup, static service lookup, reflection, or event buses with string keys used
  to evade the contract.

During migration, the only permitted cross-domain source symbols are the existing
`ILightingGeometryContributor`, `LightingMaterialEmissionContext`, and
`LightingAmbientOcclusionContext` contract symbols. They
are recorded debt because they currently live in the lighting implementation
namespace/assembly; do not add contract members or new callsites except as part
of a PR that moves/replaces the contract. No other symbol is implicitly
allowlisted. The source scan below is mandatory; a reviewer classifies every hit
as an exact existing debt reference or rejects the PR.

Cross-domain traffic MUST use one explicit contract in a dependency-neutral
assembly. Terrain publishes immutable geometry snapshots or render contributions,
monotonic source revisions, and spatial change records. Lighting subscribes at
composition time and translates those facts into its own invalidation model. No
domain may write another domain's flags or caches. The composition root is the
only place allowed to name both concrete implementations.

The public contract MUST be the smallest complete API needed by its consumer. It
MUST state coordinate space, units, lifetime, ownership, thread/render phase,
revision semantics, and failure behavior. GPU handles passed through a contract
are borrowed for the call and MUST NOT be retained. A contributor MUST NOT change
the render target, global shader state, or command-buffer state outside the
documented context. Registration and unregistration MUST be paired with owner
lifecycle and duplicate/missing contributors MUST fail with an actionable error.

### Normative target: terrain/lighting frame exchange (`TL-API`, `TL-OWN`, `TL-LIFE`)

`TerrainRenderer` MUST publish terrain facts; it MUST NOT schedule lighting,
mutate lighting invalidation state, or ask lighting for permission to render.
`LightingEngine` MUST own when a lighting solve runs, how geometry changes map
to its invalidation state, and when its output is published. A data exchange is
allowed; a service that forwards calls from Terrain to Lighting is not.

The target exchange contract lives under
`Assets/Scripts/Core/Interfaces/Contracts/WorldLighting/` in `Kern.Contracts`,
namespace `Kern.Core.Interfaces.WorldLighting`. It contains immutable value
types and a narrow `ITerrainLightingExchange`; it contains no implementation,
solver operation, delegate/callback, Unity scene lookup, or reference to any
`Kern.*` implementation assembly. Its one runtime implementation is registered
as one `Lifetime.Singleton` by `GameLifetimeScope`. The exchange stores data and
sequence watermarks only; calling it MUST NOT run terrain or lighting work.

The contract consists of these records and ownership rules:

| Contract value | Required fields | Publisher / reader |
| --- | --- | --- |
| `LightingTerrainRequirements` | `PolicyRevision`; `RequiredTerrainPaddingCells`; `StableLightingPaddingCells` | Lighting publishes after initialization/configuration; Terrain reads before planning each frame |
| `TerrainLightingFrameSnapshot` | `WorldGeneration`; contiguous `FrameSequence` starting at 1 per generation; `State` (`Ready` or `HoldingPublishedView`); `TerrainGeometryRevision`; half-open `CameraViewportCells`; half-open `LightingViewportCells`; borrowed `Camera`; borrowed neutral `ILightingGeometryContributor` | Terrain publishes after the frame's terrain commit and presentation decision; Lighting consumes the newest frame demand after Terrain LateUpdate |
| `TerrainLightingChange` | `WorldGeneration`; contiguous `Sequence`; `TerrainGeometryRevision`; `Kind` (`Region` or `FullReset`); `Channels` (`Occupancy`, `Material`, `Emission` flags); half-open world-cell `Region` for `Region`; named `FullResetReason` for `FullReset` | Terrain publishes; Lighting reads in sequence and acknowledges only after durable transfer to lighting-owned state |
| `LightingOutputSnapshot` | `OutputGeneration`; `WorldGeneration`; `State` (`Published` or `Disabled`); `WorldRectCells` | Lighting publishes after command execution/presentation; Terrain reads before validating its material binding |

`ITerrainLightingExchange` MUST expose only these operations:

```text
PublishLightingRequirements(value)
TryReadLightingRequirements(out value)
PublishTerrainFrame(value)
TryReadLatestTerrainFrame(afterFrameSequence, out value)
AcknowledgeTerrainFrame(throughFrameSequence)
PublishTerrainChange(value)
TryReadNextTerrainChange(worldGeneration, afterSequence, out value)
AcknowledgeTerrainChanges(worldGeneration, throughSequence)
PublishLightingOutput(value)
TryReadLightingOutput(out value)
```

The exact C# signatures may follow repository conventions, but no operation may
call into either domain. There is one Terrain producer and one Lighting consumer
per game scope. A duplicate publisher or consumer registration is a startup
failure. Contracts and their implementation MUST reject invalid rectangles,
sequence gaps, generation mismatches, sequence regression, and overflow; they
must not silently clamp, wrap, coalesce away, or reinterpret malformed data.

#### Publication and frame order

1. `LightingEngine` publishes `LightingTerrainRequirements` during explicit
   initialization and after any quality/configuration change that changes either
   padding value. `PolicyRevision` increments only when those values change.
   Runtime Terrain planning MUST fail closed if no requirements have been
   published; editor preview uses a separately named editor-only policy input.
2. `TerrainRenderer.LateUpdate` remains at execution order `100`. It reads the
   latest requirements, completes its existing build/publish work, determines
   the actually committed frame (including held teleport frames), and publishes
   exactly one frame demand after commit/presentation. Terrain initiates demand;
   a frame with no committed presentation publishes no demand. `Camera` and
   geometry contributor references are borrowed through the end of this Unity
   frame and MUST be valid when `State` is `Ready` or `HoldingPublishedView`.
3. A `Ready` snapshot names the camera viewport and lighting viewport that are
   valid for the committed terrain. A `HoldingPublishedView` snapshot MUST name
   the old committed viewport and old published terrain, never the pending
   destination window. A terrain build failure, unavailable world/camera, or
   uncommitted initial window publishes no usable snapshot for that frame.
4. `LightingEngine.LateUpdate` runs at execution order `200`, after Terrain. It
   reads the newest frame demand with `FrameSequence` greater than its last
   successfully processed sequence and validates `WorldGeneration` plus
   committed-presentation state. It MUST NOT use `Time.frameCount` as a gate. If
   no new demand exists, it performs no lighting solve and MUST NOT replay a
   previously processed frame. If one exists, it stages unacknowledged changes,
   calls its own coordinator once with the demand's viewport/camera and geometry
   contributor, and acknowledges the frame and staged changes only after the
   coordinator returns successfully. Budget capture follows successful update.
   On failure, acknowledgements and the successful-frame watermark remain
   unchanged; the next Terrain frame may supersede the failed demand, but change
   records remain pending. The engine itself checks its quality/bypass state;
   Terrain does not gate lighting on quality.
5. `TerrainRenderer` performs its one-time `ValidateLightingBinding` after reading
   a `LightingOutputSnapshot` with `State == Published` and a newer
   `OutputGeneration`. The `LightingEngine` publishes that snapshot only after
   command execution and `LightingPresentation.Publish` complete. Disabled
   lighting publishes `State == Disabled`; Terrain does not treat stale global
   shader state as a newly published result.

`TerrainRenderer` MUST NOT call `LightingEngine`, `LightingUpdateCoordinator`,
or any lighting invalidation/capture API after this migration. Lighting MUST NOT
cast the snapshot contributor to `TerrainRenderer`. `ILightingGeometryContributor`,
`LightingMaterialEmissionContext`, and `LightingAmbientOcclusionContext` live in
the neutral contract namespace; render methods receive only their named targets.
Terrain's contributor is selected from the snapshot, while other contributors
remain owned and registered by Lighting's registry.

#### Change publication and acknowledgement

- `WorldGeneration` increments before each world replacement and is never reused
  within a game scope. On generation change, previous pending records and frame
  snapshots become stale and MUST NOT be applied to the new world.
- Change-record `Sequence` starts at `1` per generation and increments by exactly
  one for every published change record. `TerrainGeometryRevision` is the
  revision of the committed geometry represented by that record/frame, not the
  requested CPU build. Frame `FrameSequence` starts at `1` per generation and
  increments for every committed Terrain frame demand. A failed frame demand may
  be superseded by a newer demand; change-record sequence may not be skipped
  except by a superseding `FullReset`. Regions are integer world cells, Unity
  Y-up, half-open `[min, max)`.
- Terrain publishes a `Region` record only after that changed geometry is
  committed and visible. It publishes a `FullReset` record for world replacement
  or another named event that makes all lighting-derived geometry state invalid.
  A full reset supersedes all lower-sequence unacknowledged records in the same
  generation; it never supersedes records from a newer generation.
- Lighting stages records strictly in sequence before its update. For `Region`,
  it queues the change in lighting-owned pending state with the original
  sequence. For `FullReset`, it marks the lighting field dirty and clears
  stale-generation pending regions. It acknowledges neither changes nor frame
  demand until its coordinator has successfully returned for that demand. If
  command recording or execution throws, both acknowledgements remain pending;
  retry insertion MUST be idempotent by `(WorldGeneration, Sequence)` so it
  cannot duplicate invalidation.
- A region proven outside the current stable lighting region may be consumed as
  irrelevant only if the current region-move policy guarantees a full solve on
  the next reanchor. The discard reason and sequence are recorded in lighting
  diagnostics before acknowledgement. If atlas reuse is later enabled, this rule
  must be re-proven against retained atlas dependencies first.
- While quality is Off or compute bypass is active, Lighting MUST NOT acknowledge
  changes or frame demands as solver-accepted. Terrain retains the journal until
  Lighting resumes. Enabling quality MUST apply pending resets and regions and
  perform the required full solve before publishing a `Published` output snapshot.
- The exchange retains every change record above the Lighting acknowledgement
  watermark. It may remove acknowledged records only. A gap, failed transfer,
  exception, or generation mismatch leaves the watermark unchanged and blocks
  publication of a frame that depends on the missing record.

Frame demands use a separate watermark. `TryReadLatestTerrainFrame` returns the
newest frame with a sequence greater than the last successful acknowledgement;
`AcknowledgeTerrainFrame` may supersede older failed frame demands because the
change journal is independently retained. The exchange MUST reject an ack beyond
the newest published frame or a regressing ack. Lighting consumes demands in the
same `LateUpdate` ordering window in which Terrain publishes; sequence freshness,
not wall-clock/frame-number checks, is the correctness condition.

## 2. Ownership and lifecycle gates

Every new type MUST have one named owner and one lifecycle owner. Before code is
added or moved, the PR description MUST identify both. A type with two plausible
owners fails review.

- Composition roots construct/inject and connect domain services. Runtime domain
  objects MUST NOT search scenes, resolve containers from lifecycle callbacks,
  or construct a second owner.
- Initialization MUST be explicit and ordered: dependencies/resources ready,
  contributor registration/subscription, first snapshot, then publication.
- Teardown MUST stop callbacks and GPU submission before releasing textures,
  buffers, meshes, or contributor state. Unregister/unsubscribe MUST be safe and
  occur exactly once. Destroyed Unity objects MUST not remain registered.
- No hidden static mutable state, lazy global singleton, fallback implementation,
  or “best effort” path may decide which domain owns data.
- Transitions (world load, window reanchor, resize, settings change, device/resource
  recreation) MUST be represented as explicit lifecycle/state transitions, not
  inferred from scattered null checks or repeated callback side effects.

**Review evidence:** dependency graph/callsite diff; registration and teardown
callsites; tests for duplicate registration, removal, and destruction where the
contract supports those states.

**Required lifecycle artifact (`TL-LIFE`):** include a compact state-transition
table for each changed owner (`uninitialized → ready → active → disposing →
disposed`) listing entry condition, resources acquired/released, subscriptions,
and publication point. Identify the exact method for each transition. If a type
does not have all states, state which states do not apply and why. Reviewers
block on an unaccounted callback, resource, or partial-publication path.

## 3. Geometry API, revisions, and invalidation

Terrain owns source truth; lighting owns derived state. The boundary MUST preserve
that distinction.

- A terrain revision advances if and only if lighting-visible geometry or
  emission data changed. Camera movement, mesh presentation, unrelated chunk
  metadata, and diagnostic state MUST NOT advance it.
- A revision MUST be monotonic for the lifetime of its publisher. Overflow is a
  fatal invariant violation, not silent wraparound. Registry membership changes
  are distinct from content changes.
- A change record MUST carry a revision and a world-space half-open rectangle
  `[min, max)`, or an explicit full-domain invalidation reason. It MUST identify
  whether occupancy, material/albedo, emission, or multiple channels changed.
  Empty/invalid rectangles MUST be rejected at the boundary.
- Change records MUST be retained until every relevant consumer acknowledges the
  revision. Coalescing may enlarge a region but MUST NOT drop its identity or
  under-invalidate. Full rebuild is permitted when its reason is explicit.
- Lighting MUST compare consumed and published revisions. A stable frame with no
  changed geometry, field layout, quality/configuration, or resource generation
  MUST issue zero geometry-field rebuilds and zero static-cascade solves.
- Dynamic source movement MUST invalidate only dynamic results. It MUST NOT bump
  terrain geometry revision or invalidate static cascades/AO.
- Resize, origin change, contributor set change, terrain data change, settings
  change, and GPU resource recreation MUST each have a named invalidation reason
  and a test/counter proving the intended scope of rebuild.
- Partial invalidation MUST account for the complete dependency reach of each
  affected cascade/cache. If correctness cannot be proven, use a named full solve;
  never silently under-invalidate.

**Review evidence:** revision producer and consumer, invalidation reason, dirty
region propagation/acknowledgement, and frame-local rebuild counters for both a
stable frame and each changed input represented by the PR.

**Required invalidation artifact (`TL-REV`):** provide one row per changed input
with `input → publisher/revision → region/channel → consumer acknowledgement →
expected stages rebuilt → expected stages untouched`. Include unchanged-input
case. Tests/captures must cover the rows changed by the PR; unexplained extra
rebuilds or dropped regions block merge.

## 4. Stage contracts and data flow

Keep the pipeline one-way and make intermediate data ownership visible:

```text
terrain contributors
  → geometry/material/emission fields
  → static transport (CascadeTrace)
  → static lookup (CascadeResolve)
  → dynamic transport/composition
  → cached bounce inputs / bounce solve
  → final composite
  → published WorldLightTexture
  → terrain/world presentation
```

Each stage MUST document its inputs, outputs, dispatch domain, invalidation owner,
and whether it runs on a stable frame. A stage MUST NOT perform work owned by a
later or earlier stage. In particular:

- Geometry field construction may rasterize/sample geometry; it MUST NOT trace
  static transport or publish final lighting.
- DDA/radiance traversal belongs only in declared transport/cache-building
  stages (`CascadeTrace`, dynamic polar trace, and bounce cache construction).
  Resolve, dynamic compose, bounce solve, final composite, and presentation MUST
  NOT add geometry traversal.
- Static cascade data is cached until a declared static dependency changes.
  Dynamic results follow exact source state and remain separate from static cache
  validity.
- Final output is published only after all resources and source revisions used by
  that output are coherent. A partially built window or mixed revision MUST NOT
  be presented as current.
- Compute bindings, thread bounds, texture formats, clear/load behavior, and
  resource lifetime MUST be declared at the stage boundary, not duplicated as
  informal assumptions across solvers and shaders.

Every stage change MUST update `LIGHTING_ARCHITECTURE.md` when actual dataflow or
stage behavior changes. Naming a file after a stage does not grant it ownership
of work outside that stage.

**Required stage artifact (`TL-STAGE`):** for every changed kernel/pass, list
read resources, written resources, dispatch dimensions and bounds, clear/load
semantics, cache owner, invalidation trigger, and stable-frame behavior. Link the
exact C# dispatch and shader kernel locations. A mismatch between the table,
binding code, and shader declaration blocks merge.

## 5. Coordinate, texture, and color invariants

- World cell coordinates use the project's world-space convention. Server
  top-left/Y-down coordinates convert only through `CoordinateUtils` and
  `MapManager.WorldHeight`. Conversions MUST occur once at a named boundary.
- Rectangles use documented half-open bounds. Integer cell coordinates, lighting
  texels, normalized UVs, and screen pixels MUST NOT share an untyped `Vector` API
  without explicit conversion functions and units.
- Lighting-field resolution is an integer number of texels per cell. Origin,
  extent, scale, texture dimensions, and pixel-center convention MUST be derived
  together and validated at allocation/bind time.
- Material RGB is linear albedo; alpha is physical occupancy under the current
  lighting contract. Emission, extinction/transmission, radiance, and AO remain
  separate quantities and MUST NOT be repurposed or silently clamped into one
  another.
- Scene-referred light and emission buffers MUST preserve the HDR/color contract.
  Color-space conversion, paper-white scaling, and display transform each have one
  owner; lighting shaders MUST NOT duplicate the URP display transform.
- Texture formats, color-space flags, filtering, wrapping, mip policy, clear
  values, and lifetime MUST be explicit. A format change requires validation of
  read/write support and expected numeric range on the production path.
- CPU encoders and shader decoders for packed terrain lighting data MUST be
  changed together and verified against independent expected values, including
  edge values and occupancy/emission separation.

Coordinate, color, HDR, and DDA details in `project-context.md`,
`hdr-color-contract`, and `lighting-guide` remain mandatory; this standard adds
the terrain/lighting boundary and review gates.

## 6. Performance and observability gates

Any new loop, allocation, texture, pass, dispatch, cache, invalidation source, or
per-frame callback MUST include a cost statement: worst-case work, expected steady
state work, memory/format, and the counter/marker that exposes it. “GPU-side” is
not evidence of low cost.

- Stable-frame work MUST be separated from rebuild work. Counters MUST be
  frame-local deltas or explicitly labeled cumulative; both may not be mixed.
- Every expensive stage MUST expose dispatch count, dispatched threads/pixels,
  relevant texture dimensions, and DDA work where applicable. Rebuild counts must
  include a reason.
- No timer throttling, frame skipping, hidden quality reduction, or precision
  loss may disguise excess work. Optimization must remove unnecessary work while
  preserving the documented output and update cadence.
- Performance claims require before/after measurements on the production path,
  same scene/input/resolution/settings, including stable and invalidating frames.
  CPU timings alone do not establish GPU cost; an isolated shader/helper does not
  establish production rendering cost.
- The PR MUST state baseline, observed delta, capture method, and acceptance
  threshold before claiming a performance improvement. If production measurement
  is unavailable, mark the performance result unverified and do not call the
  change an FPS fix.

## 7. Required verification by change type

| Change | Required evidence |
| --- | --- |
| Domain API or ownership | Compile/reference check plus tests for registration, lifecycle, and prohibited concrete dependency |
| Terrain geometry encoding or coordinate mapping | Independent known-value tests at boundaries, negative/origin cases, and encode/decode parity |
| Revision/invalidation | Tests proving each input invalidates the right outputs, unchanged input invalidates nothing, and regions survive until acknowledged |
| Solver/stage order or compute binding | Production shader/compute path with real resources and dispatch bounds; stage counters and output comparison |
| Color, emission, occupancy, AO, or texture format | Independent numerical oracle plus production-path image/data validation in linear HDR |
| Performance-sensitive path | Production-path before/after captures and frame-local stage/dispatch counters |
| Documentation-only architecture change | `git diff --check`; document links and claims checked against current source |

Tests MUST assert observable contract behavior, not call a production helper and
compare its result to itself. GPU/visual claims require the real production pass,
mesh attributes and position, material keywords, data textures, and camera path.
Static inspection and the C++ shader shim are supporting evidence only. Respect
the repository's Unity authority boundary: when a required Unity operation was
not explicitly requested, do not run it; report that verification gate as
pending and make no runtime correctness/performance claim.

### Mandatory architecture scans (`TL-API`, `TL-VERIFY`)

For every PR that changes C# under terrain, lighting, rendering contributors,
composition roots, or their contracts, attach the output of these source scans
and review each hit in the diff. Run from repository root:

```sh
rg -n '\b(LightingEngine|LightingComposition|LightingUpdateCoordinator|LightingFrameExecutor|GeometryLightingSolver|StaticLightingSolver|DynamicLightingSolver|IndirectLightingSolver|LightingGeometryRegistry|LightingResourceManager|LightingTexturePool|LightingPresentation|LightingRuntimeState|LightingInvalidationFlags)\b' Assets/Scripts/World/Terrain --glob '*.cs'
rg -n '\b(TerrainRenderer|TerrainViewportCalculator|TerrainCellCache|TerrainCellBuilder|TerrainCellDataTextures|TerrainMeshManager|TerrainMaterialManager|TerrainLook)\b|Kern\.World\.Terrain' Assets/Scripts/World/Lighting --glob '*.cs'
```

These scans are currently **manual gates**, not CI checks. Nonzero output is
expected only for the explicitly recorded conformance debt and must be reviewed
against the patch; any new occurrence fails the gate. Also inspect the complete
diff for aliases, fully-qualified names, `object`/reflection/service-locator
indirection, and new event paths: textual scans cannot prove absence of those
forms. Assembly references alone cannot enforce this boundary because terrain
and lighting currently share `Kern.World`.

For any compute/shader change, the change table above still requires production
path validation. If that Unity operation is not authorized in the current task,
record it as not run; static scans, the ArchitectureLinter, C++ shim, or isolated
shader compilation do not satisfy the production-path gate.

### Required PR evidence bundle (`TL-CHANGE`)

Every applicable PR description/review MUST contain these artifacts. Keep them
short and point to code/tests/captures rather than repeating implementation:

1. **Change contract:** requirement IDs; problem and evidence; owning domain/type;
   consumer; changed API and compatibility impact.
2. **Dependency result:** both scan outputs above, changed-line dependency review,
   and any conformance-debt item reduced or retained with its source location.
3. **Lifecycle and invalidation tables:** the tables required by `TL-LIFE` and
   `TL-REV`, scoped to changed owners and inputs.
4. **Stage/data contract:** `TL-STAGE` table and, for packed data, coordinate,
   units, color-space, format, and independent expected values.
5. **Verification record:** exact command/test/capture, environment and inputs,
   pass/fail result, and any operation not run with the authority reason.
6. **Performance record** when work/cost can change: baseline and after values,
   scene/input/resolution/settings, frame-local counters, memory delta, and
   explicit acceptance threshold. If no production capture is available, mark
   the result unverified and do not claim a performance fix.

Missing artifact, unexplained scan result, failed check, or unsupported claim is
a merge blocker. A reviewer cannot waive an invariant locally. Changing an
invariant requires a separate standard-change review that updates this document,
all affected contracts/tests, and the rationale before implementation relies on
the new rule.

## 8. Change procedure and review checklist

Every terrain/lighting change follows this sequence:

1. Name the owning domain, type owner, consumer, and exact stage.
2. State the invariant or measurable defect being changed and the evidence for
   it. Do not bundle unrelated cleanup into a boundary refactor.
3. Trace all producers, consumers, lifecycle hooks, shader bindings, and
   invalidation paths before editing. Update the dependency map if it changed.
4. Change the narrow contract first; keep the diff within one ownership boundary
   unless the change explicitly migrates both sides.
5. Add/update the verification that proves the contract, then run only checks
   permitted by repository instructions.
6. Update `LIGHTING_ARCHITECTURE.md` and diagnostics documentation when behavior,
   stage order, counters, or ownership changes.
7. Review the final diff against this checklist. A failed item is a release
   blocker, not a follow-up note.

**Required PR checklist** (copy into the PR or review description):

- [ ] One owner is named for every changed type, state, resource, and lifecycle.
- [ ] No forbidden concrete terrain/lighting dependency was added.
- [ ] Every cross-domain value has documented units, space, lifetime, and revision.
- [ ] Invalidation is scoped, lossless until consumed, and has an observable reason.
- [ ] Stable-frame work and each rebuild path are covered by counters/tests.
- [ ] Stage reads/writes and dispatch/resource contracts still match the pipeline.
- [ ] Coordinate, HDR/color, occupancy, and resource-format invariants are upheld.
- [ ] Verification matches the table above; unrun Unity gates are explicitly marked.
- [ ] Architecture/diagnostics docs match the resulting code.
- [ ] `git diff --check` is clean.
- [ ] The `TL-CHANGE` evidence bundle is attached and every applicable gate passes.

## 9. Migration plan for the frame-orchestration debt

This plan is the required order for removing `TerrainRenderer → LightingEngine`.
The stages are separate merge units. Do not land a later stage before the prior
gate passes. Runtime implementation has not yet been migrated to this target.

### Gate A — neutral contract and exchange

Add the DTOs and `ITerrainLightingExchange` described above under
`Kern.Contracts`; implement the data-only exchange in the game composition layer;
register exactly one singleton. Move `ILightingGeometryContributor` and its
purpose-specific field contexts into the neutral contract and update references.

**Accept only if:** Contracts assembly has no `Kern.*` implementation reference;
all DTO fields document units/lifetime; exchange tests prove duplicate registration
fails, out-of-order/generation-mismatched operations fail, unacknowledged records
are retained, and accepted values cannot be mutated by the caller. No production
call flow changes in this gate.

### Gate B — terrain change journal and lighting acknowledgement

Replace `TerrainRenderer.InvalidateRegion` and world-reset invalidation calls with
published `TerrainLightingChange` records. Lighting consumes them before solving
and transfers them to its own pending invalidation state before acknowledgement.

**Accept only if:** tests cover region, full reset, world-generation replacement,
replay after a failed transfer, contiguous acknowledgement, coalescing without
identity loss, and out-of-stable-region discard under the current forced-full
reanchor policy. Static inspection shows no new path that mutates Lighting state
from Terrain.

### Gate C — published frame and lighting-owned tick

Terrain publishes a same-frame snapshot only after committed geometry and
presentation decisions. Move `UpdateLighting` and budget capture into
`LightingEngine.LateUpdate` at execution order `200`; retain
`TerrainRenderer.LateUpdate` at `100`. Publish/consume requirements and lighting
output state through the exchange. The lighting engine uses the snapshot's exact
camera, viewport, contributor, and geometry revision. It does not accept stale
frames or solve an uncommitted window.

**Accept only if:** tests prove one solve request per ready frame, zero requests
for absent/stale snapshots, correct held-view behavior during teleport, no solve
against a pending window, dynamic updates on each ready frame, and full solve
before output publication after quality re-enable. A production-path check proves
the published texture/rect and Terrain binding validation occur in the stated
order. The runtime scan has no `TerrainRenderer → LightingEngine` references.

### Gate D — remove transitional wiring and lock the boundary

Delete the old direct injection/call paths and update dependency documentation.
Add a rule to `tools/Kern.ArchitectureLinter` that rejects every forbidden
cross-domain dependency in the source/assembly boundary, including aliases and
fully qualified references; register that rule in the default linter run.

**Accept only if:** linter fixtures fail for each prohibited dependency form and
pass for the neutral contract; the full architecture linter passes; both manual
`rg` scans show no Terrain/Lighting implementation references outside the
allowlisted neutral contract; lifecycle, journal, and production-frame gates
above pass. Remove this debt entry only in that PR.

No stage may replace the concrete injection with an interface that mirrors
`InvalidateRegion`, `InvalidateStaticCache`, `UpdateLighting`, or
`CaptureBudgetViolationIfNeeded`. Such an API preserves command ownership in
Terrain and fails `TL-API` even if the type is located in `Kern.Contracts`.

## 10. Initial conformance debt

The baseline that prompted this migration had cross-domain direct calls and
concrete wiring in `TerrainRenderer` (including `LightingEngine` access), and
`LightingPresentation` called a type in `Kern.World.Terrain`. The source changes
now remove those implementation references and old invalidation/update entry
points; terrain surface shader globals are owned by the shared
`Kern.World.Common.Rendering` namespace. `ILightingGeometryContributor` and its
material/emission and ambient-occlusion contexts live in the neutral contract.

Migration evidence as of 2026-09-24:

- Gate A contract/exchange source tests pass, including contract assembly
  boundaries, singleton binding cardinality/identity, sequence and generation
  validation, durable acknowledgements, and value-copy behavior. Unity assembly
  compilation was not run.
- Gate B journal/applier source tests pass for regions, full resets, generations,
  retries, stable-region filtering, and forced-full-reanchor behavior. Scoped
  Terrain-to-Lighting invalidation scans are empty.
- Gate C code is connected: Terrain publishes requirements-driven committed
  frame snapshots; Lighting stages journal changes, runs its ordered tick, and
  acknowledges only after success. The source-level exchange suite passes.
  Unity compilation and production runtime checks for same-frame ordering,
  teleport presentation, dynamic dispatch, re-enable full solve, output texture
  rectangle, and Terrain binding remain pending.
- Gate D boundary fixtures pass for imports, aliases, alias chains, fully
  qualified references, and the neutral contract. Additional regression tests
  cover nested UXML localization references and the relocated Bootstrap scope
  allow paths. The selected boundary rule reports zero production violations.
  The full architecture linter checked 54 rules and exited 0 with 0 errors and
  15 warnings. Its exact output and classification are recorded in
  `docs/architecture/evidence/terrain-lighting-architecture-linter-2026-09-24.txt`
  and `docs/architecture/evidence/terrain-lighting-linter-classification-2026-09-24.md`;
  the initial 209 findings are retained in
  `docs/architecture/evidence/terrain-lighting-architecture-linter-initial-2026-09-24.txt`.
  The localization rule now recursively scans nested UXML files, resolving 202
  false dead-key findings without deleting locale keys. Bootstrap pattern rules
  recognize the existing composition roots at their relocated `Scopes` paths.
  Gate D is accepted. Remaining warnings are itemized in the classification
  evidence; Unity production-path proof remains pending for Gate C.

Commands and results: `dotnet test
tools/Kern.WorldLightingExchangeTests/Kern.WorldLightingExchangeTests.csproj
--no-restore` passed 21/21; `dotnet test
tools/Kern.ArchitectureLinter.Tests/Kern.ArchitectureLinter.Tests.csproj
--no-restore` passed 10/10; the selected boundary rule and scoped source scans
passed with zero cross-domain references. `dotnet run --project
tools/Kern.ArchitectureLinter/Kern.ArchitectureLinter.csproj --
--project-root .` discovered 54 rules and exited 0 with 15 warnings.
`dotnet build Kern.Runtime.csproj --no-restore` did not reach C# compilation:
it failed on `CS0006` because generated Unity 6000.5 source-generator/analyzer
DLLs are absent. No Unity operation was run.

The first migration is incomplete until every applicable gate above is
accepted. New code MUST NOT restore or expand a cross-domain dependency.
