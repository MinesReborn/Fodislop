# PIPELINE

`F` — размер поля освещения = клетки региона × scale (scale выбирает `SelectStablePixelsPerCell` по лимиту текстуры, лимиту атласа и бюджету лучей).
`A` — поле геометрии AO = клетки региона × максимальный scale, разрешённый лимитом текстуры.
Регион ≈ 128×96 клеток. `S` — разрешение экрана.

## Освещение

```
меш террейна ──[LightingMaterialField, MRT]──┬──► MaterialField  (F, RGBA32, без мипов)
                                             └──► StaticEmission (F, ARGBHalf)

меш террейна ──[LightingAmbientOcclusionField, alpha only]──► AmbientOcclusionField (A, RGBA32)

                      ┌── MaterialField
StaticEmission ──────┴──[SolveCascade]──► RadianceAtlas (uint3 × N)
                                              │
                                              ▼
                                       [ResolveDirect]
                                              │
                                              ▼
                                    StaticDirect (F, ARGBHalf)

DynamicLights ───────┴──[TraceDynamicPolar → SolveDynamicLighting]
                                              │
                                              ▼
                                       [ComposeDynamicLighting]
                                              │
                                              ▼
                                       Direct (F, ARGBHalf)

Direct ─────────┐
StaticDirect ───┼──[SolveDiffuseBounce]──► Bounce (F/2, ARGBHalf)
MaterialField ──┘

Direct ─────────┐
StaticDirect ───┼──[CompositeLighting]──► Lightmap (F, ARGBHalf)
Bounce ─────────┘                              │
                                               ▼
                                      global _WorldLightTexture
```

## Террейн (фрагмент, пасс Universal2D)

```
_WorldLightTexture ──[если DebugView≠0]──► выход, шаги ниже не выполняются

BaseMap (атлас) ──┐
subAtlasRect ─────┼──[выборка тайла]──► texColor
tileSizeUV ───────┤
animData ─────────┘
                       │
                       ▼
                   [анимация цвета]──► finalRGB ─────────────┐
                                                             │
маска соседства ──[силуэт: круг + углы]──► finalAlpha       │
                                                             │
_WorldLightTexture ──► lightColor ────────────────────────────┤
AmbientOcclusionField ──[8 radial taps + mean + sqrt]──► AO ─┤
                                           │                 │
                                           ▼                 ▼
                     finalRGB × lightColor × AO ÷ finalAlpha (AO только фону)
                                          │
                                          ▼
                                     цвет пикселя
```

## Экран

```
кадр ──[CompositeFinal: блум + запечённый грейд]──[тонмапп URP]──[DisplayFinal: LUT/кривые, виньетка, зерно]──► экран
```

Тонмаппинг — за URP (Neutral SDR / Neutral BT2390 HDR через `HDROutputReconciler`); своей сигмоиды и хроматической аберрации в коде нет.

## Таблица стадий

| #  | Стадия              | Читает                          | Пишет           | Размер | Когда           |
|----|---------------------|---------------------------------|-----------------|--------|-----------------|
| 1  | Поле материалов     | меш террейна + атлас + анимация цвета   | Material + Emis | F      | геометрия/регион/текстуры|
| 2  | Поле AO             | меш террейна + атлас            | AO occupancy    | A      | геометрия/регион/текстуры|
| 3  | Геометрические кэши | Material                        | SolidMask/Taps  | F      | геометрия/регион|
| 4  | Каскады (стат.)     | Material, StaticEmission        | RadianceAtlas   | N зап. | мир изменился   |
| 5  | Resolve (стат.)     | RadianceAtlas                   | StaticDirect    | F      | мир изменился   |
| 6  | Полярное динамич.   | Material, DynamicLights         | Direct          | F      | источник изменился|
| 7  | Диффузный отскок    | Direct, StaticDirect, Material  | Bounce          | F/2    | свет изменился  |
| 8  | Сведение            | Direct, StaticDirect, Bounce    | Lightmap        | F      | свет изменился  |
| 9  | Выборка тайла       | BaseMap, атрибуты вершины       | texColor        | S      | каждый пиксель  |
| 10 | Анимация цвета      | texColor, animData              | finalRGB        | S      | каждый пиксель  |
| 11 | Силуэт              | displaced cell coverage         | finalAlpha      | S      | каждый пиксель  |
| 12 | AO вокруг блоков    | AO occupancy (только фон)       | множитель       | S      | каждый пиксель  |
| 13 | Освещение           | Lightmap, finalRGB, occlusion   | цвет пикселя    | S      | каждый пиксель  |
| 14 | Постобработка       | кадр                            | экран           | S      | каждый кадр     |
\* Нет динамических источников → динамический direct очищается, остальные стадии используют кэш статического света.

## Ветвления

| Условие                       | Эффект                                                  |
|-------------------------------|---------------------------------------------------------|
| `_WorldLightDebugView == 9`   | AO поле выводится по displaced foreground coverage |
| `_WorldLightDebugView != 0 && != 9` | Выводится тексель Lightmap без стадий 10-14 |
| `!rebuildFields`              | Стадии 1-2 пропущены, используются прошлые текстуры     |
| Лимит текстуры превышен       | `scale` понижается, вплоть до 1                         |

## Отсутствует

| Что                                | Статус                                     |
|------------------------------------|--------------------------------------------|
| Нормали поверхности                | Снесены полностью: объём даёт AO вокруг блоков |
| Эмиссия в стадии 9                 | Читается только отладочными видами         |
| Детали мельче клетки в освещении   | Невозможны: F ≤ 4 текселя на клетку, тайл 32 px |
