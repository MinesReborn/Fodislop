# Kern.FrameHarness

Первый исполняемый слой единого terrain/lighting harness: анализ сохранённых кадров
и before/after, а не синтетический рендерер. Требует .NET 10, Unity не запускает.

## Команды

Из корня репозитория:

```sh
dotnet test tools/Kern.FrameHarness.Tests
dotnet run --project tools/Kern.FrameHarness -- validate capture.json --json
dotnet run --project tools/Kern.FrameHarness -- compare before.json after.json \
  --cpu-p95-ms 0.2 --cpu-p99-ms 0.4 --cpu-max-ms 1 \
  --gpu-p95-ms 0.2 --gpu-p99-ms 0.4 --gpu-max-ms 1 --json
```

Последний `--json` необязателен; без него вывод читаемый. Числа в примере —
пример синтаксиса, **не принятые бюджеты проекта**. Все шесть допусков задаются
явно в миллисекундах: абсолютная допустимая разность candidate − baseline.
CPU-допуски применяются отдельно к каждому CPU-маркеру и к общему интервалу
кадра; GPU-допуски — только к GPU. Интервал кадра не называется CPU/GPU time.
Вложенные CPU-маркеры не суммируются.

Exit codes: `0` — все применимые проверки выбранного scope прошли; `1` —
нарушение/регрессия/несовместимые входы; `2` — ошибка чтения/JSON; `3` —
неполные доказательства; `64` — неверная команда. Один `validate` всегда
оставляет performance = INCOMPLETE: без baseline и бюджетов сравнения нет.
Известный FAIL имеет приоритет над INCOMPLETE. Успешное сравнение времени
не отменяет провал инварианта: CLI всё равно возвращает 1.

## Реализованный scope

Первый этап WorldRenderGrid добавляет renderer-owned контракт покрытия: строго
32 пикселя на клетку, округление наружу и один защитный пиксель по каждой стороне.
WorldRenderGridCamera вычисляет view/projection и viewport-to-world UV, сохраняя
исходную Camera для UI и ввода. Это пока контракт и проверка матриц: Renderer2D
ещё не использует его для мирового color/depth, GRID-32 production coverage остаётся
INCOMPLETE. Layout-тесты добавлены в существующий проект тестов харнесса; они не
являются замером игры или визуальным oracle.

Каждый JSON capture теперь имеет соседний `*.render-work.json` с наблюдениями
выполненных Kern scene/display RenderGraph passes: реальные width/height,
dispatch/draw count, логические пиксели, GPU threads, количество созданных
color targets, их payload bytes и CPU-время записи команд. Для fused kernel
группа из 64 threads покрывает 16×16 пикселей: пиксели и threads не смешиваются.
GPU-время в этом файле — null; отдельные GPU recorder windows остаются в текстовом
отчёте. Bytes не означают пиковую память устройства: импортированные ресурсы,
pooling, aliasing и драйвер не включены. Незапущенный/устаревший проход — null,
а не нулевая работа. Объект наблюдений принадлежит экземпляру render pass;
в production instrumentation нет покадровых managed allocations. Этот sidecar
пока не участвует в CLI comparison и не расширяет scope schema v1.

Все captures и sidecars одного benchmark run сохраняются под
`Logs/Diagnostics/Performance/frame_run_<stamp>/`. Каталог использует штатный
DiagnosticArtifactPaths.CreateDirectory и считается одной retention-записью:
экспорт поздних сценариев больше не удаляет ранние файлы того же прогона.
ProfilerRecorder использует WrapAroundWhenCapacityReached; перед чтением все
recorders остановлены, CopyTo проверяется против capacity. Coroutine disposal
освобождает recorders и при исключении/прерывании.

