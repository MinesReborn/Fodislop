- тексты написать (без иишки)
- компонентно-солид mvp рефакторинг
- тонна блять синхронных (серийных) процессов и гонок определений
- [ ] добавить пимпочку справа снизу которое показывает состояние загрузки ассетов и туда вынести и версию билда и фпс и пинг и т.п.
- [x] Восстановить полноценный локальный чат в активном `GlobalChatUI`: отдельный local-tab, открытие клавишей T, общий lifecycle/input blocking, отправка `SendLocalChatMessagePacket`, приём `LocalMessageReceived` и отдельная история канала. Старый неподключённый `LocalChatPopup` не возвращать.
- физику вырезать, матрицы коллизий??? чо это?? тоже вырезать.... слои юнити сделать
- [ ] реализовать Render Governor / Frame Budget Coordinator (кадрирование и разделение тяжелых задач рендера: terrain remesh, batch sprite rebuild, minimap/worldmap pixel sampling и UI painter во избежание микро-статтеров в одном кадре)

## Реестр огромных production C# файлов

Критерий: production-файл больше 500 строк должен быть разбит по ответственностям,
а не механически превращён в `partial`. Новые файлы больше 500 строк запрещены;
текущий конечный debt-list охраняется архитектурным линтером и сокращается до нуля.

- [ ] `World/Lighting/Core/LightingEngine.cs` (2015): coordinator/resources/scheduling/pipelines.
- [ ] `World/Persistence/WorldLayer.cs` (1081): format/index/cache/IO/compaction.
- [x] `Networking/Connection/Client/DummyConnection.cs` (393, было 1154): разделён на session/auth/player+world simulation/movement/gameplay+chat+inventory+window+asset responders.
- [ ] `World/Terrain/Core/TerrainRenderer.cs` (874): lifecycle/coverage/mesh/material updates.
- [ ] `UI/Overlays/InGameDebugOverlay.cs` (837): state/sampling/presenter/view binding.
- [ ] `AssetPipeline/Animation/GifAnimationDecoder.cs` (774): parser/LZW/compositing/output.
- [ ] `UI/Chat/GlobalChatUI.cs` (727): state/presenter/view binding.
- [ ] `Rendering/PostProcessing/PostProcessRenderPass.cs` (708): resources/scheduling/effect passes.
- [ ] `Game/Entities/Robot.cs` (692): state/visual loading/presentation.
- [ ] `UI/Menu/Core/MainMenu.cs` (685): state/presenter/view binding/transition flow.
- [ ] `World/Textures/WorldTextureManager.cs` (661): loading/cache/atlas orchestration.
- [ ] `AssetPipeline/Loading/ClientAssetLoader.cs` (656): requests/cache/batching/decoding.
- [ ] `UI/Programmator/Model/ProgrammatorData.cs` (650): model/serialization/commands.
- [ ] `UI/Gateway/GatewayController.cs` (646): auth state/presenter/view binding.
- [ ] `UI/Map/WorldMapRenderer.cs` (638): sampling/texture updates/presentation.
- [ ] `World/Rendering/BackgroundFloodFill.cs` (627): traversal/cache/output.
- [ ] `UI/Programmator/Grid/ProgrammatorClipboardController.cs` (627): clipboard/selection/commands.
- [ ] `AssetPipeline/Cache/AssetCacheEntry.cs` (624): entry state/download/decode/lifecycle.
- [ ] `UI/HUD/Player/View/PlayerHUDView.cs` (621): binding/presenter/subviews.
- [ ] `Game/Audio/ServerAudioEvent.cs` (599): lifecycle/audio/VFX/loading.
- [ ] `UI/Settings/PauseMenu.cs` (596): state/presenter/view binding.
- [ ] `World/Terrain/Mesh/TerrainMeshBuilder.cs` (587): topology/attributes/output.
- [ ] `World/Lighting/Core/LightingResourceManager.cs` (577): allocation/ownership/destruction.
- [ ] `Player/Controllers/PlayerMovementController.cs` (567): state/input/network movement.
- [ ] `World/Textures/TextureAtlas.cs` (561): packing/storage/upload.
- [x] `World/Lighting/Config/LightingConfigHolder.cs` (491, было 557): ClientConfig mapping/normalization вынесены в `LightingRuntimeConfigMapper`.
- [x] `UI/Menu/Scenery/MenuSceneryController.cs` (498, было 554): viewport projection и occlusion вынесены в `MenuSceneryProjection`.
- [x] `World/Terrain/Cache/TerrainCellCache.cs` (469, было 547): сдвиг coverage-массивов вынесен в `TerrainCacheArrayScroller`.
- [x] `Game/Rendering/WorldEntityBatchRenderer.cs` (463, было 547): texture atlas packing/upload/ownership вынесены в `WorldEntityTextureAtlas`.
- [x] `Rendering/PostProcessing/PostProcessController.cs` (493, было 545): profile validation/component override setup вынесены в `PostProcessDefaults`.
- [x] `UI/Map/MinimapController.cs` (498, было 541): UXML binding, координаты и видимость вынесены в `MinimapView`.
- [x] `World/Rendering/SurfaceRenderer.cs` (454, было 518): mesh/component/lighting lifecycle вынесен в `SurfaceMeshUtilities`.

