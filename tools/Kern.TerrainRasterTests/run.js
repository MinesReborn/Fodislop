#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const root = path.resolve(__dirname, "../..");
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");

const shim = read("tools/Kern.LightingTests/NativeTransportShim.cpp");
const loader = read("Assets/Shaders/Terrain/TerrainCellData.hlsl");
const terrainContour = read("Assets/Shaders/Terrain/TerrainContour.hlsl");
const terrainShader = read("Assets/Shaders/Terrain/Terrain.shader");
const scenario = read("tools/Kern.TerrainRasterTests/scenario.cpp");
const aoShim = read("tools/Kern.TerrainRasterTests/ao-field.cpp");
const ao = read("Assets/Shaders/Terrain/TerrainAmbientOcclusion.hlsl");
const terrainSampling = read("Assets/Shaders/Terrain/TerrainSampling.hlsl");
const lighting = read("Assets/Shaders/Terrain/TerrainLightingData.hlsl");
const terrainGeometryUv = terrainSampling.slice(
  terrainSampling.indexOf("float TerrainUvCross("),
  terrainSampling.indexOf("void TerrainSetResolvedTileIdentity("),
).replace("[unroll]", "");
if (!terrainGeometryUv.includes("TerrainResolveGeometryTileUV(")) {
  throw new Error("Production terrain geometry UV remap was not found");
}