Unity benchmark принимает `KERN_BENCHMARK_RESOLUTION=3420x2148` и проверяет
фактический camera target размер перед запуском сценариев. Без переменной остаётся
текущий размер. В batch-прогоне production camera рендерит в явный ARGBHalf
target нужного размера (depth 24); capture и основной отчёт используют camera
pixelWidth/pixelHeight. Target создаётся один раз вне окон измерения и
освобождается после сценариев. Это offscreen rendering workload, без
доказательства эквивалентности финальному выводу на дисплей. Capture сохраняет
графические настройки без сетевых токенов.
Соседний `*.timings.json` содержит уникальные FrameTiming timestamps и доступные
CPU/GPU значения; задержанные GPU результаты не приписываются произвольным
frameId. Это оконные наблюдения, а не полноценное покадровое GPU покрытие.
CommandBuffer ProfilerRecorder scopes не заявляются как доказанные GPU durations.
Текстовый отчёт показывает покрытие аллокаций и GPU timings для каждого сценария.
Postprocessing включён для явно зарегистрированной diagnostic offscreen camera;
остальные offscreen cameras сохраняют прежнюю политику. Окна mask 7/6/5/3/1/2/4/0/7
проверяют bloom/vignette/eigengrau при авторских intensities; отдельное окно
отключает весь URP postprocessing при нулевых custom effects. Frame stamps
проверяют, что необходимые scene/display production passes действительно записаны.
См. docs/architecture/POSTPROCESS_BENCHMARK.html.

Аллокации выводятся отдельно по steady/cold/reanchor: среднее B/frame,
p50/p95/p99/max в байтах и decimal MB/s для полной выборки. `gcAllocBytes`
берётся бенчмарком из собственного `GC Allocated In Frame` recorder за
завершённый кадр; недоступный counter записывается как null. Это общий счётчик
кадра, а не атрибуция террейну/свету и не доказательство причины низкого FPS.
Разница занятой managed heap в AllocationLedger — приблизительное наблюдение,
её нельзя подменять этим счётчиком. Изменение касается диагностики: стадии,
ресурсы и invalidation террейна/света не меняются. Счётчик живёт только в окне
замера и освобождается при завершении/dispose корутины; анализ выполняется
после capture и не добавляет покадровых объектов. Production-проверка нового
сбора данных требует запуска Unity benchmark.

- OPT-1 для S0 (неизменный мир), S1 (камера внутри подготовленного окна),
  S4 (изменяется только динамический свет). Это **счётчиковые проверки**,
  а не доказательство правильных пикселей или соответствия каждой позиции света.
- Нулевые terrain rebuild/full populate/dirty patch/chunk load/atlas scroll
  дельты; нулевые upload calls/bytes и lighting field/static rebuild.
- Для S0/S1/S4 дополнительно проверяются cell-data `Apply`/`CopyTexture`, если capture
  содержит согласованные cumulative endpoints: дельта вычисляется из соседних
  observation frame IDs одной texture generation и сверяется с экспортированной
  frame delta. `sourceFrameId` — stamp последней операции, а не observation endpoint.
  Generation transition, недоступная текстура или разрыв endpoint-ов дают
  INCOMPLETE. Старые captures без этих optional полей сохраняют прежний scope;
  отсутствие данных не считается нулём и не заявляется как проверенное покрытие.
- В S4 изменение source revision требует увеличения dynamic solve и trace.
  Само имя сценария не доказывает движение: S1/S4 требуют наблюдённых изменений.
- До проверки лишней работы сравниваются source observations соседних кадров:
  world/geometry/window/lighting-region/settings/resources/contributors,
  камера, dynamic source revision, readiness и отсутствие pending work.
  Потерянный source observation даёт INCOMPLETE, а не выдуманный «стабильный» кадр.
- Дельты cumulative counters идут через **все** кадры, включая cold/reanchor.
  Сброс требует нового поколения + reset flag. Дельта через сброс неизвестна;
  следующий кадр использует новую baseline. Уменьшение без сброса — FAIL.
- CPU, общий кадр и GPU сравниваются отдельно для steady/cold/reanchor,
  с одинаковым ненулевым числом samples каждой присутствующей категории.
  p50/p95/p99 — nearest-rank; max всегда сравнивается, в том числе для редкого
  события 1/600. Отсутствующие значения не превращаются в нули и не исключаются
  из performance-проверки ради PASS.
  Один устаревший producer stamp блокирует сравнение CPU-метрики целиком для
  её класса, а не сокращает выборку. Независимые frame/GPU-метрики продолжают
  сравниваться; уже доказанный FAIL не скрывается за INCOMPLETE.
