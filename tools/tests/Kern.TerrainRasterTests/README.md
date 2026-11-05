# Terrain raster and contact AO regression

Run `dotnet run --project tools/tests/Kern.TerrainRasterTests/RunRasterTests` (the .NET
SDK, clang++ and the C++ test backend are required).
The local pre-commit hook and the architecture CI job run this check. CI currently
has manual workflow triggers; the hook supplies automatic local coverage.

The runner reads the current production HLSL, adapts vector constructors to the
existing clang float32 shim, and executes `LoadTerrainCellVertex`,
`TerrainGeometryCoverage`, and the AO sampling functions. An independent CPU
triangle rasterizer interpolates the shader outputs. Expectations come from a
convex polygon half-plane oracle sampled at raster positions between logical
pixel centers.

Atlas-addressing checks execute the production UV resolver for World-anchored
packs: all 256 descriptors, 14/16-column sheets, 32 texel centers and positive/
negative world positions (81,920 cases), plus unchanged world-sheet addressing
and frame animation. An independent rotation/mirror and atlas-offset oracle
checks the selected variant and sampling bounds. Restoring the early World-sheet
return must fail the `world-anchor-skips-autotiles` mutation.

Coverage includes exact displaced polygon edges, adjacent cell seams read
from shared grid nodes of neighbouring cells, background geometry isolation,
shader-computed phase (every k/1000 value and every faceted value against an
independent half truncation), shader-computed grid nodes (classic,
against `TerrainVertexDistortionCalculator.ComputeNode`), per-vertex buffer
reads (at most 28), the ID-mesh corner index order, and the real
`LoadTerrainCellVertex` on three varied worlds exported by the terrain stand
(`ExportWorldsForHlslShim`): every vertex attribute must equal the frozen CPU
reference (`TerrainQuadBuilder.FillQuad` in the test tree), including the
view-offset and door-overlay addresses. Shader mutations of the relief,
occlusion, pack-wall, node jitter sign and round-block
rim rules must fail (the round-block rim must follow the round silhouette), displaced autotile UV continuity at a shared
edge, and contact AO shape sensitivity at
8/16/32/64 texels per cell. It verifies AO carrier padding and checks the
signed-distance edge helper against axis-aligned and diagonal distance oracles.
The AO field stores a half-cell contact falloff and screen shading reads one
mip-zero sample. The runner injects a second fragment
quantization and a flat contact response in temporary generated code; both defects
must fail. Repository files are never mutated.
The analytic flat-cell distance is checked against the four-corner polygon distance.

This is an algorithm regression, not a Unity render test. It cannot validate
Metal compilation, runtime texture bindings, mesh submission order, or the final
camera image. The MainGame PlayMode test checks runtime wiring separately. The
sampling fixture models bilinear mip-zero filtering at the receiver position.

Raised terrain geometry uses the actual `TerrainDepth.hlsl` forward vertex
projection. Independent expectations cover ground/top height planes, perspective
width (rather than a translated face), camera response, outward normals, wall UV
depth, exact POSITION-only endpoint decoding, face bounds and ground/field
isolation. Twelve injected mutations must fail. Source guards reject screen-only
field accesses in lighting passes, reserved HLSL identifiers and TEXCOORD vertex
requirements. The native runner remains an algorithm check, not Metal/render proof.

`dotnet test tools/tests/Kern.TerrainTests` additionally executes real volume topology
and mesh-construction code with published-fact and mesh-upload test backends:
internal faces, destruction, round contour area, grid/negative origin, shape-only
invalidation, wire-format values, ordered side indices, stable upload behavior,
and disposal/recreation. MainGame PlayMode asserts the production POSITION-only
layout and real side triangles, then compares six camera/zoom cases with real
textures, lighting and fisheye. Those captures require a separate Unity execution.

Optional local HLSL grammar/type check (not a production render):
`glslangValidator -D -V -S vert -e Main -I. tools/tests/Kern.TerrainRasterTests/Native/Terrain/volume-syntax.hlsl -o /tmp/kern-volume-syntax.spv`.
