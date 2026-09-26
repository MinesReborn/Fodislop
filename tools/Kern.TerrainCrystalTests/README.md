# Original OpenMines crystal checks

Run `node tools/Kern.TerrainCrystalTests/run.js`. These are auxiliary CPU checks
of production HLSL, not Unity shader compilation or a visual/GPU regression test.

X-crystals use the OpenMines `Unlit_TerrainShader.shader` branch `animType == 5`
(lines 335–373), with colors/speed from `CellRender.cs` (395–434). The shader
executes the original equation in gamma space; the caller converts the linear
terrain atlas sample to gamma and the result back to linear. This preserves the
original working space while leaving world lighting in its existing space.
No normal maps, derived light directions, extra facet highlights or color masks
are part of this effect.

`PrismaticFlowMap.bytes` is the original 160×128 RGBA8 phase region, extracted
from the 2048×2048 OpenMines terrain atlas: x=400,y=368,width=160,height=128.
Rows are flipped for Unity raw texture loading; no resampling/recoloring occurs.
Regenerate with `node tools/Kern.TerrainCrystalTests/generate.js` (requires the .NET
and the sibling OpenMines checkout). Tests require neither Pillow nor OpenMines;
they verify the committed phase data hash and compare 12,000 color samples to an
independent Python/colorsys evaluation of the source equation. Wrong Y and wrong
luminance mutations must fail.

Cost: crystals retain one phase-map sample (80 KiB, Bilinear, no mipmaps), HSV hue,
one sine, the original polynomial and gamma/linear conversion. The rejected
reflection path's normal-map read, four neighboring light reads, derivatives and
400 KiB texture are removed. No extra passes, dispatches or per-frame allocations
are added.

Remaining validation in Unity: compile both Terrain passes and inspect animated
X-crystals in the running game. The CPU tests do not establish final image quality,
material binding or GPU performance.

The crystal phase lookup now uses world-anchored 1/32-cell centers, before
normalizing to the original 10×8 phase sheet. Time and the original color
formula remain continuous/unchanged. Both Terrain passes call this same helper.
16,384 subpixel pairs must share phase coordinates; adjacent pixels advance by
exactly one sample. Removing the position quantization must fail the CPU test.
Cost: one float2 floor and scale/bias, no extra texture reads or allocations.