- Версия schema/harness, строгий JSON, frame gaps/order, отрицательные counters,
  NaN/Infinity, пропущенные обязательные поля, неизвестные/дублированные JSON
  properties и неверные enums проверяются.

`coverageStatus` относится только к выбранному реализованному scope.
**OPT-2/3/4, полное OPT-5, GRID-32, причинность/ack/dispatch rectangles,
replay S2/S3/S5–S7 и визуальные oracle пока не реализованы.**
Cold/reanchor имеют timing statistics и performance budgets, но не готовые
правила правильности их пересчётов; coverage для них остаётся INCOMPLETE.
Визуальная проверка здесь не выполняется: для полноты capture необходимы
`visualCoverage: true` и ссылка `visualEvidence` на независимый production oracle.

## Capture v1

Контракт типов: [CaptureSchema.cs](CaptureSchema.cs).
Независимый полный fixture: [CaptureAnalyzerTests.cs](../Kern.FrameHarness.Tests/CaptureAnalyzerTests.cs).
Fixture помечен как искусственный: это вход для теста анализатора, не замер игры.

- `schemaVersion: 1`, `harnessVersion: "1"`, уникальный `captureId`,
  `capturedAtUtc` в UTC; manifest и непустой последовательный массив frames.
- `manifest`: scenarioId, capturePhase, workloadHash, build, runtime,
  observationEvidence, visualCoverage/visualEvidence и unavailableMetrics.
  workloadHash идентифицирует мир/seed/маршрут/расписание входов, а не имя теста.
  qualityProfile идентифицирует эффективные настройки Kern и активные стадии,
  не только Unity QualitySettings.
- `build.loadedArtifactId` идентифицирует **загруженные** артефакты.
  `artifactScope: "production-content"` разрешён только для полного набора
  загруженного кода, shaders/assets/config. MVIDs одних сборок имеют scope
  `managed-code-only` и не дают полноценный artifact PASS.
  Before/after обычно имеют разные ID кода; это не делает их несовместимыми.
  Capture не удостоверяет текущие файлы на диске автоматически.
- `counterBaseline`, `counterBaselineGeneration`, `inputBaseline` относятся
  к кадру непосредственно перед первым sample. Счётчики с cumulative-семантикой
  находятся только в cumulative; покадровые — в frameCounters.
  `baselineObservationFrameId`, `baselineProducerFrameId` и
  `baselineProducerLifecycleValid` проверяют привязку начальной точки.
- `frameId`, `counterGeneration`, `counterResetObserved` обязательны, чтобы
  пропуск поля не стал ложным нулём. `class`: steady/cold/reanchor или null,
  если событие не классифицировано. Не выводить класс из времени кадра.
- `producerFrameId` и `producerLifecycleValid` снимаются с producer, а не
  вычисляются из observation frame. Пропуск reset оставляет старую метку,
  Dispose делает её недействительной; отсутствие метки — неизвестное значение.
  Reset после Dispose выбрасывает ObjectDisposedException и не оживляет producer.
- Timings — миллисекунды; cameraX/Y — мировые клетки Unity Y-up; revisions —
  неотрицательные версии в пределах указанного world generation.
  `terrainWindowRevision` и `lightingRegionRevision` описывают содержимое/
  layout опубликованных окон, не frame sequence и не факт вызова presentation.
- `sourceChangeKind` — дополнительная аннотация; не заменяет наблюдения.
  Missing/unsupported metrics — null с объяснением в unavailableMetrics.

Capture producer отвечает за frame correlation, полноту identity и достоверность
observations. Анализатор обнаруживает несогласованный формат/счётчики, но не может
проверить честность произвольного написанного вручную capture.

