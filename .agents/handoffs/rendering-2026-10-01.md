# Хендофф: мировой рендер и сломанное освещение

Состояние на 2026-10-01. Проект: `/Users/murasama/Projects/games/Fodislop`, Unity
6000.6.0f1, URP 2D 17.6, macOS. Исходный план НЕ выполнен; исправление всех
визуальных дефектов и восстановление производительности НЕ подтверждены.

## Глобальный вектор и цель пользователя

Сделать весь мировой рендер дешевле за счёт настоящего растра 32×32 пикселя на
клетку и устранения повторной работы. Это касается террейна, роботов, VFX,
геометрии, шейдерных вычислений, применения света и AO. Рисовать мир сначала в
полном разрешении, затем пикселизовать или уменьшать отдельным проходом — не
выполнение задачи. Геометрия должна соответствовать сетке 1/32 клетки во время
рендеринга. Углы поворота роботов произвольные. UI остаётся полноразмерным.

Перенос света может использовать более редкие пробы. Это НЕ означает, что
материальные поля, геометрия, конечный мировой растр и все карты обязаны
наследовать плотность этих проб. Нельзя автоматически снижать качество,
пропускать кадры или ограничивать частоту света. Пользователь хочет сам выбрать
разрешения и цену этапов через понятные числовые параметры VisualTuning и дебаг UI.
Он прямо спросил про независимое разрешение каждой карты; сейчас этого нет.

Цепочка вывода по исходному плану:

`мир 32×32 → малый bloom → штатный URP exposure/tonemapping → масштабирование + DisplayFinal → штатное HDR-кодирование и UI`.

Нужно сохранить HDR-радианс, яркость источников и владельца exposure/output
transform. SceneExposureScale пользователь потребовал убрать; свойство удалено,
не возвращать. Bloom — quarter-resolution prefilter с четырьмя выборками сцены
и эмиссии, до четырёх уровней Kawase, additive до URP, без большого HDR-композита
и без shared-memory reconstruction. Остаток объединения DisplayFinal с финальным
масштабированием/FinalBlit ещё не закрыт.

Бюджет 200 FPS = 5 мс; это цель, а не достигнутый результат. Приёмка исходного
плана требует реальных production кадров и трёх повторений по 300 измеряемых
кадров после прогрева: 3420×2148, разные зумы, стояние/движение, редкая/плотная
эмиссия. Нужны p50/p95 и стоимость этапов, без выдачи CPU recording time за GPU.

## Текущее главное сообщение пользователя

Освещение по-прежнему сломано на реальной игровой сцене. Последние скрины
18:38 и 19:04 показывают, что внесённые правки не закрыли проблему. Пользователь
отдельно возмущён тем, что тесты проходят, а изображение остаётся плохим.
Не объявлять текущий визуальный дефект исправленным на основании старых тестов.

Ранее были сообщения о:

- мерцании/изменении света и материала при зуме и движении камеры;
- освещении блоками вместо потексельного результата;
- квадрате света при положении робота между клетками;
- потере освещения у стоящего робота;
- снижении разрешения статики, динамики, альбедо и других карт после рефакторинга;
- падении FPS до 7–8 и заявленной пользователем регрессии света с примерно
  0.5 мс до примерно 60 мс;
- накоплении/размножении мировых подписей и других артефактах от тестовых сцен;
- тяжёлом тестовом прогоне, после которого пользователь сообщил о серьёзном
  зависании системы. Причина состояния ОС после перезагрузки не установлена.

Часть частных багов ранее правилась, но это не доказывает, что они отсутствуют
в текущем дереве. Последний скрин важнее старого зелёного отчёта.

## Что сейчас реально находится в конфигурации

Последнее чтение непосредственно перед созданием хендоффа:

`Assets/Scripts/Rendering/PostProcessing/ColorGrading/Contracts/VisualTuning.cs`

