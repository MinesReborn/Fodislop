---
name: lighting-guide
description: >-
  Kern lighting system guide: dataflow, stage boundaries, DDA call placement rules, telemetry
  metrics, and static/dynamic split contract. Use when editing any lighting shader or C# —
  geometry cache, cascade trace/resolve, dynamic polar, bounce cache/solve, or composite.
  Triggers on: LightingTypes.hlsl, DDA.hlsl, CascadeTrace, CascadeResolve, DynamicPolar,
  DynamicLightTrace, BounceCache, BounceSolve, CompositeLighting, TraceLightSegment,
  TraceRadianceSegment, LightingDdaSegments, LightingDdaTexelVisits, SolveCascade, ResolveDirect,
  BuildCellSolidMask, SolveDiffuseBounce, BuildBounceTaps, IFrameTelemetry.
---

# Lighting guide

## Before making a change

1. Read [LIGHTING_ARCHITECTURE.md](../../../docs/architecture/LIGHTING_ARCHITECTURE.md) — dataflow and stage contracts.
2. Identify which stage the change belongs to:

| File | Stage |
|------|-------|
| `LightingTypes.hlsl`, `Extinction.hlsl`, `GeometryField.hlsl`, `DDA.hlsl` | Shared primitives |
| `GeometryCache/GeometryCache.hlsl` | `BuildCellSolidMask` |
| `Cascades/CascadeTrace.hlsl` | `SolveCascade` (DDA traversal) |
| `Cascades/CascadeResolve.hlsl` | `ResolveDirect` (atlas lookup) |
| `Dynamic/DynamicPolar.hlsl` | `TraceDynamicPolar`, `DynamicRadianceFromPolar` (DDA) |
| `Dynamic/DynamicLightTrace.hlsl` | `SolveDynamicLighting`, `ComposeDynamicLighting` |
| `Bounce/BounceCache.hlsl` | `BuildBounceTaps`, `BuildBounceFilter` (DDA) |
| `Bounce/BounceSolve.hlsl` | `SolveDiffuseBounce` |
| `Composite/CompositeLighting.hlsl` | `CompositeLighting` |

## Prohibitions

**FORBIDDEN** to add DDA calls (`TraceLightSegment`, `TraceRadianceSegment`) inside:
- `CascadeResolve`
- `DynamicLightTrace`
- `BounceSolve`
- `CompositeLighting`

## Metrics and verification

- Any change to transport stages (CascadeTrace, DynamicPolar, BounceCache) must increase `LightingDdaSegments` or `LightingDdaTexelVisits` in `IFrameTelemetry`.
- After changing transport math verify:
  - All debug views (0–9) show the expected picture.
  - `LightingDdaSegments` / `LightingDdaTexelVisits` did not grow unexpectedly.
  - FPS did not drop relative to baseline.

## Architectural constraints

- Do not add "quality step budget" or frame skipping to DDA — walls must be accurate.
- Static/dynamic split is preserved: cascades are cached until terrain/emission changes; dynamic light is solved per-frame.

## Research materials

Academic papers on GI and Radiance Cascades are in [`docs/lighting-research/`](../../../docs/lighting-research/README.md).
