# Kern standalone tools

Инструменты здесь собираются отдельно от Unity-проекта и не являются
runtime-кодом игры. Все текущие проекты — `net10.0`.

## Архитектура и качество

- `Kern.ArchitectureLinter` — статические правила asmdef, DI, сцен, UI и
  rendering-контрактов.
- `Kern.DesignSystem` — анализ и генерация данных дизайн-системы.
- `Kern.DisplayTests` — standalone-тесты display/HDR-поведения.

## Планета и UI-ассеты

- `Kern.UIAssets` — подготовка UI-ассетов.

## Terrain и lighting

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