```csharp
FieldPixelsPerCell: 2,
CascadeProbePixelsPerCell: 2,
MaximumStaticCascadeDirections: 64,
DynamicNearCells: 6f,
DynamicAngularSampleCount: 8,
DynamicEmitterPointsPerAxis: 3,
DynamicPolarDirectionCount: 64
```

За текущий диалог плотность в рабочем файле менялась с 32 на 4, затем на 2;
плотность проб в последнем чтении тоже 2. Эти изменения не вносил текущий агент.
Считать их внешними/пользовательскими, перечитать файл перед работой и не
переписывать обратно на 32. Комментарий «по умолчанию 32» сейчас расходится с
числовым значением. Не превращать это в объяснение, снимающее ответственность
за сломанный production путь.

Сейчас `FieldPixelsPerCell` одновременно задаёт размер material/albedo,
static emission, static direct, dynamic direct и конечной lightmap. AO отдельно
фиксирован на 32. Поэтому пользователь не может удешевить только свет, сохранив
точную геометрию/альбедо. Независимые параметры карт ещё НЕ реализованы.

Важные авторские значения: AmbientIntensity=0, EmissionScale=12,
DynamicLightIntensity=1, air extinction=0.20, solid extinction=1,
SurfaceReflectionReachCells=0.5, URP exposure +1 stop, bloom=false.
Не менять их для маскировки ошибки. Пользователь самостоятельно подбирает вид.

## Последние внесённые изменения: читать до новых правок

### 1. Исправлена область действия защиты OOM

Пользователь требовал защитить тесты, а предыдущая реализация ошибочно
распространила отказ по свободной RAM на обычную игру. Получалась ошибка:

`[MemoryGuard] Lighting fields and cascade buffers: свободно 27%; аллокация 1025 МиБ нарушит резерв 1638 МиБ`.

Теперь `Assets/Scripts/Core/Diagnostics/MemoryAllocationGuard.cs`:

- `Require` сначала читает `_testProcessLimit`; при нуле сразу возвращается,
  до OS snapshot и проверки бюджета;
- `TestRunRejection` вне тестового scope возвращает null;
- budget устанавливается `BeginTestRun`, снимается `EndTestRun`;
- runtime half-RAM policy убрана; политика тестов — min(3 GiB, 35% RAM),
  резерв max(512 MiB, 20% RAM), 25% overhead к планируемому payload.

Защита вызвана в lighting resource owners перед release/create, но вне теста
эти вызовы не блокируют обычную игру. Не возвращать runtime OOM gate.

`ProcessMemorySnapshot.cs` читает macOS phys_footprint и memorystatus,
Windows физическую RAM, Linux MemAvailable; swap не является запасом для
аллокаций. `Tests/Shared/TestMemoryMeasurement.cs` содержит независимый Timer
250 мс: batch breach завершает только тестовый batch процесс с report/exit1,
GUI breach передаёт отмену на основной поток. Старый environment bypass удалён.

Проверки этого CPU scope fix: 185/185 bounded .NET tests, 5/5 компиляций
Core/World/Tests.Shared/Tests.Editor/Tests.PlayMode, 0 errors. Существующие
compiler warnings: Core 1, PlayMode 2; architecture linter 0 errors, 15 existing
warnings. Это НЕ проверки визуального освещения.

### 2. Правка двойного альбедо/света в композите — визуально не подтверждена

`Assets/Resources/Shaders/Lighting/Composite/CompositeLighting.hlsl`:

- старый `SurfaceReflection(pixel, albedo, centerIncident)` начинал с
  centerIncident, умножал результат на albedo;
- composite добавлял это к centerIncident;
- видимый terrain затем снова умножал lightmap на albedo.

Внесено: `SurfaceIncidentLighting(pixel, centerIncident)` возвращает incident
estimate без альбедо; composite делает
`lerp(combinedDirect.rgb, surfaceIncident, solid)` вместо повторного сложения.
Альбедо применяется видимым материалом один раз. Число passes/texture loads и
размеры ресурсов не увеличены. Это доказанная ошибка формулы, но её исправление
НЕ устранило все дефекты на новом скрине.

