# Kern standalone tools

Инструменты здесь собираются отдельно от Unity-проекта и не являются
runtime-кодом игры. Все standalone-проекты в `tools/` используют `net10.0`
и лежат в каталоге своей категории.

## Структура

- `architecture/` — правила и инвентарь архитектуры: `Kern.ArchitectureLinter`,
  `Kern.ArchitectureLinter.Tests`, `Kern.RepositoryTaxonomy`.
- `tests/` — standalone-тесты и проверки: `Kern.DisplayTests`,
  `Kern.FrameHarness.Tests`, `Kern.LightingTests`, `Kern.TerrainCrystalTests`,
  `Kern.TerrainRasterTests`, `Kern.TerrainTests`,
  `Kern.WorldLightingExchangeTests`.
- `harness/` — измерительные обвязки и бенчмарки: `Kern.CrystalPixelHarness`,
  `Kern.FrameHarness`, `Kern.TerrainBench`.
- `generators/` — генераторы ассетов: `Kern.CellPalette`, `Kern.DesignSystem`,
  `Kern.SidebarIconsGenerator`, `Kern.UIAssets`.
- `lib/` — общие внешние сборки для standalone-проектов.

## Архитектура и качество

- `architecture/Kern.ArchitectureLinter` — статические правила asmdef, DI,
  сцен, UI и rendering-контрактов.
- `architecture/Kern.RepositoryTaxonomy` — инвентарь переносов и проверка
  GUID/метаданных/каталогов.
- `architecture/Kern.ArchitectureLinter.Tests` — сценарии правил линтера.

### Локальный набор и GitHub Actions

`bash scripts/run-ci.sh` запускает тесты ArchitectureLinter, source-only lint,
DisplayTests, TerrainTests, WorldLightingExchangeTests и FrameHarness.Tests;
затем собирает DesignSystem и GenerateCrystalFixtures с warnings as errors и
запускает lighting transport, crystal и raster harnesses. Это standalone-проверки,
а не запуск Unity или измерение GPU production-пути.

В `.github/workflows/architecture-lint.yml` остался один ручной workflow
(`workflow_dispatch`), job `unified-ci` отключён через `if: false`.
Автоматических проверок push/PR, nightly soak и сборок игроков сейчас нет.

`SettingsProbe` находится внутри
`architecture/Kern.ArchitectureLinter/SettingsProbe`, правило
`KERN-SETTINGS-PROBE`. Его код включается только при заданном `UnityManagedPath`
и define `KERN_SETTINGS_PROBE`. Текущий csproj автоматически ищет Unity 6000.6.0f1,
хотя версия проекта уже 6000.7.0b4; каталога отдельного `Kern.SettingsProbe` нет.
Без define правило возвращает пустой результат, не выполняя reflection-проверки.
Успешный source-only lint не подтверждает валидацию настроек этим probe.

## Генераторы ассетов

- `generators/Kern.UIAssets` — wrapper с режимами `all`, `menu` и
  `sidebar-icons`. Сейчас его `Kern.MenuAssetsGenerator` dependency отсутствует
  (`tools/generators/Kern.MenuAssetsGenerator/Kern.MenuAssetsGenerator.csproj`);
  при этом поиск корня требует этот файл для любого режима. Поэтому все режимы
  wrapper завершаются ошибкой до запуска генератора. Не считать приведённые ниже
  команды рабочими в текущем checkout.
- `generators/Kern.DesignSystem` — анализ и генерация данных дизайн-системы.
- `generators/Kern.SidebarIconsGenerator` — генерация иконок боковой панели.
- `generators/Kern.CellPalette` — обновление цветов клеток в конфигурации мира.

Команды `Kern.DesignSystem compare-components` и `check-fit` требуют карту
`visual/kern-ui/component-map.json`. В текущем checkout её нет, поэтому они не
пересобирают отчёты component drift/layout fit; сохранённые отчёты в
`docs/design/` помечены как исторические.

```sh
dotnet run --project tools/generators/Kern.UIAssets/Kern.UIAssets.csproj -- all
dotnet run --project tools/generators/Kern.UIAssets/Kern.UIAssets.csproj -- menu
dotnet run --project tools/generators/Kern.UIAssets/Kern.UIAssets.csproj -- sidebar-icons
```

`Kern.SidebarIconsGenerator` как отдельный проект в дереве присутствует, но
wrapper не обходит отсутствующую menu dependency даже в режиме `sidebar-icons`.

## Terrain и lighting

Нормативные Terrain/Lighting требования и review gates описаны в
[`TERRAIN_LIGHTING_STANDARD.md`](../docs/architecture/TERRAIN_LIGHTING_STANDARD.md).
Первый исполняемый слой сравнительного harness реализован в `Kern.FrameHarness`:
он анализирует captures и сравнивает before/after; опциональный JSON-экспорт
существующего PlayMode benchmark. Полный replay и production/GPU-проверки пока
не реализованы.

- [`harness/Kern.FrameHarness`](harness/Kern.FrameHarness/README.md) — проверка captures,
  счётчиковые правила OPT-1 для S0/S1/S4, отдельные CPU/GPU-бюджеты, PASS/FAIL/INCOMPLETE.
- `harness/Kern.TerrainBench` — бенчмарки terrain.
- `tests/Kern.TerrainTests` — standalone terrain tests.
- `tests/Kern.WorldLightingExchangeTests` — контракты и обмен Terrain/Lighting без Unity Editor.
- `tests/Kern.LightingTests` — lighting harness и fixtures.
- `tests/Kern.TerrainCrystalTests` — проверки кристаллической анимации из исходного проекта.
- `tests/Kern.TerrainRasterTests` — raster/contact-AO regression checks.

## Правила

- `bin/`, `obj/` и `TestResults/` — generated output, не исходники.
- Tool не доказывает production-поведение Unity, если он не проходит через
  реальный production path; особенно это относится к GPU/visual claims.
- Общая архитектурная карта находится в
  [`../.agents/repository-map.md`](../.agents/repository-map.md).
