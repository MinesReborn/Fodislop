# AGENTS.md - Инструкции для работы с клиентом Fodinae

## 1. Обзор проекта

**Fodinae** — 2D-клиент игры на Unity (URP) с тайловым рендерингом мира и сетевым обменом данными.

- **Движок**: Unity 6 (`6000.2.10f1`).
- **Рендер**: Universal Render Pipeline 2D (`com.unity.render-pipelines.universal` 17.2.0).
- **Сетевое взаимодействие**: Пакеты `darkar25.fodinae.*` (data, networking, connection) — подключены как Git-зависимости из [MinesReborn/MinesServerNetworking](https://github.com/MinesReborn/MinesServerNetworking).
- **Интерфейс**: UI Toolkit.
- **Асинхронность**: `UniTask` (vendored в `Assets/Plugins/UniTask/`).

### 1.1 Архитектурная концепция клиенто-серверного взаимодействия

Архитектура Fodinae построена на четком разделении тяжелого рендеринга и легкого сетевого состояния:

1. **Примитивы рендеринга на клиенте**: Клиент содержит готовые системы отрисовки и воспроизведения (тайловый мир `SingleMeshTerrainRenderer`, сущности роботов `RobotManager`, предметы на земле `PackManager`, эффекты `SFXEffectManager`).
2. **Легковесный сетевой поток данных**: Сервер передает клиенту только чистые координаты и идентификаторы состояний (где стоят роботы, какие предметные паки лежат, какие ячейки попадают в радиус прорисовки, какие звуки вызваны).
3. **Ленивая однократная загрузка тяжелых ассетов (On-Demand Fetching)**: При первом появлении ранее неизвестного объекта (новый блок, скин робота, иконка пака, аудио-банк) клиент запрашивает бинарные ассеты (текстуры, спрайты, `.bank`) с CDN/сервера один раз.
4. **Кэширование и локальный рендер**: Все полученные ассеты сохраняются в стойком дисковом кэше `PersistentAssetCache` (с ETag/MD5 валидацией) и ОЗУ (`CellTextureCache`, `AssetCache`). В дальнейшем клиент выполняет тяжелый рендеринг исключительно из локального кэша без повторных сетевых запросов.

## 2. Структура проекта

```text
Assets/
  Editor/              # BuildScript.cs, CsProjFix.cs, ExportSprites.cs — тулинг сборки и экспорта
  Plugins/             # Vendored DLL
    UniTask/           # Vendored UniTask (полный пакет)
    SharpCompress, ZstdSharp, K4os.Compression.LZ4  # Сжатие
    NetCoreServer      # Сеть
    Genumerics, ExtendedNumerics.BigDecimal          # Математика
    SmartFormat, NCalc, Parlot, ZString              # UI/шаблоны
    System.*, IsExternalInit                         # Системные заглушки
  Scenes/              # SampleScene.unity, TextureStorageTestScene.unity
  Scripts/
    # Ассет-пайплайн
    AssetPipeline/
      ClientAssetLoader.cs        # Загрузка ассетов с сервера/локально
      PersistentAssetCache.cs     # Стойкий кэш ассетов (ETag, MD5)
      ETagCalculator.cs           # MD5-хэш для ETag-валидации
      DynamicImage.cs             # Компонент Image, загружающий спрайты с сервера
      AssetCache.cs               # RAM-кэш декодированных ассетов
      AnimatedSpriteData.cs       # Данные анимированного спрайта

    # Аудио
    Audio/
      Backend/
        AudioSystem.cs            # Синглтон-контроллер (Play, PlayAttached, PlaySnapshot, SetGlobalParameter, SetBusVolume)
        FmodAudioBackend.cs       # Низкоуровневый FMOD API: loadBankFile, AttachInstanceToGameObject, шины, снэпшоты
      Core/
        AudioLayer.cs             # Параметры звука: шина (SFXDefault/UIDefault/etc), volume, pitch, IsSpatial
        AudioPlaybackHandle.cs    # Обёртка над FMOD.Studio.EventInstance (Stop, SetPosition, SetVolume, SetParameter)
      Spatial/
        AudioSpatial.cs           # Компонент: нативная привязка 3D-звука к трансформу
        AudioZone.cs              # Триггерная зона: запускает FMOD Snapshots и выставляет Global Parameters
        WorldAudioController.cs   # Управление фоновым аудио мира

    # Системная инфраструктура
    Core/
      SingletonMonoBehaviour.cs   # Базовый класс синглтонов MonoBehaviour
      GameConstants.cs            # Игровые константы

    # Эффекты (Effekseer)
    Effekseer/
      RuntimeEffekseerLoader.cs   # Загрузчик эффектов Effekseer в рантайме

    # Игровые сущности и менеджеры
    Game/
      Pack.cs                     # Игровой предмет (пак на земле)
      Robot.cs                    # Робот (NPC/игрок в мире)
      RobotHeadlight.cs           # Фары/освещение робота
      ServerAudioEvent.cs         # Серверный аудио-эффект (SFXPacket → FMOD + VFX)
      VFXPool.cs                  # Пул визуальных эффектов
      Managers/
        GameManager.cs            # Точка входа: инициализация сцены и подсистем
        MapManager.cs             # Жизненный цикл мира (WorldInit, MapRegion), конфиги ячеек
        MapStorage.cs             # Хранилище карты (чанки 32×32), кэш в .mapb
        RobotManager.cs           # Управление роботами (спавн, движение, деспавн)
        PackManager.cs            # Управление предметами на земле
        ServerAudioEventManager.cs # Принимает SFXPacket → запускает FMOD + VFX
        ItemRegistry.cs           # Реестр предметов: имена, иконки
        ServerConfig.cs           # Конфигурация с сервера (digCooldown и т.д.)

    # GIF-декодер
    MgGifDecoder/
      MgGifDecoder.cs             # GIF-декодер (MG.GIF)

    # Сеть
    Networking/
      NetworkService.cs           # Синглтон: подписка/отписка пакетов Subscribe<T>
      PacketHandler.cs            # Диспетчер пакетов → менеджеры
      Connection/
        ConnectionManager.cs      # Синглтон: управление подключением (TCP, авторизация, реконнект)
        Client/
          DummyConnection.cs      # Заглушка для офлайн-режима
          TextureStorageManager.cs # Менеджер хранения текстур на сервере

    # Игрок
    Player/
      PlayerMovementController.cs   # Ввод (New Input System), клиентская валидация по Passable
      PlayerInteractionController.cs # Обработка кликов и клавиш (копка, использование)
      CameraFollow.cs               # Следование камеры за игроком

    # UI
    UI/
      Builders/
        PacketUIBuilderFactory.cs # Фабрика UI-билдеров пакетов
        PacketUIBuilderBase.cs    # Базовый класс билдера
        PacketUIBuilder.cs        # Базовый интерфейс билдера
        CanvasPacketBuilder.cs, PanelPacketBuilder.cs, GridPacketBuilder.cs,
        TextPacketBuilder.cs, TextBoxPacketBuilder.cs, ImagePacketBuilder.cs,
        SelectablePacketBuilder.cs, SliderPacketBuilder.cs,
        IntDropdownPacketBuilder.cs, StringDropdownPacketBuilder.cs,
        ScrollViewerPacketBuilder.cs, LinePacketBuilder.cs, DockPanelPacketBuilder.cs
      Controls/
        Selectable.cs             # Кастомный Selectable (UI Toolkit)
        RegexTextField.cs         # Текстовое поле с валидацией по regex
        UILine.cs                 # Кастомный VisualElement для линий
        ChatInputBlinker.cs       # Анимация курсора в поле чата
      Binding/
        WindowBinding.cs          # SmartFormat-привязка данных для окон GUI
        LogiCalcFormatter.cs      # Форматтер вычислений для SmartFormat
      Programmator/
        ProgrammatorData.cs          # Данные программатора
        ProgrammatorGrid.cs          # Сетка программатора
        ProgrammatorTextureRegistry.cs # Реестр текстур программатора
        RadialMenu.cs                # Радиальное меню программатора
      ChatInput.cs                # Управление фокусом чата (блокировка управления)
      ClickContextResolver.cs     # Разрешение clickContext-путей в VisualElement
      FloatingChatBubble.cs       # Всплывающее сообщение над персонажем
      FloatingChatManager.cs      # Менеджер всплывающих чат-сообщений
      FPSCounter.cs               # Счётчик FPS
      GlobalChatUI.cs             # Глобальный чат (ввод, история)
      InventoryModel.cs           # Модель данных инвентаря
      InventoryUI.cs              # Окно инвентаря (сетка 9×6 + хотбар)
      ItemData.cs                 # Данные предмета (тип, количество)
      LocalChatPopup.cs           # Popup локального чата
      MainMenu.cs                 # Главное меню
      MinimapController.cs        # Контроллер миникарты
      ModalWindowHandler.cs       # Обработчик модальных окон
      PauseMenu.cs                # Меню паузы (настройки, выход)
      PlayerHUD.cs                # HUD: HP, энергия, баффы, кнопки
      PlayerStatsModel.cs         # Модель статистики игрока
      StyleApplicator.cs          # Применение стилей к UI-элементам
      WorldMapController.cs       # Полноэкранная карта мира (управление)
      WorldMapRenderer.cs         # Рендеринг карты мира (текстура из MapStorage)

    # Мир и рендеринг
    World/
      SingleMeshTerrainRenderer.cs  # Один меш на весь террейн, 7 UV-каналов
      CoordinateUtils.cs            # Прямая конвертация координат 1:1 (сервер↔Unity)
      FodinaeGizmos.cs              # Визуальные Gizmos отладки мира
      WorldTextureManager.cs        # Загрузка тайлов в TextureAtlas
      TextureAtlas.cs               # Упаковка текстур в атлас
      SurfaceRenderer.cs            # Transit + Perspective поверхности (доп. меши)
      CellTextureCache.cs           # ConcurrentDictionary-кэш текстур ячеек
      AtlasCoordinate.cs            # Координаты ячейки в текстурном атласе
      AnimationContainerDecoder.cs  # Декодинг PNG/GIF/WebP в спрайты
      WorldBackgroundSetup.cs       # Настройка фона сцены
      WorldLayer.cs                 # Дисковый стриминг чанков (RLE + LRU кэш)
      TileMaskConverter.cs          # Битмаски авто-тайлинга
      SceneSetup.cs                 # Инициализация сцены при старте
      StandaloneWorldInitializer.cs # Тестовый мир без сервера
      RenderingConstants.cs         # Константы рендеринга
      Extensions/
        WorldLayerTextureExtensions.cs # Расширения WorldLayer для текстур

  Settings/            # URP и Renderer2D конфиги
  Textures/            # Cells/, Clan/, Crystals/, Exported/, Items/,
                       #   Pack/, Skin/, Tail/, UI/, VFX/ — тайлы, UI, экипировка
  UI Toolkit/          # PanelSettings.asset, темы (.tss)
```

## 3. Архитектура систем

### 3.1 Сетевой слой (Networking)

- **NetworkService**: Синглтон. Подписка: `Subscribe<T>` / `Unsubscribe<T>`.
- **PacketHandler**: Получает пакеты и делегирует менеджерам (`MapManager`, `RobotManager`, `PackManager`, `SFXEffectManager` и др.).
- **ConnectionManager**: Синглтон. Управление TCP-подключением, авторизация (`LoginRequestPacket`), реконнект. Использует `MinesServer.Networking.Connection.Client` из Git-пакета.
- **TextureStorageManager**: Загрузка и кэширование текстур с сервера (аватары, клановые значки и т.д.).
- **Пакетный UI**: Динамическая сборка UI из `OpenWindowPacket` через `PacketUIBuilderFactory`.

### 3.2 Мир и Рендеринг (World & Rendering)

- **MapManager**: Жизненный цикл мира (`WorldInitPacket`, `MapRegionPacket`), конфигурации ячеек, тайл-группы.
- **MapStorage**: Хранилище данных карты (чанки 32x32). Кэширует в `persistentDataPath/*.mapb`. Требует `InitWorld` перед рендерингом.
- **WorldLayer\<T\>**: Дисковый стриминг с LRU-кэшем в RAM. RLE-сжатие. Append-only запись с компактификацией.
- **WorldTextureManager**: Загружает тайл-текстуры из файловой системы (не Resources/Addressables), упаковывает в `TextureAtlas`.
- **SingleMeshTerrainRenderer**: Один меш на весь видимый террейн. 7 UV-каналов (атлас, тайлинг, анимация, тени, рельеф). `Sorting Order = -1000`.
- **SurfaceRenderer**: Дополнительные меши для Transit (переходы между слоями) и Perspective (перспективные блоки). Два материала, отдельные Sorting Orders.
- **CellTextureCache**: ConcurrentDictionary-кэш текстур ячеек для быстрой загрузки из файловой системы. Хранит `Texture2D` по `CellType`.
- **AtlasCoordinate**: Структура координат ячейки в текстурном атласе.
- **AnimationContainerDecoder**: Декодирование PNG/GIF/WebP-файлов в массивы спрайтов для анимированных тайлов и эффектов.
- **Координаты**: Левый верхний угол карты — это серверные координаты `(0, 0)`. Ось X растет вправо, ось Y растет вниз (вглубь шахты). Все пространственные конвертации централизованы в утилите `CoordinateUtils`.

### 3.3 Игрок и Управление

- **PlayerMovementController**: Ввод через New Input System. Единственный источник истины позиционирования игрока — свойство `Position` (`Vector2Int` в серверных координатах Top-Left `0:0`). Устаревшие псевдонимы `ClientPosition` и `ServerPosition` полностью устранены. Клиентская валидация по `Passable` + серверная через `MovePacket`.
- **PlayerInteractionController**: Обработка кликов и клавиш (копка, использование предметов). Отправляет `DigRequestPacket`, `ItemUsePacket` и т.д.
- **CameraFollow**: Следование камеры за игроком.

### 3.4 Аудио-домен (Audio)

Аудио-домен построен полностью идиоматично под **FMOD Studio C++ Engine**.

**Архитектура:**
```
Audio/
  Core/                         # Ядро и типы
    AudioBusType.cs             # Enum шин: Master, SFX, Music, Voice, Ambience, UI
    AudioLayer.cs               # Параметры звука: шина (SFXDefault/UIDefault/etc), volume, pitch, IsSpatial
    AudioPlaybackHandle.cs      # Прямая обёртка над FMOD.Studio.EventInstance (Stop, SetPosition, SetVolume, SetParameter)
  Backend/                      # FMOD Studio Бэкенд
    FmodAudioBackend.cs         # Низкоуровневый FMOD API: loadBankFile, AttachInstanceToGameObject, Snapshots, Global Parameters
    AudioSystem.cs              # Синглтон (SingletonMonoBehaviour): API Play, PlayAttached, PlaySnapshot, SetGlobalParameter, SetBusVolume
  Spatial/
    AudioSpatial.cs             # Компонент на GameObject: нативная привязка 3D-звука к трансформу (AttachInstanceToGameObject)
    AudioZone.cs                # Триггерная зона: запускает FMOD Snapshots (snapshot:/...) и выставляет Global Parameters
```

**FMOD интеграция (MMO & Zero-RAM Waste):**
1. Банки `.bank` скачиваются с игрового CDN через `ClientAssetLoader.GetAssetPathAsync` (ETag-кеширование на диск)
2. Загрузка в FMOD выполняется через `loadBankFile` с дискового пути (без напрасного дублирования банков в RAM)
3. **Нативное 3D-позиционирование**: `FMODUnity.RuntimeManager.AttachInstanceToGameObject()` транслирует координаты и повороты объектов на C++ стороне FMOD без C#-поллинга в кадрах.
4. Динамические фиче-банки подгружаются на лету через `AudioSystem.Instance.EnsureBankLoadedAsync("Zone_Name.bank")` и выгружаются через `UnloadBank()`
5. **FMOD Snapshots**: `AudioZone` активирует нативные Snapshots микшера (настройки акустики/фильтров), не затирая пользовательские настройки громкости.
6. FMOD проект: `FodinaeAudio/FodinaeAudio.fspro` (в корне репозитория)
7. 6 шин FMOD мапятся на `AudioBusType` (bus:/, bus:/sfx, bus:/music, bus:/voice, bus:/ambience, bus:/ui).

**Примеры использования:**
```csharp
// Проигрывание звука UI (2D)
AudioSystem.Instance.Play2D("ui_click");

// Проигрывание 3D-звука с нативной привязкой к GameObject
AudioSystem.Instance.PlayAttached("robot_engine", gameObject);

// Запуск FMOD Snapshot (например пещера)
var snapshot = AudioSystem.Instance.PlaySnapshot("snapshot:/Cave_Ambient");

// Установка глобального параметра FMOD (глубина)
AudioSystem.Instance.SetGlobalParameter("Depth", 450f);

// Настройка громкости шины SFX
AudioSystem.Instance.SetBusVolume(AudioBusType.SFX, 0.8f);
```

- **ServerAudioEventManager**: Принимает `SFXPacket` от сервера, запускает 3D-звук в FMOD через `AudioSystem.Instance.PlayAt()` и создаёт `ServerAudioEvent` для рендеринга спрайтов/Effekseer.

### 3.5 Ассеты и кэширование (Asset Loading)

- **ClientAssetLoader**: Загрузка ассетов с сервера (GET-запросы) или локально из файловой системы.
- **PersistentAssetCache**: Стойкий кэш в `persistentDataPath`. Хранит ETag + MD5 для валидации, пропускает повторную загрузку неизменных файлов.
- **AssetCache**: Вспомогательный кэш ассетов в оперативной памяти (RAM).
- **ETagCalculator**: MD5-хэш данных для ETag-заголовка.
- **DynamicImage**: `MonoBehaviour` с `UnityEngine.UI.Image`, загружающий спрайт с сервера по URL. Работает через `ClientAssetLoader` + `PersistentAssetCache`.
- **Пайплайн загрузки ассетов (Локальный CDN)**:
  1. Запрос ассета (`GetTextureAsync`, `GetAudioAsync` и т.д.) поступает в RAM-кэш `AssetCache`. При промахе опрашивается дисковый кэш `PersistentAssetCache`.
  2. Если ассет есть локально на диске, отправляется HTTP-запрос с ETag. При ответе `304 Not Modified` ассет считывается с диска. Если файл обновился или отсутствует, скачивается новый поток байт.
  3. Параллельные запросы к одному файлу объединяются (coalescing) через `TaskCompletionSource`, предотвращая дублирование сетевого трафика.

### 3.6 UI-системы

- **Пакетный UI** (см. 3.1): Динамическая сборка окон из `OpenWindowPacket` — фабрика `PacketUIBuilderFactory` и несколько типовых билдеров (Canvas, Panel, Grid, Text, Slider, Dropdown, ScrollView, Line, DockPanel...).
- **Binding**: `WindowBinding` привязывает данные через `SmartFormat`. Сканирует VisualElement-дерево, ищет именованные поля ввода (источники) и Label с SmartFormat-шаблонами (потребители), пересчитывает при любом изменении.
- **PauseMenu**: Меню паузы с настройкой всех 6 шин громкости FMOD (`Master`, `SFX`, `Music`, `Voice`, `Ambience`, `UI`), масштабом UI, графикой и выбором разрешения. Автоматически выставляет `PauseMenu.IsMenuOpen`, блокируя ввод движения и кликов (`PacketHandler.IsInputBlocked`).
- **Инвентарь**: `InventoryUI` (сетка 9×6 + хотбар 9 ячеек), `InventoryModel` (данные), `ItemData` (тип/количество).
- **HUD**: `PlayerHUD` — HP, энергия, баффы, кнопки (включая авто-копку и программатор).
- **Карта**: `WorldMapController` (управление, переключение режима), `WorldMapRenderer` (рендеринг текстуры из `MapStorage`).
- **Чат**: `GlobalChatUI` (история + ввод), `LocalChatPopup`, `FloatingChatManager`/`FloatingChatBubble` (всплывающие сообщения над персонажами), `ChatInput` (блокировка управления при фокусе).
- **Прочее**: `PauseMenu` (пауза, настройки громкости/полноэкранного режима, выход), `FPSCounter`, `MinimapController`, `ModalWindowHandler`, `StyleApplicator`, `ClickContextResolver`.

### 3.7 Программатор (Programmator)

- **ProgrammatorGrid**: Графическая сетка для визуального программирования алгоритмов поведения робота.
- **ProgrammatorData**: Модель данных и структура алгоритма робота.
- **RadialMenu**: Радиальное круговое меню выбора команд для быстрого размещения на сетке программатора.

## 4. Стандарты разработки

### Unity & YAML

- **Прямое редактирование**: Предпочтительно редактирование `.prefab` и `.unity` как текстовых YAML-файлов.
- **Мета-файлы**: У каждого ассета ДОЛЖЕН быть `.meta` файл. При перемещении/удалении через CLI — обрабатывать оба.
- **GUID**: Не ломайте связи между ассетами, сохраняйте GUID.

### C# и Код

- **Синглтоны**: Паттерн `Instance` + `DontDestroyOnLoad` для менеджеров.
- **События**: `Action` для связи между компонентами (`OnWorldInitialized`, `OnWorldDataLoaded`).
- **UniTask**: Для асинхронных операций (загрузка текстур, сетевые запросы).

### Стандарты именования (Casing Standards)

В проекте строго соблюдаются следующие разграничения регистра (Casing):

1. **Unity Файлы и C# Код (`PascalCase`)**:
   - Классы, структуры, интерфейсы, перечисления: `WorldTextureManager`, `CellType`.
   - Публичные методы, свойства, события: `GetCellTextureCoordinate()`, `ActiveVoiceCount`.
   - Константы: `MaxLifetime`.
   - Директории Unity внутри `Assets/`: `Assets/Scripts/`, `Assets/Textures/Cells/`, `Assets/Audio/`.
   - Файлы ассетов: `SampleScene.unity`, `PlayerHUD.uxml`, `PanelSettings.asset`.
   - Приватные/защищенные поля: `_camelCase` (`private float _volume;`).
   - Параметры и локальные переменные: `camelCase` (`int x, int y`).

2. **Сетевые ресурсы, CDN и FMOD (`lowercase` / `snake_case`)**:
   - Имена FMOD событий: `event:/sfx_bz`, `event:/dig_rock`.
   - Сетевые тэги окон и контексты: `"teleport"`, `"open_missions"`, `"join_clan"`.
   - CDN URL-пути: `/cells/1.png`, `/clan/4.png` (Linux CDN серверы регистрозависимы, поэтому сетевые URL строчные).

### Документация (`docs/`)

- **Формат**: Только HTML. Никакого Markdown, никаких генераторов (Jekyll, Hugo, Docusaurus).
- **Стили**: Инлайн `<style>` в каждом файле. Минимальные, короткие, читаемые. Без внешних CSS-файлов, без фреймворков.
- **Шаблон**: См. `docs/rendering.html` как эталон. Тёмная тема, `system-ui`, `max-width: 720px`, `code` с моноширинным шрифтом.
- **Правило**: Каждый документ должен быть автономным — открыл файл в браузере, всё читается без зависимостей.

## 5. Критические нюансы (Gotchas)

1. **Инициализация MapStorage**: Рендеринг не начнется, пока `MapStorage.IsReady` не станет `true`. Это происходит после `WorldInitPacket`.
2. **Инверсия Y**: Самый частый источник багов. Всегда проверяйте систему координат входящих данных.
3. **Текстуры**: Пайплайн кастомный — файловая система, не Resources. Билд должен копировать `Textures/` вручную.
4. **UI Toolkit**: Темы привязаны к GUID. Missing Reference в `PanelSettings` = пустой UI.
5. **Сортировка**: `SingleMeshTerrainRenderer` рисуется на `Sorting Order = -1000` (под спрайтами роботов).

## 6. Рабочий процесс (Workflow)

- **Открытие**: Unity Hub → папка проекта. Основная сцена: `Assets/Scenes/SampleScene.unity`.
- **Сборка**: Использовать `BuildScript.BuildOSX` из `Assets/Editor/`. Стандартный Build Settings не копирует текстуры.
- **Автономный режим**: `StandaloneWorldInitializer` создаст тестовый мир без сервера.
- **Сцена содержит**: `[WorldTextureManager]`, `SingleMeshTerrainRenderer`, `UIDocument`, `Main Camera`, `Global Light 2D`, `SceneSetup`, `MapManager`.

## 7. Линтинг C# (обязательно для ИИ)

Проект использует 4 Roslyn-анализатора без перекрытий:

| Анализатор | Префикс | Зона ответственности |
|---|---|---|
| `StyleCop.Analyzers` | `SA` | Стиль, форматирование, именование |
| `Microsoft.CodeAnalysis.NetAnalyzers` | `CA` | Корректность, надёжность, безопасность |
| `Roslynator.Analyzers` | `RCS` | Упрощение кода, dead code |
| `Microsoft.Unity.Analyzers` | `UNT` | Unity-специфика (Update, Invoke, Message) |

### Обязательный хук после генерации C# кода

```bash
dotnet build Assembly-CSharp.csproj --no-incremental 2>&1
```

Вывод содержит предупреждения вида:
```
MapManager.cs(42,13): warning SA1300: ...
WorldLayer.cs(88,5): warning CA1031: ...
```

**Правило**: все предупреждения с префиксами `SA`, `CA`, `RCS`, `UNT` — нарушения линтера. Исправляй их до финального ответа пользователю.

### Запрет обхода Git Hooks

- **СТРОГО ЗАПРЕЩЕНО** использовать `--no-verify`, пропускать хуки проверки или насильно отменять их при коммитах.
- Если пре-коммит хук или сборка зависает или завершается ошибкой, необходимо дождаться завершения, разобраться с причиной (песочница, линтеры, `dotnet build` ошибки) и исправить проблему, а не обходить хуки.

### Настройка

- `Directory.Build.props` — подключает анализаторы через NuGet во все `.csproj`
- `.stylecop.json` — отключает нерелевантные для Unity правила (XML-доки, file headers)
- `.editorconfig` — severity для каждого правила (`none` / `warning` / `error`)