### 3. Отсечение поля вне видимого материала — визуально не подтверждено

В `Assets/Shaders/Terrain/Terrain.shader`, `MaterialFieldFrag`, добавлены:

```hlsl
clip(cellCoverage - 0.5);
clip(albedoTexel.a - _AlphaCutoff);
```

Они идут до decals/emission. Expanded carrier ранее записывал RGB за контуром,
а decal мог добавить эмиссию на отсутствующем atlas texel. Geometry/field
resource owners и invalidation не менялись.

Под новое имя функции обновлены только существующие ссылки в
`tools/Kern.LightingTests/NativeHarness.cs` и `NativeTransportScenario.cpp`.
Новые тесты для этих двух shader правок не создавались и не запускались.
Проверен только текст/`git diff --check`. Даже shader compile этих последних
правок не запускался. Не выдавать 185/185 OOM checks за их верификацию.

## Более ранняя реализация в грязном дереве

В дереве около 134 изменённых/новых файлов; это длительная работа и внешние
правки. Не приписывать весь diff последнему агенту и не заменять дерево HEAD.

- Renderer-owned WorldRenderGrid и embedded URP Renderer2D RenderGraph
  выделяют низкоразрешённые world color/depth сразу; исходная камера сохраняется
  для UI/input. Важные файлы: Rendering/Contracts/WorldRenderGrid.cs,
  Rendering/WorldRenderGridCamera.cs, embedded URP WorldRenderGrid.hlsl.
- Геометрия террейна/роботов и shader positions квантованы в world passes;
  effekseer интеграция менялась. Полное выполнение контракта для всех VFX и
  путей камеры не считать доказанным.
- Добавлены WorldBloom.compute, WorldBloomAdd.shader, WorldBloomRenderPass.
- Geometry/statics revision caches, обмен Terrain/Lighting и reanchor atlas
  переиспользование уже интегрированы; нельзя просто включить старый dormant scroll.
- Dynamic per-source tiles теперь Tex2DArray: постоянный slot = layer;
  uploaded-list order отдельный. DynamicTileInfo stride 32 bytes содержит reachIndex.
- При neutral RGB extinction polar depth — RFloat; при unequal RGB — ARGBFloat.
  Tiles при neutral RGB содержат RFloat brightest-channel radiance, восстановление
  через RGB источника; vector reference ARGBHalf. Финальный RGB HDR не UNorm.
- `DynamicLightTileCache.InvalidateAll` инвалидирует receiver и polar caches:
  это исправляло stale depth у стоящего источника при reanchor.
- DDA uniform-cell shortcut допускается только при доказанной одинаковой
  occupancy; static shortcut дополнительно требует нулевой emission во всех
  texels клетки. Он не должен менять перенос или скрывать тонкие блокеры.

## Где продолжать чтение и что ещё НЕ доказано

Основной production путь:

`TerrainRenderer → TerrainLightingFrameSnapshot → LightingEngine.LateUpdate →
LightingUpdateCoordinator → LightingFrameExecutor → GeometryLightingSolver →
StaticLightingSolver / DynamicLightingSolver → IndirectLightingSolver →
LightingPresentation → WorldLightSampling.hlsl → Terrain.shader / WorldSurface.shader`.

Координаты и sampling:

- `GeometryField.hlsl`: MaterialUv/MaterialPixel, MaterialYFlip;
- `DDA.hlsl`: локальные probe anchors, field clip, uniform-cell traversal;
- `Dynamic/DynamicPolar.hlsl`: emitter bounds, near DDA, far optical-depth gather;
- `Cascades/CascadeTrace.hlsl`: ближние пути при билинейном чтении дальнего каскада;
- `Cascades/CascadeResolve.hlsl`: bilinear reconstruction редких проб к dense receivers;
- `TerrainMeshManager.DrawLightingField`: собственные view/projection матрицы,
  full-grid mesh, ViewOffset=0, store actions;
