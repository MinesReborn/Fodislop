- тексты написать (без иишки)
- компонентно-солид mvp рефакторинг
- тонна блять синхронных (серийных) процессов и гонок определений
- [x] добавить сверху пимпочку которое показывает состояние загрузки ассетов и туда вынести и версию билда и фпс и пинг и т.п.
- [ ] движок цветокоррекции: оставшиеся дыры. Вывоз `.cube` — `.cdl` несёт slope/offset/power и насыщенность, но НЕ несёт кривую, то есть в DaVinci уедет половина вида; печатать LUT надо на GPU и читать обратно, повторять математику шейдера на CPU нельзя — это второй источник истины. Кривые как инструмент (RGB, hue-vs-hue, hue-vs-sat). Вторичные коррекции — ключи по оттенку, маски, окна: для стилизованной 2D-картинки дают мало, а сложности много, браться последними.
- [ ] Xcode GPU capture: блум 8×8 проти 16×16 (тредгрупи). Контекст: спроба 16×16 дала 146 fps проти 151 fps (6.85мс проти 6.62мс, +0.23мс ≈ шум). Відкочено на 8×8. Потрібен full Xcode (не CLT) + Development Build + Attach to Process + Capture GPU Frame → Shader Profiler: occupancy % і registers/thread у BloomPrefilter/Downsample/Upsample. Якщо occupancy падає на 16 — гіпотеза тиску регістрів доведена, питання закрите. Зняти капчу до/після, цифри сюди.
- [ ] откалибровать порог `PostProcessLook.Bloom.Threshold` (1.6 — относительный: во сколько раз пиксель ярче локального фона, не абсолютный). `Lens.GlintThreshold` в коде нет вообще; грязи на линзе, анаморфных лучей, дифракции и heat haze в пайплайне нет — `_BloomTex` ест только блум. Верное число видно только на кадре: смотреть глазами через F5, false color для отсечки. Расчётом не подбирать — именно так и появились прежние числа.
- [ ] реализовать Render Governor / Frame Budget Coordinator (кадрирование и разделение тяжелых задач рендера: terrain remesh, batch sprite rebuild, minimap/worldmap pixel sampling и UI painter во избежание микро-статтеров в одном кадре)
- [ ] Распараллелить сборку бендов террейна Jobs/Burst: бенды независимы по строкам, писать в NativeArray. НЕ трогать флудфилл (уже параллельный+инкрементальный) и скролл-reuse (выключен обоснованно). Сначала доказать striped-бенч на F1 frametime, потом мержить.

## Реестр огромных production C# файлов

Критерий: production-файл больше 500 строк должен быть разбит по ответственностям,
а не механически превращён в `partial`. Новые файлы больше 500 строк запрещены;
текущий конечный debt-list охраняется архитектурным линтером и сокращается до нуля.

- [ ] `World/Lighting/Core/LightingEngine.cs` (682, было 2015: coordinator/resources/scheduling/pipelines частично вынесены в солверы, но лимит 500 всё ещё превышен).
- [ ] `World/Persistence/WorldLayer.cs` (938, было 1081): format/index/cache/IO/compaction.
- [ ] `World/Terrain/Core/TerrainRenderer.cs` (1280, было 874 — ВЫРОС): lifecycle/coverage/mesh/material updates.
- [x] `AssetPipeline/Animation/GifAnimationDecoder.cs` (319, было 774): parser/LZW/compositing/output — распилили ниже лимита.
- [ ] `UI/Chat/GlobalChatUI.cs` (516, было 727): state/presenter/view binding.
- [ ] `Rendering/PostProcessing/Pipeline/PostProcessRenderPass.cs` (659, было 708): resources/scheduling/effect passes.
- [x] `Game/Entities/Robot.cs` (461, было 692): state/visual loading/presentation — распилили ниже лимита.

## Программа оздоровления клиента (6–9 месяцев)

Цель: воспроизводимые macOS ARM64 / Windows x64 релизы, отсутствие потери
локальных данных и управляемый lifecycle без скрытых фоновых операций.

### 3. Границы модулей

