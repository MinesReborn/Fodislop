# Kern standalone tools

Инструменты здесь собираются отдельно от Unity-проекта и не являются
runtime-кодом игры. Все standalone-проекты в `tools/` используют `net10.0`.

## Архитектура и качество

- `Kern.ArchitectureLinter` — статические правила asmdef, DI, сцен, UI и
  rendering-контрактов.
- `Kern.DesignSystem` — анализ и генерация данных дизайн-системы.
- `Kern.DisplayTests` — standalone-тесты display/HDR-поведения.

## Планета и UI-ассеты

- `Kern.UIAssets` — единая точка входа генерации UI-ассетов. Команды `all`,
  `menu` и `sidebar-icons` запускают профильные реализации в соседних проектах
  `Kern.MenuAssetsGenerator` и `Kern.SidebarIconsGenerator`.

```sh
dotnet run --project tools/Kern.UIAssets/Kern.UIAssets.csproj -- all
dotnet run --project tools/Kern.UIAssets/Kern.UIAssets.csproj -- menu
dotnet run --project tools/Kern.UIAssets/Kern.UIAssets.csproj -- sidebar-icons
```

## Terrain и lighting

Принципы оптимизации, проверяемые сценарии и спецификация единого harness:
[`TERRAIN_LIGHTING_OPTIMIZATION.html`](../docs/architecture/TERRAIN_LIGHTING_OPTIMIZATION.html).
Первый слой реализован в `Kern.FrameHarness`: анализ captures и сравнение
before/after, опциональный JSON-экспорт существующего PlayMode benchmark.
Полный replay и production/GPU-проверки пока не реализованы.

- [`Kern.FrameHarness`](Kern.FrameHarness/README.md) — проверка captures, счётчиковые
  правила OPT-1 для S0/S1/S4, отдельные CPU/GPU-бюджеты, PASS/FAIL/INCOMPLETE.
- `Kern.TerrainBench` — бенчмарки terrain.
- `Kern.TerrainTests` — standalone terrain tests.
- `Kern.LightingTests` — lighting harness и fixtures.
- `Kern.TerrainCrystalTests` — проверки кристаллической анимации из исходного проекта.
- `Kern.TerrainRasterTests` — raster/contact-AO regression checks.

## Правила

- `bin/`, `obj/` и `TestResults/` — generated output, не исходники.
- Tool не доказывает production-поведение Unity, если он не проходит через
  реальный production path; особенно это относится к GPU/visual claims.
- Общая архитектурная карта находится в
  [`../.agents/repository-map.md`](../.agents/repository-map.md).