Корреляция проверяется отдельно от готовности результата: номер кадра в точке
сброса телеметрии не доказывает успешный terrain commit, завершённый lighting tick
или публикацию GPU output. Счётчики с устаревшей/отсутствующей привязкой не могут
доказывать ни лишний пересчёт данного кадра, ни его отсутствие. Для cumulative
дельты нужны обе корректно привязанные точки, включая начальную baseline.
Ошибка корреляции одного кадра не должна скрывать нарушение, доказанное другим.

## Подключение к настоящему игровому бенчмарку

[FrameBenchmarkPlayModeTests.cs](../../Assets/Scripts/Tests/PlayMode/FrameBenchmarkPlayModeTests.cs)
поддерживает опциональный экспорт при `KERN_FRAME_CAPTURE=1` в окружении процесса,
в котором **отдельно, явно** запускается existing explicit test
`MainGame_FrameCostByScenario`. Этот README не даёт разрешения запускать Unity.

Бенчмарк дополнительно измеряет production terrain draw на максимальном зуме,
затем на минимальном зуме после максимального (проверка возврата presentation
mesh к размеру текущего viewport). Затем он снимает диагностическую 2×2
матрицу `terrain draw × lighting compute`: оба включены, отдельно отключён
каждый, отключены оба. После каждого перехода выдерживается полный warmup —
lighting bypass освобождает GPU-ресурсы, поэтому короткая пауза дала бы замер
переинициализации вперемешку со steady-state. Разности матрицы — атрибуция
взаимодействий в production-сцене, не аддитивная оценка стоимости draw/pass и
не доказательство FPS-улучшения. Текстовый отчёт выводит p50-разницы относительно
all-on и factorial interaction по среднему frame time `оба − только terrain −
только lighting + all-on`. Окна идут последовательно, не попарно, поэтому
interaction остаётся диагностикой с возможным временным дрейфом; p50-разницы
не трактуются как аддитивные. Исходный zoom и debug-флаги восстанавливаются
даже при ошибке теста. После всех измерительных окон сохраняются JSON-файлы через
DiagnosticArtifactPaths в `Logs/Diagnostics/Performance/` (Editor) либо
`persistentDataPath/Diagnostics/Performance/` (player), с обычной retention
политикой категории (20 записей). Запись объявляется через DiagnosticReport.
Экспорт JSON не находится внутри измерительного цикла.

Снимаются реальные IFrameTelemetry CPU timers, cumulative terrain/dynamic/scroll
counters и frame-local field/static counters. Coroutine читает предыдущий кадр
до следующего Terrain.LateUpdate reset; frameId = Time.frameCount − 1,
frameDurationMs = Time.unscaledDeltaTime × 1000. FrameTelemetry отдельно
фиксирует producerFrameId в существующей точке ResetFrameTimers; collector
экспортирует и эту метку, и её lifecycle validity. Равенство двух номеров
проверяется, а не предполагается. Реальное соответствие coroutine/render фаз
ещё требует production-проверки. Сбросы не «угадываются» по уменьшению счётчика.

В текстовый отчёт добавлены независимые GPU marker scopes уже существующих
production CommandBuffer стадий: Terrain material/AO field, Lighting material/AO,
geometry caches, cascade 0–3, dynamic radiance и composite. Для каждой печатается
среднее по реально полученным marker samples; отсутствующая метка выводится как
`NO GPU SAMPLES`, а не как ноль. Это оконная диагностика, не frame-correlated
статистика и не поле JSON capture/CLI comparison. Вложенные scopes намеренно не
суммируются. Отрисовка видимого Terrain через `MeshRenderer` пока не имеет
собственного GPU scope, поэтому её bypass-разность остаётся только грубой
контрфактической оценкой, не стоимостью шейдера.

