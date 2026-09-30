---
name: hdr-color-contract
description: >-
  Kern HDR/color pipeline contract: scene-referred linear working space, paper white units, radiance
  format rules, URP output transform ownership, DisplayFinal perceptual/linear space, UI brightness,
  HDR calibration screen, safe-start HDR switch, and HDROutputController usage. Use when touching
  lighting shaders, emission, bloom, tonemapping, post-processing, color grading, LUT, display output,
  UI colors, calibration, or any code with DisplaySettings / HDROutput / PaperWhite / Tonemapping /
  CompositeFinal / DisplayFinal / VisualTuning. Triggers on: saturate on radiance, clamp color,
  sRGB in shader, UNorm light buffer, ARGBHalf, paper white, HDROutputReconciler, VisualTuning,
  BT2390, detectPaperWhite, HDRCalibrationScreen, HDRSwitchPending, HDROutputSettings.
---

# Color, brightness, and HDR

This is a contract, not a recommendation. Every number in the render must have a known color space and a known unit.

## Brightness unit

The working color space of the scene is linear, scene-referred, with no upper bound. `1.0` is reference white, i.e. the display's paper white (`DisplaySettings.PaperWhiteNits`, 100–400, defaulting from `PostProcessLook.DisplayCalibration`). A lamp at `8.0` means eight paper-white units — not an error and not "needs clamping". The range above 1.0 belongs to light sources, fire, arcs, flashes, and specular highlights.

## Prohibitions (each silently kills HDR)

- **FORBIDDEN** to cap radiance from above: `saturate`, `clamp(x, 0, 1)`, `min(x, 1.0)` on scene color, lighting, emission, bloom. `saturate` is permitted only on values that are by definition in 0..1: albedo, alpha, masks, coefficients, coordinates.
- **FORBIDDEN** to apply gamma or sRGB encoding inside lighting shaders. Textures flagged sRGB are decoded by the GPU once, at sample time, and nowhere else.
- **FORBIDDEN** to store radiance in UNorm format. Everything carrying light uses `ARGBHalf` or higher (`_RadianceDirect`, `_RadianceBounce`, `_WorldLightTexture`, `_StaticEmissionField`). `ARGB32` is allowed only for masks and coefficients (`_LightingMaterialField`, AO).
- **FORBIDDEN** to treat HDR as a separate artistic render ("take an SDR image and multiply by brightness"). SDR and HDR are one scene-referred master with different output transforms applied at the final step.

## Output transform

Done by URP, not us: `HDROutputReconciler` holds a runtime Volume with `Tonemapping` in `Neutral` mode, BT.2390 range compression, `detectPaperWhite`/`detectBrightnessLimits` disabled. The same Volume carries `ColorAdjustments.postExposure` (`PostProcessLook.Exposure.Stops`), so URP applies it right before its own curve. Adding a custom tone transform on top of URP produces double-mapping and is **FORBIDDEN**.

## Frame order (`PostProcess.compute`)

scene → bloom (`CompositeFinal`) → URP: `ColorAdjustments.postExposure` (+1 stop, `PostProcessLook.Exposure`) and Neutral tonemapping → `DisplayFinal`: divide by paper white → server LUT (`ApplyCubeLut`, HDR-normalized) → vignette → eigengrau → multiply by paper white in `ToDisplayOutput` → PQ/scRGB encoding done by URP.

## Color space inside `DisplayFinal`

After the paper-white division the frame is linear relative to screen white, **not** sRGB-encoded — URP encodes after us. Therefore:
- Colors/thresholds specified as encoding-level values (e.g. `#16161D`) are converted to linear via `PerceptualToLinear` at the point of application.
- Frame brightness is compared via `LinearToPerceptual`.
- Light addition happens in linear space.

Pre-converting constants "forward" in C# is **FORBIDDEN**: `#16161D` must appear in code as `#16161D`.

## UI

Interface white equals paper white, not peak brightness. A menu at 1000 nits is a defect.

## Calibration

`detectPaperWhite` and `detectBrightnessLimits` are disabled; white point and peak come from user settings. Source is `HDRCalibrationScreen` with a pattern from the output pass (`_CalibrationPattern` in `PostProcess.compute`). Rendering calibration samples via UI is **FORBIDDEN**: UI white equals paper white and cannot represent peak brightness.

## Safe start

Switching output mode sets `DisplaySettings.HDRSwitchPending` and writes the file immediately (`Save`, not `SaveDeferred`). A flag found at startup means the switch was never confirmed — the game starts in SDR. Applying a saved output mode bypassing this check is **FORBIDDEN**: a user with an incompatible output would otherwise be stuck on a black screen.

## HDR detection

Output state is read only via `HDROutput` / `HDROutputController`. Querying `HDROutputSettings.main` directly from game code is **FORBIDDEN**, as is calling `RequestHDRModeChange` outside the controller.