- [x] Оставить в `Kern.Contracts` только интерфейсы, DTO и value types.
- [x] Перенести `WorldLayer<T>` и файловый формат в `Kern.Persistence` assembly.
- [ ] Разбить `Kern.Runtime` на Core/Application/Infrastructure/Presentation.
- [ ] Закрыть implementation types через `internal`. Граф asmdef проверяется:
      `checkAssemblyGraph` в `scripts/check-architecture.js` ловит и кольца, и
      обращение к типу из сборки, на которую нет ссылки. Нашла две настоящие
      поломки сразу: `AnimatedSpriteData` и `IRuntimeAssetPaths` стояли в
      сигнатурах Contracts/AssetPipeline, а объявлены были в сборках, которые
      сами ссылаются на них. Рядом `checkNamespaceVisibility` — тип виден по
      ссылкам, но не по `using`: нашла ещё три файла (`AssetLoadingIndicator`,
      `PostProcessRenderPass`, `DisplayManager`). Перенос типа между сборками
      почти всегда меняет и пространство имён — потребителей проверять.
- [ ] Экрана загрузки бутстрапа (`BootstrapLoadingScreen.uxml/.uss`) **в макете нет**.
      Его вид не из чего выводить: сейчас он собран из общих токенов по аналогии
      с оверлеями, но источника истины у него не существует. Либо экран рисуется
      в `visual/kern-ui-lab`, либо признаётся служебным и не участвует в
      сверке с макетом. До решения любые правки его вида — догадка.
- [ ] Контракт `data-fit` не покрывает main game: заголовок предмета в инспекторе
      (`clip`), описание предмета (`clamp`) и слоты корзины (`atomic`) объявлены
      в макете, но в USS игры не перенесены — правки туда не разрешены. Перенос
      делается вместе с разбором main game; проверку это не роняет, потому что
      `check-fit.py` сверяет только пары из `component-map.json`.
- [ ] Переполнение текста проверяется вручную. Детектор макета — кнопка в
      дев-панели, замер геометрии (`docs/design-text-overflow.md`) — прогон в
      браузере. Ни то ни другое не встроено в сборку: для этого нужен
      headless-драйвер, а он тянет первую node-зависимость в репозитории.
      Решение отложено сознательно — сначала стало видно, сколько там дефектов.
- [ ] Главное меню расходится с макетом **структурно**, а не значениями. Окно
      настроек в игре — две подписи в строке (`mm-item-title` + `mm-item-val`),
      тогда как в макете это живые органы управления: `styled-select`,
      слайдеры, переключатели и подсказка под каждой строкой (`settings-hint`).
      Браузер серверов в игре — список карточек (`mm-server-card`), в макете —
      таблица из четырёх колонок с индикатором пинга и строкой ввода IP
      (`servers-table`, `srv-search-row`). Это не правка числа в USS: нужна
      новая разметка и код. Решение — за человеком: макет либо переносится,
      либо признаётся опережающим, и тогда об этом надо сказать вслух.
- [ ] Три псевдоэлемента макета остаются невыразимыми: сетка звёзд
      (`.space-backdrop::after` — маска плюс фоновая сетка), слой поверхности
      планеты (`.fa-surface::after`) и ползунок переключателя настроек
      (`.switch-knob:before`, а самого переключателя в игре нет). Остальные шесть
      расшиты: бусина хроники, перекрестье прицела, ромб шага и чёрточка
      надзаголовка стали настоящими узлами в обеих сторонах. Заодно текст
      надзаголовка и шага маршрута уехал в дочерний узел — на самом элементе
      висел `data-i18n`, и система перезаписала бы содержимое вместе с
      добавленным узлом.
- [ ] Карта пар покрывает 126 классов игры из 159 в `MainMenu.uxml`. Непокрытое
      сверять не с чем: свойства там никем не проверяются. Отдельно опасны
      модификаторы — до этого захода они не сверялись вовсе, и первый же
      заведённый показал золотую активную вкладку настроек против бирюзовой
      в макете. `compare-components.py` теперь понимает составные значения
      (`mm-nav-tab--active` ↔ `fdn-settings-tab.active`) и раскрывает сокращения
      рамок, так что остальные модификаторы заводятся строкой в карте.