## Программа оздоровления клиента (6–9 месяцев)

Цель: воспроизводимые macOS ARM64 / Windows x64 релизы, отсутствие потери
локальных данных и управляемый lifecycle без скрытых фоновых операций.

### 1. Честная сборка и зависимости

- [x] Заменить фиктивный Linux `dotnet build` gate на Unity EditMode/PlayMode jobs.
- [x] Добавить обязательные macOS ARM64 и Windows x64 IL2CPP builds.
- [x] Закрепить Git UPM-зависимости конкретными commit SHA.
- [x] Валидировать Build Settings без автоматического изменения авторских данных.
- [ ] Подключить лицензированные self-hosted runners с меткой `fodinae-unity`.
- [ ] Зафиксировать performance baseline и бюджеты регрессий.

### 2. Async lifecycle и сохранность мира

- [x] Не удалять dirty chunk из RAM до успешного завершения eviction-save.
- [x] Ввести единый `IAsyncLifetime` и владельца фоновых задач.
- [x] Запретить голый `.Forget()` вне task supervisor.
  - [x] Перевести batch-loop ассетов, FMOD/feature-банки, мировые/packet-текстуры, post-connect, переходы сцен, HUD/chat delays и surface setup под supervisor.
  - [x] Удалить неиспользуемый `LocalChatPopup` (нет C#-потребителей и сериализованных GUID-ссылок).
  - [x] Перевести загрузку визуалов `Robot`/`Building` под supervisor и объединить связанные robot-assets через structured `WhenAll`.
  - [x] Перевести динамическую загрузку `ServerAudioEvent` VFX под supervisor.
  - [x] Перевести offline connect/disconnect, world init, packet responses, pathing и dummy simulation loops под supervisor.
  - [x] Запретить новые `.Forget()` во всём production-коде; старый долг зафиксировать конечным allowlist.
  - [x] Перевести оставшиеся allowlist-владельцы; единственное исключение — внутренний запуск самого supervisor.
- [x] Ожидать остановку сети и durable flush перед выгрузкой `MainGame`.
- [x] Добавить `FlushAsync`, `DisposeAsync` и сериализацию persistence-операций.
- [x] Разделить состояния чтения чанка: available/loading/missing/failed.
- [x] Версионировать world-layer format и мигрировать v0→v1 атомарно с backup.
- [x] Версионировать config/cache formats и выполнять атомарные миграции с backup.
  - [x] `client_config.json`: schema v15, последовательные миграции, durable atomic replace и `.vN.backup`.
  - [x] `AssetCache`: schema v1 marker, metadata-only v0 backup и atomic marker commit/recovery без копирования или повторной загрузки payload-файлов.

### 3. Границы модулей

- [x] Оставить в `Fodinae.Contracts` только интерфейсы, DTO и value types.
- [x] Перенести `WorldLayer<T>` и файловый формат в `Fodinae.Persistence` assembly.
- [ ] Разбить `Fodinae.Runtime` на Core/Application/Infrastructure/Presentation.
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
      в `visual/fodinae-ui-lab`, либо признаётся служебным и не участвует в
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
- [ ] Отказ дисплея в HDR не доходит до игрока. `DisplayManager.SetHDREnabled`
      теперь возвращает исход и откатывает настройку, когда дисплей HDR-способен,
      но не переключается на лету, — однако галка в настройках
      (`UI/Settings/PauseMenuDisplayTabBuilder.cs`) исход по-прежнему выбрасывает.
      Правильное поведение: при `RejectedUnsupported` галку гасить как
      неприменимую, при `RejectedNotSwitchable` показывать причину («переключите
      HDR в настройках ОС»). Файл в main game — правки туда не разрешены.
- [ ] Оверлей отладки не показывает, состоялось ли кодирование HDR. URP включает
      его в `FinalBlitPass` только при `outputsToHDR && overlayUITexture.IsValid()`,
      то есть когда собрана отдельная текстура оверлейного UI. Если её нет, сцена
      уходит в HDR-swapchain без преобразования и paper white — картинка
      неправильная, но не чёрная, и заметить трудно. Лечится одной строкой:
      печатать `cameraData.rendersOverlayUI` рядом с `available/active/gamut`.
      `UI/Overlays/InGameDebugOverlay.cs` — main game.
- [x] Неподдерживаемые CSS-фильтры удалены из USS. `filter`,
      `backdrop-filter` и `box-shadow` не входят в используемый UI Toolkit USS;
      из-за них правила меню импортировались с ошибками и интерфейс получал
      непредсказуемые стили. Сверка макета теперь явно считает эти эффекты
      непереносимыми; свечение при необходимости делается материалом,
      Painter2D или подготовленной текстурой.
- [ ] Рампа высот понадобится только после выбора поддерживаемого механизма
      теней. CSS-смещения и сигмы из web-макета нельзя печатать в USS как есть.
- [ ] Стрелка миссии (`MissionArrowUI.cs`) невидима: элемент создаётся кодом с классом
      `mission-arrow`, под которым **нет ни одного правила** ни в одном листе, а инлайном
      задаются только позиция и поворот. Без размера и фона элемент нулевой — указатель
      на цель миссии не рисуется вообще. Файл в `UI/Overlays`, то есть main game.
- [ ] `PlayerHUDView.cs:233` вешает `UILayoutTier.Attach` на `rootVisualElement`, а не на
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

- [ ] Покрыть reconnect, pause/resume, disk failures и UI input PlayMode-тестами.
- [ ] Добавить GPU lifecycle integration tests для lighting.
- [ ] Сделать dummy-сценарии детерминированными через virtual clock.
- [ ] Добавить nightly soak: 50 переходов сцен, reconnect storm и streaming карты.
- [ ] Проверять миграцию двух предыдущих форматов и clean/upgrade install.

Критерии завершения: обе production-сборки запускаются из чистого checkout;
50 циклов Menu/Game не оставляют задач, подписок и объектов; fault-injection не
теряет dirty chunks; p95 frame time и память не ухудшаются более чем на 5%.

## Графика: приведение арта к системе

Разбор показал, что рендер не является узким местом: каскадный свет
(`WorldLighting.compute`, 885 строк), AgX, dual-Kawase блум, MRT-поле материалов
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
- [ ] Расширить `Assets/Editor/PixelArtTextureImportPolicy.cs` на
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

- [ ] `Robot.cs:572` грузит `ProjectRuntimeContracts.ResourcePaths.RobotPreviewTexture`
      = `"Textures/bot"` с запасным путём `"bot"`. **Ни того, ни другого ассета
      в проекте нет**, папки `Assets/Resources/Textures` не существует — превью
      робота всегда сваливается в `Texture2D.whiteTexture`, то есть белый
      прямоугольник. Либо ассет добавляется, либо путь удаляется вместе с
      запасным.
- [ ] `Assets/Textures/skills.png` (746x447) и `Assets/Textures/programmator.png`
      (512x512) — легаси-листы, на которые не ссылается ни один файл кода. Оба
      к тому же единственные в проекте с `isReadable: 1`, то есть держат копию
      в памяти CPU без причины.
- [ ] Все 65 файлов `Textures/Cells` объявлены `spriteMode: 2` (Multiple), но в
      каждом ровно одна запись спрайта: нарезка объявлена и не существует.
      Настройка мертва вдвойне — рантайм метаданные этой ветки игнорирует.
- [ ] `RuntimeTextureFactory.CreateRgba32ArrayNoMip`
      (`AssetPipeline/Loading/RuntimeTextureFactory.cs:84`) не вызывается
      ниоткуда: `Texture2DArray` в проекте не создаётся никогда. Либо мёртвый
      код, либо незаконченный заход на массив тайлов вместо атласа.
- [ ] `Textures/Cells/117.png` — единственный из 65 с `filterMode: 0` и
      `wrapU/V/W: 0`. **На экран не влияет**: метаданные `Assets/Textures` в
      рантайме не читаются. Записано, чтобы линтер не выдавал это за дефект вида
      и чтобы расхождение не «чинили» повторно.

## Постпроцесс: что осталось за человеком

- [ ] Подписи тумблеров на вкладке эффектов переиспользуют старые ключи и теперь
      обещают меньше, чем включают. `settings.effects.anamorphic_beams` подписывает
      тумблер, который включает ещё грязь на линзе, дифракцию и блики;
      `settings.effects.glow_dust` — вместе с тепловым искажением;
      `settings.effects.phosphor_pattern` — вместе с дизерингом;
      `settings.effects.phosphor_afterglow` — вместе со стабилизацией света.
      Ключи не переименованы и новые не заведены намеренно: тексты пишет человек.
      Нужны четыре подписи по смыслу групп, дальше механическая замена ключей.
- [ ] Числа в `PostProcessLook` — отправная точка, а не решение. Подобраны
      консервативно, чтобы стек стало видно; ни одно не проверено на экране.
      Крутить только этот файл: он единственный источник вида.
- [ ] `_emissionScale` в `ProjectDefaults.asset` опустить с 8 до **3** — один шаг
      в Inspector, текстом `.asset` править нельзя (§62). Замер по скриншоту от
      03.09.2026: кадр глобально не пересвечен (медиана 82/255, клиппинг 0.05 %,
      p1 = 11/255), но ядра руды теряют цвет — медиана красной руды (152, 89, 91)
      при p99 (239, 237, 200). Белого в альбедо нет: у 65 листов `Cells`
      медианная доля почти-белых пикселей 0.00 %. Причина в том, что 8 — это
      верхняя граница собственного ползунка «сила эмиссии», а земля в кадре
      лежит около 0.10 линейных: эмиссия идёт вплоть до восьмидесяти раз ярче
      фона, и поканальная кривая AgX сводит все три канала к верхушке. На экране
      насыщенность ядра 0.10 при 8, 0.16 при 4, 0.18 при 3, 0.22 при 2.
      Тонмап тут ни при чём: объект в восемьдесят раз ярче окружения и должен
      быть белым. Выбрано 3, а не 2, потому что `_EmissionScale` умножает и
      собственную яркость руды, и её вклад в марш лучей
      (`WorldLighting.compute:343`): двойка срезала бы свет от руды вчетверо.
      Если после правки мир станет темнее нужного, компенсировать амбиентом или
      `_dynamicLightIntensity`, но не возвращать эмиссию.
- [ ] Гамма дисплея применяется не на той стороне кривой. `_Gamma` в
      `PostProcess.compute` возводит цвет в степень `2.2/_Gamma` **до** тонмапа,
      то есть над сцен-линейными значениями, где показатель степени не является
      гаммой ничего: это нелинейный сдвиг экспозиции, который вдобавок кормит
      кривую AgX другими числами. Калибровочная гамма по определению относится к
      display-encoded сигналу, поэтому её место — показатель в возврате
      `ToneMapAgX`: вместо `pow(color, 2.2)` писать `pow(color, _Gamma)`, и тогда
      ползунок ровно и означает «гамма моего дисплея не 2.2». На значении по
      умолчанию разницы нет — блок гаммы при 2.2 пропускается, — поэтому правка
      не срочная, но она меняет вид на сдвинутом ползунке и требует взгляда.
- [ ] `HdrPaperWhiteScale` тоже умножается до тонмапа, а белая точка кривой уже
      выведена из paper white. Одна и та же величина участвует дважды, и не
      доказано, что так задумано. Проверять на HDR-дисплее.
- [ ] Тонмап впервые заработает на ярких местах мира. До сих пор всё выше
      единицы срезалось в белый, и как выглядит каскадный свет после кривой AgX
      никто не видел — ни одна из правок этого захода в Unity не проверялась.
