# Lighting / Streaming Freeze Handoff

## Текущее состояние

При движении игрока через границу загруженных чанков остаются две проблемы:

1. заметный фриз;
2. на границе экрана на кадр видны квадраты и незаполненная область.

Последняя попытка включить reuse/scroll статического Radiance Atlas сломала освещение целиком. Эта ветка отключена обратно. Качество полного solve восстановлено, но фриз остался.

Нельзя считать задачу решённой снижением частоты освещения, frame skipping, FPS cap или временным пропуском geometry/lighting.

### Последние исправления кода — без проверки в Unity

- После скриншота с чёрной нижней частью: видимый mesh снова следует текущей камере даже при ожидании нового lighting/terrain окна. Ранее ветка ожидания подменяла также presentation viewport старым.
- Residency-check теперь вызывает неблокирующий `ReadChunk` для всех чанков target + halo: `TryGetCell` только проверял RAM и не инициировал загрузку холодных данных. Недостающие на диске чанки запрашиваются через опциональный `IWorldRegionRequester`; его реализует офлайн-транспорт. Реальный сетевой протокол не изменён.
- Terrain передаёт фактический размер окна существующему `DummyMapStreamer.SendMapWindowAsync`. Окно стриминга вокруг игрока (160×160) не гарантировало готовность terrain 192×160 плюс halo. Повторные запросы одной области дедуплицируются, смена мира отменяет запрос.
- В `DummyMapStreamer` удалён принудительный yield после четырёх payload; все нужные дисковые чтения инициируются до первого ожидания. Пакет по-прежнему атомарный; при failed/cancelled чтении частичный пакет не отправляется.
- Dirty-области применяются в старых координатах до scroll кольцевого terrain cache; изменения перекрытия больше не удаляются без обработки. При совмещении dirty и scroll выгружается также обновлённая сетка distortion nodes.
- Пока целевое окно не resident, обрабатывается действующее окно: его dirty-изменения и точная позиция источника. Resize ресурсов отложен до готовности целевого окна.
- Texture refresh использует origin действующего cache, а не ещё не построенного target. Неуспешный build/patch не очищает dirty и не публикует новый origin/lighting.
- В отключённом atlas scroll исправлено преобразование world cells → field texels → probes; несовместимая фаза проб отклоняется. Reuse остаётся выключенным.
- Локальные проверки теперь запускаются из `tools/lighting-tests/Kern.LightingTests.csproj`: layout/binding, 5 CPU golden-сцен, streaming governor, native HLSL transport (3117 проверок) и shader equivalence. Отдельный terrain-transition harness в этот перенос не включён; production terrain tests остаются в `tools/Kern.TerrainTests`.
- Дополнительно вне репозитория выполнены 26 проверок production streaming-кода и извлечённого residency-check: полный halo, cold/missing, дедупликация, failed/cancelled batch и отмена при смене мира. Для fixture из 56 чанков: warm batch — 0 yield, cold batch с одновременным завершением чтений — 1 yield. Это проверка алгоритма ожидания, не замер времени игры. Тестовые файлы репозитория не изменялись; 176 terrain-тестов и 3117 native transport-проверок прошли.

Стоимость последней правки: новых GPU dispatch, потоков, DDA, текстур и буферов — 0. CPU инициирует до одного чтения на каждый требуемый чанк до ожидания и затем собирает те же payload в один batch. Фактическое terrain-окно может потребовать больше чанков, чем старое окно вокруг игрока; это необходимые данные, не бесплатная оптимизация. Пиковая CPU-стоимость подготовки полного batch не доказана меньшей. Полный static solve не удешевлён этой правкой.

Полный static solve при смене lighting region не устранён. Отсутствие фриза и квадратов, GPU-время и визуальная корректность этих правок требуют отдельно разрешённого воспроизведения перехода окна в Unity Play Mode; локальные проверки этого не доказывают.

## Что доказано последним dump

Источник: `LightingDumps/2026-09-15_17-04-04/`.

| Метрика | Значение |
| - | - |
| staticSolveCount | 17 |
| staticDependencyMaskSolveCount | 5 |
| cascadeFullEntries | 69,861,376 |
| estimatedCascadeRayWorkUnits | 1,164,902,400 |
| estimatedCascadeDispatchThreads | 4,259,840 |
| terrainFullPopulateCount | 1 |
| terrainRebuildCount | 9 |
| terrainChunkLoadCount | 54 |
| terrainDirtyPatchCount | 5 |
| streamingPlanKind | ScrollTerrain |
| streaming delta | (0, -32) |
| atlasScrollCount | 0 |
| atlasReusedEntries | 0 |
| cascadePartialEntries | 0 |

Счётчики смешивают интервалы: часть сбрасывается каждый кадр, часть накапливается. Поэтому эти данные доказывают наличие повторных полных static solve, но не связывают каждый solve с конкретным кадром движения.

DDA counters в этом dump равны нулю, потому что текущий binder выставляет `_LightingCountersEnabled = 0`. Нулевые DDA counters нельзя использовать как доказательство отсутствия DDA.

## Подтверждённый дорогой путь

Изменение чанка проходит так:

```text
MapRegionProcessor
  → WorldLayer.SetRegion
  → ChunkLoaded / RegionChanged
  → LightingEngine.InvalidateRegion
  → LightingRuntimeState.FieldDirty
  → LightingUpdateCoordinator
  → RecordMaterialField + GeometryCache
  → полный SolveCascade всех каскадов
  → ResolveDirect
  → Bounce / Composite
```