if (terrainShader.includes("TerrainReliefRimRaw(") || terrainShader.includes("TerrainReliefRim(surface")) {
  throw new Error("Terrain.shader must not apply the retired relief rim to visible albedo");
}
if ((terrainShader.match(/BuildTerrainSurfaceInputs\(/g) ?? []).length !== 3) {
  throw new Error("Expected exactly one surface parse in each terrain pass");
}
if ((terrainShader + read("Assets/Shaders/Terrain/TerrainLightingFieldCommon.hlsl"))
  .match(/PixelArtSampleUV\(/g)?.length !== 2) {
  throw new Error("Visible and field albedo paths must share the pixel-grid UV correction");
}
const materialFieldPassAt = terrainShader.indexOf('Name "LightingMaterialField"');
const ambientOcclusionPassAt = terrainShader.indexOf('Name "LightingAmbientOcclusionField"');
if (materialFieldPassAt < 0 || ambientOcclusionPassAt < 0 || materialFieldPassAt > ambientOcclusionPassAt) {
  throw new Error("Terrain material and AO fields must have distinct ordered shader passes");
}
const materialFieldPass = terrainShader.slice(materialFieldPassAt, ambientOcclusionPassAt);
const ambientOcclusionPass = terrainShader.slice(ambientOcclusionPassAt);
if (materialFieldPass.includes("TerrainGeometryCoverageForField") ||
    !ambientOcclusionPass.includes("TerrainGeometrySignedDistance") ||
    !ambientOcclusionPass.includes("if (exteriorDistance >= _TerrainAmbientOcclusionDistance)") ||
    !ambientOcclusionPass.includes("_TerrainAmbientOcclusionDistance, exteriorDistance)") ||
    !ambientOcclusionPass.includes("ColorMask A") ||
    ambientOcclusionPass.includes("TerrainAnimationSampling.hlsl") ||
    ambientOcclusionPass.includes("_PrismaticFlowMap")) {
  throw new Error("AO must have an alpha-only geometry pass without transport or flow-map dependencies");
}
const debugAt = terrainShader.indexOf("if (KernTerrainDebugActive())");
const debugClipAt = terrainShader.indexOf("clip(cellCoverage - 0.5)", debugAt);
if (debugAt < 0 || debugClipAt < debugAt) {
  throw new Error("The terrain debug silhouette must be clipped before its AO/rim output");
}
const rawAoDebugAt = terrainShader.indexOf("_WorldLightDebugView == 9)");
const rawAoClipAt = terrainShader.indexOf("clip(cellCoverage - 0.5)", rawAoDebugAt);
const rawAoSampleAt = terrainShader.indexOf("KernSampleTerrainAmbientOcclusion(", rawAoDebugAt);
if (rawAoDebugAt < 0 || rawAoClipAt < rawAoDebugAt || rawAoSampleAt < rawAoClipAt) {
  throw new Error("The raw AO debug output must use the displaced foreground silhouette");
}

const rim = terrainContour.slice(
  terrainContour.indexOf("// Выключатель каймы."),
  terrainContour.indexOf("// The visible terrain"),
);
const contour = terrainContour.slice(0, terrainContour.indexOf("float2 QuantizeTerrainFaceUV"));
const quantizeUv = `
float2 QuantizeTerrainFaceUV(float2 uv)
{
    float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
    return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
}
`;
const extra = `
float2 round(float2 a) { return {std::round(a.x), std::round(a.y)}; }
float lerp(float a, float b, float t) { return a + (b-a)*t; }
float2 lerp(float2 a, float2 b, float2 t) { return a + (b-a)*t; }
float4 round(float4 a) { return {std::round(a.x),std::round(a.y),std::round(a.z),std::round(a.w)}; }
float4 make_float4(float a, float2 b, float c) { return {a,b.x,b.y,c}; }
float _TestFwidth = 0.0f;
float fwidth(float) { return _TestFwidth; }
float smoothstep(float a, float b, float x)
{
  float t = std::clamp((x-a)/(b-a), 0.0f, 1.0f);
  return t*t*(3.0f-2.0f*t);
}
`;

// Форма поверхности террейна объявлена свойствами материала в
// TerrainMaterialCBuffer.hlsl, но тест срезает директиву include вместе с ней.
// Объявляем нужные тесту величины здесь, с авторскими значениями.
const terrainUniforms = `
float _OrganicBendStrength = 1.0;
float _OrganicBendPivot = 0.35;
float _RoundableCornerRadius = 0.51;
float _ReliefRimDistanceScale = 2.0;
float _ReliefRimFalloff = 0.5;
`;

function translate(source) {
  return source
    .replace(/^#.*$/gm, "")
    .replace("inout TerrainTileUvResult tile", "TerrainTileUvResult& tile")
    .replace("out float2 nearestPosition", "float2& nearestPosition")
    .replaceAll("Texture2D<float4>", "Texture")
    .replaceAll("(TerrainCellVertex)0", "TerrainCellVertex{}")
    .replace(/\(int2\)round\(([^)]+)\)/g, "make_int2(round($1))")
    .replace(/\b(float[234]|int[23]|uint[23])\(/g, "make_$1(");
}

const mutations = [
  null,
  "double-quantize-fragments",
  "ao-flat-contact",
  "ao-to-black",
];
const temporaryDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "kern-terrain-raster-"));

try {
  const cppPath = path.join(temporaryDirectory, "test.cpp");
  const executablePath = path.join(temporaryDirectory, "test");

  for (const mutation of mutations) {
    let candidateLoader = loader;
    let candidateContour = contour;
    let candidateAo = ao;

    if (mutation === "double-quantize-fragments") {
      candidateContour = candidateContour.replace(
        "    return TerrainPolygonContains(\n" +
          "        samplePosition,\n" +
          "        cornersX,\n" +
          "        cornersY,\n" +
          "        0.0,",
        "    float2 fragmentGridSample = (floor(samplePosition * 32.0) + 0.5) / 32.0;\n" +
          "    return TerrainPolygonContains(\n" +
          "        fragmentGridSample,\n" +
          "        cornersX,\n" +
          "        cornersY,\n" +
          "        0.0,",
      );
      if (candidateContour === contour) throw new Error("double-quantize-fragments mutation is stale");
    }
    if (mutation === "ao-flat-contact") {
      candidateAo = candidateAo.replaceAll(
        "contact * _TerrainAmbientOcclusionStrength",
        "_TerrainAmbientOcclusionStrength",
      );
      if (candidateAo === ao) throw new Error("ao-flat-contact mutation is stale");
    }
    if (mutation === "ao-to-black") {
      candidateAo = candidateAo.replace(
        "1.0 - (occlusion * (1.0 - _TerrainAmbientOcclusionFloor))",
        "1.0 - occlusion",
      );
      if (candidateAo === ao) throw new Error("ao-to-black mutation is stale");
    }
    candidateAo = candidateAo.replaceAll("Texture2D<float4>", "AoTexture").replaceAll("SamplerState", "int");
    fs.writeFileSync(
      cppPath,
      shim + extra + aoShim + terrainUniforms +
        translate(candidateLoader + lighting + candidateContour + quantizeUv + rim + terrainGeometryUv + candidateAo) +
        scenario,
    );

    const compile = spawnSync(
      "clang++",
      ["-std=c++20", "-O2", "-ffp-contract=off", cppPath, "-o", executablePath],
      { stdio: "inherit" },
    );
    if (compile.status !== 0) process.exit(compile.status ?? 1);

    const result = spawnSync(executablePath, { encoding: "utf8" });
    if (mutation === null) {
      process.stdout.write(result.stdout);
      if (result.status !== 0) throw new Error(result.stderr);
    } else {
      if (result.status === 0) throw new Error(`Regression check accepted mutation: ${mutation}`);
      console.log(`Mutation ${mutation} rejected: ${result.stderr.trim()}`);
    }
  }
} finally {
  fs.rmSync(temporaryDirectory, { recursive: true, force: true });
}
