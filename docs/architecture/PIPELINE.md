# Render Pipeline

This is a compact map of the current world-rendering path. For ownership,
invalidation and review gates of the Terrain–Lighting boundary, see
[`LIGHTING_ARCHITECTURE.md`](LIGHTING_ARCHITECTURE.md) and the normative
[`TERRAIN_LIGHTING_STANDARD.md`](TERRAIN_LIGHTING_STANDARD.md). Source code owns
exact bindings and dispatch details.

## Lighting fields

The render region is shared in world coordinates, but Terrain and Lighting own
separate caches. `TerrainRenderer` commits/presents terrain and publishes a
fresh snapshot through `ITerrainLightingExchange`; `LightingEngine` consumes
that snapshot later in the frame. See the architecture document for publication
and acknowledgement ordering.

```text
Terrain mesh + atlas
    ├── LightingMaterialField ──► MaterialField (ARGB32, no mipmaps)
    │                              └──► geometry cache / solid mask
    ├── LightingMaterialField ──► StaticGlowField (ARGBHalf)
    └── LightingAmbientOcclusionField ──► AO field (R8, 32 texels/cell)

MaterialField + StaticGlowField
    └──► static radiance cascades ──► ResolveDirect ──► StaticDirect (ARGBHalf)

MaterialField + dynamic source data
    └──► dynamic polar transport ──► DynamicDirect (ARGBHalf)

StaticDirect + DynamicDirect + material/albedo
    ├──► diffuse bounce
    └──► composite ──► published world light texture (ARGBHalf)

Terrain fragments sample the published light and AO fields for world presentation.
```

`FieldPixelsPerCell` controls the material/transport raster used by DDA and
cascade probes. `LightPixelsPerCell` controls direct-light receivers and the
published lightmap; it must be a power of two no greater than
`FieldPixelsPerCell`. Defaults are 2 and 2. Quality overrides are explicit;
zoom does not select field density. AO remains 32 texels per cell. A region that
exceeds allocation limits fails explicitly; the pipeline does not silently
lower resolution. See `LightingConfigHolder` and `LightingQualityTuningController`
for current validation and limits.

The cascade radiance atlas is stored in GPU buffers, not a `RenderTexture`.
Dynamic-light polar and distance resources have their own formats and allocation
rules; consult their resource owners rather than inferring formats from this
overview.

## Terrain fragment and display path

The Terrain shader samples atlas/material data, resolves the cell silhouette and
animation/decal inputs, then applies world lighting and contact AO according to
the active material flags. Specialized debug views have distinct shader paths;
the lighting AO view is value 9. Do not infer that every nonzero lighting debug
view bypasses the same set of fragment stages.

```text
Terrain mesh + atlas + world lighting/AO
    └──► Terrain color ──► world camera ──► WorldBloom
        ──► URP exposure/tonemapping ──► DisplayFinal ──► display output
```

`DisplayFinal` is the display-processing kernel in `PostProcess.compute`;
bloom is a separate world pass. URP owns exposure and tonemapping. The actual
output transform depends on the active URP/display configuration; follow the
HDR/color contract when changing it.

## Verification boundary

Standalone tests and source checks can validate data contracts and orchestration,
but do not prove Unity assembly/DI behavior, production frame ordering, shader
bindings, GPU resource lifetime, or rendered output. Those claims require the
authorized Unity production path. Current Terrain–Lighting review status and
known unverified runtime gates are tracked in `LIGHTING_ARCHITECTURE.md`.