Эти captures намеренно **INCOMPLETE**: нет детерминированного workload, полного
набора source revisions/readiness, классификации кадров, эффективного graphics
profile, aggregate mesh/atlas upload counts/bytes, GPU timing и visual oracle.
Cell-data texture Apply/CopyTexture counts и payload estimates now экспортируются
как отдельные optional observational metrics; CaptureAnalyzer проверяет их для
S0/S1/S4 при подтверждённых входах, поколении и соответствии observation IDs
номерам кадров. Отсутствующие данные остаются неизвестными. Сценарии имеют имена
observational/diagnostic, **не S0/S1/S4**. Bypass-варианты — диагностические
наблюдения, а не приёмочный before/after. MVIDs не отождествляются с source tree.

Для Apply считает calls и estimated payload как allocated texture width × height ×
format bytes-per-pixel: full uploads считают target texture, staged uploads —
полный staging slab, включая неиспользованное место последнего slab. Каждый
CopyTexture считает прямоугольник копии × bytes-per-pixel. Это API payload
estimates, не GPU bandwidth. Per-call instrumentation читает texture/rect
dimensions, format и `Time.frameCount`; `ResetFrameTimers` делает одну constant-size
snapshot/delta операцию. Новых GPU resources нет. Cumulative snapshot содержит
generation, availability и last-upload source frame; frame delta отдельна,
действительна только между adjacent valid observation frames одной generation.
Reset-start snapshots помечены `Time.frameCount - 1`, поскольку относятся к уже
завершившимся upload calls предыдущего кадра. Measured CPU upload duration fields
остались numeric; only uncovered aggregate upload counts/bytes are null.

Старый замер до upload telemetry: 105/105 тестов и 96-byte managed sample на
standalone .NET 10 (84 байта до producer stamps); это historical baseline, не
текущий размер sample. Проверка 116 тестов относится к предыдущему этапу. Current managed
sample layout и instrumentation cost в Unity не измерялись. JSON formatting и IO
создаются после всех окон; standalone проверки не устанавливают стоимость
наблюдения в production и не дают performance claim.

## Проверка и ограничения

`dotnet test tools/Kern.FrameHarness.Tests/Kern.FrameHarness.Tests.csproj --no-restore`
— 156/156 (2026-09-29).
Standalone NUnit fixtures проверяют анализатор, JSON round-trip и настоящий CLI
процесс с кодами возврата. Source-linked production FrameTelemetry проверяется
на initial/reset/skipped-reset/dispose/repeated-dispose/reset-after-dispose.
Source-linked upload tests verify generation/reset/dispose, invalid-call atomicity,
allocated-slab and rectangle byte estimates, adjacent/missing/stale frame endpoints,
and strict CaptureJson round-trip through the production capture-field builders;
Time и ProfilerRecorder заменены узкими тестовыми заглушками. Это не проверка
Unity runtime, profiler API или шейдеров.
`LightingInvalidationSourceTests` компилирует настоящий DynamicLightManager,
coordinator, runtime state, setter и frame executor; границы Unity/GPU заменены
spies. Проверяет отсечение, capacity, точное движение, цвет/интенсивность,
вход/выход/удаление, приоритет полного composite и сохранение partial-пути.
Fault injection воспроизводит прежние лишние отправки команд. Это проверка
управляющего C#-пути, не shader output и не измерение FPS.
Unity producer assembly compile, PlayMode exporter execution, scene frames, and
render were not run; those checks remain pending under the repository's Unity
authority boundary. The strict standalone round-trip covers the shared production
capture-field builders and schema, not the complete Unity exporter/runtime path.

Изменения не добавляют runtime callback, solver, ресурс GPU, иной scheduler или
новый путь invalidation. Владельцы: CLI invocation (DTO/report), существующая
benchmark coroutine (sample arrays/export), существующий singleton FrameTelemetry
(producer stamp, lifetime GameLifetimeScope). IFrameTelemetry не расширялся:
новые IFrameTelemetryProducerStamp and terrain upload telemetry receiver/source
are optional capabilities on existing telemetry object/terrain texture owner,
not separate DI registrations. Render stages and GRID-32 were not changed.

Остаются обязательными отдельный явно запрошенный PlayMode-прогон указанного
benchmark с экспортом и проверка реальной фазовой корреляции. Performance,
пиксели, поведение GRID-32 и исправление FPS этой реализацией не доказаны.