- [x] Неподдерживаемые CSS-фильтры удалены из USS. `filter`,
      `backdrop-filter` и `box-shadow` не входят в используемый UI Toolkit USS;
      из-за них правила меню импортировались с ошибками и интерфейс получал
      непредсказуемые стили. Сверка макета теперь явно считает эти эффекты
      непереносимыми; свечение при необходимости делается материалом,
      Painter2D или подготовленной текстурой.
- [ ] Рампа высот понадобится только после выбора поддерживаемого механизма
      теней. CSS-смещения и сигмы из web-макета нельзя печатать в USS как есть.
- [x] Стрелка миссии (`MissionArrowUI.cs`) невидима: элемент создаётся кодом с классом
      `mission-arrow`, под которым **нет ни одного правила** ни в одном листе, а инлайном
      задаются только позиция и поворот. Без размера и фона элемент нулевой — указатель
      на цель миссии не рисуется вообще. Файл в `UI/Overlays`, то есть main game.
- [x] `PlayerHUDView.cs:233` вешает `UILayoutTier.Attach` на `rootVisualElement`, а не на
      клонированное поддерево, как MainMenu/Gateway/Bootstrap/серверные окна. Тир там
      считается по всей панели, а не по экрану; правится вместе с разбором main game.

### 4. Декомпозиция god-object'ов

- [ ] Разделить `LightingEngine` на coordinator, resources, scheduling и pipelines.
- [x] Разделить `DummyConnection` на session, auth, simulation и responders.
  - [x] Вынести generation-based lifecycle/status в `DummyConnectionSession`.
  - [x] Вынести offline identity и token resolution в `DummyAuthSession`.
  - [x] Вынести mutable player state (position/direction/HP/toggles/basket/geology) в `DummyPlayerSimulationState`.
  - [x] Вынести `WorldLayer`, cell-configs, sent-chunk cache и single-flight gate в `DummyWorldSimulationState`.
  - [x] Вынести стартовый world/player/status/inventory snapshot и bot/ping/online loops в `DummyWorldStartupResponder`.
  - [x] Разнести packet responders из центрального `SendAsync`.
    - [x] Вынести state/history/local/global chat packets в `DummyChatResponder`.
    - [x] Вынести selection/use-item и inventory state в `DummyInventoryResponder`.
    - [x] Вынести routing daily bonus/teleport/clan/missions/test windows в `DummyWindowResponder`.
    - [x] Вынести move/rotate/click-path, cancellation и position snapshots в `DummyMovementResponder`.
    - [x] Вынести dig/suicide/geology/heal/build в `DummyGameplayActionResponder`.
    - [x] Вынести runtime asset responses в `DummyAssetResponder`.
- [x] Разделить config repository, migration и runtime settings.
  - [x] Вынести чтение, legacy-key normalization и durable atomic save/backup в `ClientConfigRepository`.
  - [x] Вынести последовательные schema migrations из `ClientConfigManager` в `ClientConfigMigration`.
  - [x] Вынести default construction и validation; оставить в runtime manager lifecycle, состояние и применение пользовательских настроек.
- [ ] Разделить крупные UI-классы на view binding, presenter и state.

### 5. Системная проверка релиза

- [x] Покрыть reconnect, pause/resume, disk failures и UI input PlayMode-тестами.
  - `ReconnectPlayModeTests`, `PauseResumePlayModeTests`, `DiskFailurePlayModeTests`, `UIInputPlayModeTests`; общая обвязка — `PlayModeHarness`.
- [x] Добавить GPU lifecycle integration tests для lighting.
  - `LightingGpuLifecyclePlayModeTests` (категория `GPU`).
- [x] Сделать dummy-сценарии детерминированными через virtual clock.
  - `IDummyClock`: `RealtimeDummyClock` в игре, `VirtualDummyClock` в тестах; `DummyClockContractTests` не пускает обход часов.
- [x] Добавить nightly soak: 50 переходов сцен, reconnect storm и streaming карты.
  - `SoakPlayModeTests` (категория `Soak`), задача `macos-soak` в CI; метрики пишутся в `persistentDataPath/Soak`.
- [x] Проверять clean install и сброс чужих форматов (конфиг/кэш/карта).
  - `InstallUpgradeTests`: чужая версия конфига → ResetToDefaults, чужая карта → пересоздание, чужой маркер кеша → перештамповка, всё на одной папке данных. Миграций нет.