На конфигурации из dump один полный cascade solve оценивается примерно в 1.16 млрд ray work units. 17 накопленных solve дают большой объём работы даже при небольшом изменении геометрии.

## Что оказалось ошибочным

### Atlas scroll/reuse

В `LightingUpdateCoordinator` существовал отключённый путь:

- `ScrollRadianceAtlas` копирует старое перекрытие в scratch atlas;
- `RecordCascadeStrips` трассирует новые полосы;
- атласы меняются местами.

Включение этого пути вызвало видимую поломку статического света. Найден и исправлен дефект единиц: `regionDelta` в клетках мира делился на `ProbeSpacing` в текселях без учёта texels-per-cell. При масштабе 4 перенос был вчетверо меньше требуемого.

Этого исправления недостаточно для включения reuse: лучи сохранённых проб могут пересекать входящую/выходящую полосу, поэтому само перекрытие атласа не гарантирует неизменность transport inputs. Остальные места проверки:

- направление `regionDelta` при переносе oldProbe → newProbe;
- соответствие world region, probe spacing и atlas offsets;
- пересечение угловых полос при одновременном X/Y сдвиге;
- корректность порядка far-to-near после swap;
- публикация `RadianceAtlas` после swap;
- наличие stale данных в `StaticDirectTexture`, bounce и composite.

До отдельного golden-теста full solve против scroll solve ветка должна оставаться выключенной.

### Cooperative yield загрузчика

Уступка после подготовки четырёх чанков удалена: движение ожидает завершения загрузчика, поэтому этот yield добавлял обязательные ожидания Update даже для уже resident данных. Последовательный старт дисковых чтений также заменён стартом всех нужных чтений перед ожиданием.

Итоговый packet всё ещё отправляется одной batch-операцией, а `WorldLayer.SetRegion` обрабатывает его синхронно.

## Текущая гипотеза по фризу

Нужно разделить три участка, которые сейчас выглядят как один «streaming freeze»:

1. подготовка чанков и payload в `DummyMapStreamer`;
2. синхронный `WorldLayer.SetRegion` и обработка batch packet;
3. следующий `TerrainRenderer.LateUpdate`, где выполняются:
   - `TerrainCellCache.ScrollAndFill`;
   - `PrecalculateIncremental`;
   - flood fill;
   - `TerrainCellBuilder.ScrollAndBuildBand`;
   - texture/mesh upload;
   - lighting update.

Старый dump уже показывал пик порядка 57 ms на terrain mesh и 15 ms на terrain cache. Это сильнее похоже на синхронную перестройку terrain/данных, чем на повторный full terrain populate: `terrainFullPopulateCount = 1`, но `terrainRebuildCount = 9`.

## Почему видны квадраты

`TryGetCell` в старом `IsTerrainWindowResident` действительно проверял наличие целого chunk array в RAM, а не случайную выборку неполных данных. Дефект был в отсутствии инициирования холодных чтений, несовпадении запрошенных окон и замороженном presentation viewport. Проверка residency сама по себе также не гарантирует, что:

- весь фактически используемый диапазон уже содержит данные;
- кольцевой cache и mesh находятся в одной версии окна;
- lighting field и terrain mesh используют один и тот же world rect;
- старая текстура не публикуется на кадр после смены origin.

Нужно диагностировать и исправить атомарность окна:

```text
requested window
  → all required chunks resident
  → cache/mesh/material textures updated
  → lighting field updated
  → presentation publishes new window
```

Публикация нового origin до завершения всех зависимых ресурсов недопустима.

## Что должен сделать следующий агент

1. Не включать atlas scroll повторно без теста full-vs-scroll на packed atlas.
2. Добавить frame-local counters и reason для каждого static solve:
   - `regionChanged`;
   - `geometryChanged`;
   - `FieldDirty`;
   - `resourcesResized`;
   - `dependencyMask`;
   - `full/partial entries`.
3. Включить DDA counters только для diagnostic capture.
4. Раздельно измерить одним dump/trace:
   - `DummyMapStreamer`;
   - `WorldLayer.SetRegion`;
   - terrain cache;
   - precalculate;
   - flood fill;
   - mesh build;
   - texture upload;
   - lighting command build/execute.
5. Проверить, не вызывается ли `LightingEngine.UpdateLighting` после каждого batch или dirty patch.
6. Проверить resident-window contract по всему прямоугольнику, а не по sample cells.
7. Сделать golden-сценарии:
   - движение на один streaming quantum;
   - загрузка вертикальной полосы;
   - загрузка горизонтальной полосы;
   - одновременный X/Y сдвиг;
   - неполный/cancelled streaming request;
   - повторное движение до завершения предыдущей загрузки.
8. Сравнить `MaterialField`, `CellSolidMask`, `StaticDirect` и `FinalLightmap` до/после каждого перехода окна.

## Запрещённые «исправления»

- throttling освещения;
- solve раз в N кадров;
- пропуск обновления при движении;
- FPS cap/frame skipping;
- ослабление света или разрешения как маскировка;
- публикация незаполненного окна ради исчезновения фриза.

## Критерий готовности

Готово только когда одновременно выполнены условия:

- нет видимых квадратов на границе окна;
- движение не запускает серию полных static solve без изменения transport inputs;
- полный и частичный алгоритмы дают одинаковый packed atlas;
- найден и устранён конкретный CPU/GPU участок, вызывающий фриз;
- worst case массовой загрузки чанков не стал хуже.