- `TerrainLightingFieldCommon.hlsl`: реальный field vertex pass;
- `TerrainCellData.hlsl`: кольцевое адресование и carrier/silhouette;
- `LightingResourceLayout.cs`: пробная layout validation, spacing = field/probe density;
- `LightingResourceManager.cs`: все карты сейчас привязаны к общему FieldSize.

Возможная проблема для расследования, НЕ установленная причина последнего
скрина: `CheckDiagonalStepOccluded` в DDA классифицирует пару соседних целых
клеток через cell-center solid mask. При displaced/rounded геометрии это не
эквивалентно проверке фактических texels на пересечении луча. Не чинить наугад
и не добавлять DDA в Resolve/Composite.

Нельзя просто выдать разным textures разные размеры: текущие DDA, cache,
resolve и composite исходят из общего `_FieldSize`. Для независимых карт нужны
явные согласованные world→texel преобразования и фактические размеры ресурсов.
Снижение output-light density не должно незаметно снижать плотность material
transport или пропускать его texels. Разные material/emission размеры также
нельзя подключить к одному MRT draw без корректной переработки field passes.

## Почему зелёные тесты не закрывают визуальную проблему

- Последние 185 .NET tests доказывают memory scope/CPU contracts, не картинку.
- Native shader shim — дополнительный анализ, не Metal/production proof.
- Старые surface checks передавали белое альбедо и нулевой center incident;
  они не ловили двойное окрашивание и сложение конечного composite.
- Scalar/vector и optimized/reference comparisons могут иметь общий дефект:
  равенство двух сломанных production вариантов не является физическим oracle.
- Некоторые проверки изображения измеряют усреднение/стабильность/ненулевой
  сигнал и не задают независимое ожидаемое освещение стены/приёмника.

Нужен oracle известной сцены, независимый от production helpers, и реальный
production shader/pass/mesh attributes/SV_POSITION/material keywords/data
textures/camera path. Не новый набор чистых функций вместо существующего
глобального harness. Но сейчас пользователь прямо потребовал писать/читать
код без тестовых прогонов; не продолжать тестовую разработку вопреки этому.

## Старые измерения: только контекст, не текущий результат

Одна движущаяся лампа, 3420×2148 world output, quality 32/4/64/6/8/3/64,
120 warmup + 300 frames ×3, `lighting_traversal_ab_20261001_162336`:

- reference p50 40.9786/40.8943/44.3306 → optimized 21.7153/21.9897/22.2356;
- p95 43.3154/43.4941/49.6388 → 24.3665/24.5919/27.0721;
- image MaxError 0.001953125, ref drift 0.

Это whole-frame, не GPU cost света. Бюджет 5 мс не достигнут; возврат к 0.5 мс
не доказан. Сценарий не проверяет текущие 44 источника/новые настройки/последние
shader правки. 44-source benchmark не завершился; после жалобы его не повторять.

Ранее Metal image pack 9/10 pass; SubcellSource упал на старом ожидании atlas
width 768 после перехода на array width 384. Ожидание поменяли на 384×384×2,
но повторного прогона после OOM жалобы нет. Не представлять это как 10/10 pass.

## Ограничения работы и общения

- Прочитать AGENTS.md и применимые skills: csharp-conventions,
  critical-invariants, lighting-guide, hdr-color-contract; unity-cli только
  при работе с Editor. Terrain/Lighting стандарт обязательный:
  `docs/architecture/TERRAIN_LIGHTING_STANDARD.md`.
- Никаких rollback/reset/restore/revert, включая ручное возвращение своих
  прежних правок или удаление добавленных кода/тестов. Никаких commit/push.