Критерии завершения: обе production-сборки запускаются из чистого checkout;
50 циклов Menu/Game не оставляют задач, подписок и объектов; fault-injection не
теряет dirty chunks; p95 frame time и память не ухудшаются более чем на 5%.

## Графика: приведение арта к системе

Разбор показал, что рендер не является узким местом: каскадный свет
(`WorldLighting.compute`, 228 строк + `Lighting/*.hlsl` — ядра разъехались по
инклудам, было 885 в одном файле), стоковый URP-тонмап (AgX удалён, сейчас
Neutral / Neutral BT2390), dual-Kawase блум, MRT-поле материалов
и HDR уже дают больше, чем в них подаётся. Весь разрыв — в слое ассетов:
326 PNG без единого общего правила. Направление выбрано: сначала арт-библия,
затем перерисовка руками, затем линтер как храповик. Ничего из раздела не
начато — ждёт обсуждения с командой.

### Измерение и закрепление

- [ ] `visual/main-menu-mirror/tools/inventory-art.py`: карта всех 326 PNG
      (семейство, размер, кратность метрике, занятость холста, поля, мягкость
      края, оттенок/насыщенность/светлота, неиспользуемость). Печатает
      `docs/design-art-inventory.md` машинным файлом. Сейчас единственный способ
      узнать состояние арта — руками через sips, и потому никто его не знает.
- [ ] `scripts/check-art.js`: метрика семейства (Cells кратны 32, Items 42x42,
      Skills 73x73, Crystals единый размер), совпадение размеров внутри набора
      состояний одного контрола, существование ассетов, на которые ссылается
      код, поимённый список мёртвых PNG. Правила, требующие решения человека
      (палитра, контур, поля), не пишутся до арт-библии.
- [ ] Потолок долга по арту в духе `DEBT_BUDGET`: число ассетов вне метрики
      фиксируется числом; рост — нарушение, падение — тоже, иначе отвоёванное
      можно молча вернуть.
- [ ] Расширить `Assets/Scripts/Editor/Rendering/PixelArtTextureImportPolicy.cs` на
      `Assets/Textures` (сейчас покрыты только `Resources/Programmator` и
      `Resources/Skills`). В комментарии честно оговорить, что для этой ветки
      настройки импорта на экран не влияют: `BuildTextureStager` копирует файлы
      в StreamingAssets, а `TextureStorageManager.DecodeTexture` принудительно
      ставит sRGB/Point/Clamp. Политика нужна для единообразия редактора,
      а не для вида в игре.

### Арт-библия

- [ ] `docs/art-bible.md`: палитра, направление света, контур, метрика
      семейства, обработка края, уровень износа, подача (чёткий пиксель или
      живопись). Решение по каждой оси — за человеком; без библии перерисовка
      разъедется снова, что уже и произошло. Каждая решённая ось становится
      правилом в `check-art.js`.

### Расхождения с решением «тайл 32px — константа»

- [ ] Тайлы фильтруются `Point` (`TextureStorageManager.cs:81,118`,
      `TextureAtlas.cs:77`), но при `CameraFollow.DefaultOrthographicSize = 7`
      растягиваются в дробное число раз: 900p x2.01 (ровно), 1080p x2.41,
      1440p x3.21, 4K x4.82. Один исходный пиксель занимает на экране 2 или 3
      экранных в зависимости от места — ровная в текстуре линия идёт ступеньками
      неравной высоты. Побочно: на 900p картинка чище, чем на 1080p. Лечится
      подгонкой ortho под целый масштаб либо рендером мира в RT фиксированного
      размера; и то и другое — правка камеры/конвейера, не арта, поэтому едет
      отдельно. Решение отложено сознательно.
- [ ] Здания не привязываются к сетке: `Building.cs:141-145` создаёт спрайт с
      `pixelsPerUnit = CELL_SIZE = 32`, а ни одна из 7 текстур `Textures/Pack`
      не кратна 32 — Market 114x114 это 3.562 x 3.562 клетки, Resp 50x87 это
      1.562 x 2.719, Craft 50x47 это 1.562 x 1.469. Дополнение холста
      прозрачным до кратного 32 сместило бы рисунок относительно клетки, то есть
      правка не механическая: нужна перерисовка художником. До неё линтер по
      `Pack/` не ругается.

### Разнобой внутри семейств

- [ ] Четыре состояния одного чекбокса имеют четыре разных размера:
      `unchecked` 256x255, `checked` 259x253, `deselected` 259x260,
      `selected` 260x260. При переключении контрол дёргается на пиксель-два.
      Человек, рисующий набор состояний, такого не делает — это подпись
      поштучной генерации.
- [ ] `Resources/Programmator`: 166 иконок, **31 различный размер** — 15x15,
      30x30, 13x13, 27x28, 29x28, 16x22, 19x19, 11x11 и около двадцати
      одиночных вплоть до 27x7. Иконки показываются в интерфейсе рядом друг с
      другом. Приведение к сетке — перерисовка, а не масштабирование.
- [ ] `Textures/Items`: холст у всех 51 одинаковый (42x42), но занятость кадра
      от 0.05 до 0.61 — предмет может быть в двенадцать раз мельче соседнего.
      Мягкость края от 0.00 до 1.79: часть иконок с жёстким контуром, часть с
      широким ореолом. Цвет занимает 9 секторов оттенка из 12, разброс
      насыщенности 0.35 — общей палитры нет.
- [ ] `Textures/Crystals/green.png` 25x24 при 24x24 у остальных пяти.

### Мёртвое и ложное

- [x] `Robot.cs` больше не содержит `ProjectRuntimeContracts.ResourcePaths.RobotPreviewTexture`
      и запасных путей `"Textures/bot"` / `"bot"`; отсутствующий ассет больше не загружается.
      `RobotAssetLoader.CreateEditorPreviewSprite()` намеренно использует
      `Texture2D.whiteTexture` только для editor preview.
- [x] `Assets/Textures/skills.png` (746x447) и `Assets/Textures/programmator.png`
      (512x512) подтверждены как легаси-листы без ссылок из кода и GUID-ссылок.
      Файлы и `.meta` удалены из рабочего дерева.
- [x] Все 65 PNG и 2 GIF в `Textures/Cells` имеют ровно одну запись спрайта.
      `spriteMode` нормализован с `2` (Multiple) на `1` (Single); рантайм эти
      метаданные не читает.
- [x] `RuntimeTextureFactory.CreateRgba32ArrayNoMip` отсутствует; `Texture2DArray`
      в project code не создаётся, поэтому незаконченный array-заход закрыт.
- [x] `Textures/Cells/117.png` остаётся единственным файлом с `filterMode: 0` и
      `wrapU/V/W: 0`. Это importer-only расхождение, на экран оно не влияет.

## Постпроцесс: что осталось за человеком

- [x] Подписи тумблеров на вкладке эффектов переиспользуют старые ключи и теперь
      обещают меньше, чем включают. `settings.effects.anamorphic_beams` подписывает
      тумблер, который включает ещё грязь на линзе, дифракцию и блики;
      `settings.effects.glow_dust` — вместе с тепловым искажением;
      `settings.effects.phosphor_pattern` — вместе с дизерингом;
      `settings.effects.phosphor_afterglow` — вместе со стабилизацией света.
      Ключи не переименованы и новые не заведены намеренно: тексты пишет человек.
      Подписи обновлены по смыслу групп.
      [2026-09] УСТАРЕЛО: ключей `anamorphic_beams`, `glow_dust`, `phosphor_*`
      больше нет ни в коде, ни в локализации — удалены как мёртвые. Тумблеры
      1:1: bloom / vignette / eigengrau / motion_blur.
- [ ] Числа в `PostProcessLook` — отправная точка, а не решение. Подобраны
      консервативно, чтобы стек стало видно; ни одно не проверено на экране.
      Крутить только этот файл: он единственный источник вида.
- [ ] Тонмап впервые заработает на ярких местах мира. До сих пор всё выше
      единицы срезалось в белый, и как выглядит каскадный свет после кривой AgX
      никто не видел — ни одна из правок этого захода в Unity не проверялась.
      [2026-09] Частично закрыто: тонмаппит стоковый URP (Neutral SDR /
      Neutral BT2390 HDR через HDROutputReconciler), кастомная кривая в bypass.
      Глазами всё равно не проверено.