- Не править .prefab/.unity/.asset текстом; сохранять GUID/.meta.
- Не добавлять прямые вызовы между реализациями Terrain и Lighting;
  exchange/ownership gates A–D обязательны, forwarding facade не заменяет их.
- Не подавлять warnings; не чистить кэши и не менять авторские величины,
  чтобы замаскировать ошибку.
- Не объяснять дефект debug mode, Editor overhead, VSync, другими программами,
  swap или нагрузкой ОС. Найти причину в текущем коде/шейдерах/ассетах.
- Пользователь ранее разрешил проверки в открытом Editor, но затем прямо
  потребовал остановить возню с тестами и читать/писать код. Это последнее
  steering; сейчас НЕ запускать tests/benchmarks, тем более тяжёлые.
- Не запускать/закрывать GUI Editor/Hub или builds по собственной инициативе.
  До последнего кода был read-only CLI status: открытый Editor PID 10142,
  port7800, ready. Read-only eval не нашёл LightingEngine в активной сцене.
  Это наблюдение того момента, не состояние нового скрина; перечитать при необходимости.
- Не задавать повторные разрешения на уже авторизованную работу. Пользователь
  раздражён длительным исследованием и остановками; краткие updates, реальные
  правки, без обещания законченности и без отчёта о каждом мелком шаге.
- При вопросе пользователя остановиться и ответить; не редактировать под видом
  ответа. «а настраивать разрешение каждой карты?» был вопросом: на него
  ответили, что независимых настроек ещё нет; в том сообщении код не меняли.
- Не создавать subagents без явной авторизации/инструкции применимого guide.

## Приоритет следующего агента

1. Перечитать текущий код/config и не потерять внешние правки (сейчас поля/пробы 2).
2. Найти конкретную причину оставшегося сломанного lighting production path;
   последние два исправления не считать завершением. Не отвлекаться на harness
   инфраструктуру и не менять всё одновременно без причинной связи.
3. Развязать разрешения карт и транспорта по пользовательскому требованию:
   понятные числовые controls в VisualTuning/существующем «Цена света», без
   функций/скрытого LOD. Согласовать преобразования, владение и invalidation,
   а не только новые поля конфигурации.
4. Устранить повторную дорогую работу в текущем frame light path; стоимость
   объяснять размером targets/dispatch/ray visits, не внешними обстоятельствами.
5. Когда пользователь вернётся к проверкам, только ограниченные production
   проверки с OOM scope, независимым oracle и настоящими resource bounds.
6. Завершить оставшийся мировой render/bloom/output план и evidence bundle.
   Нельзя объявлять успех до реального изображения и замеров; непроверенные
   gates отметить pending без выдачи их за passed.

## Артефакты и записи

Основной журнал долгой работы:
`docs/architecture/WORLD_RENDER_GRID_EVIDENCE.html`.
Он содержит исторические проверки, pending gates и последние исправления.
`docs/architecture/LIGHTING_ARCHITECTURE.md` описывает текущий dataflow;
стандарт имеет приоритет над историческими описаниями.

Последние безопасные checks OOM scope:

- `Logs/Diagnostics/Tests/oom-guard-test-scope-tests.txt`;
- `oom-guard-test-scope-compile-base.txt`, `oom-guard-compile-verified.txt`;
- `oom-guard-test-scope-architecture.txt`, `oom-guard-boundary-scans.txt`.

Read-only Editor diagnosis: `lighting-live-state.cs/.json`,
`lighting-current-editor-commands.json` в том же каталоге Logs.
Logs/Temp могут очищаться между сессиями; отсутствие старого artifact не
разрешает заявлять проверку повторённой или прошедшей.

Последний screenshot пользователя:
`/var/folders/76/x4md2qxs6gj_1k6h2wt0yz800000gp/T/TemporaryItems/NSIRD_screencaptureui_VmxBN3/Screenshot 2026-10-01 at 19.04.37.png`.
Скрин после composite/field fixes; реакция пользователя «помянем».
