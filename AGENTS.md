# AGENTS.md - Инструкции для работы с клиентом Fodinae

## 1. Обзор проекта

**Fodinae** — 2D-клиент игры на Unity (URP) с тайловым рендерингом мира и сетевым обменом данными.

- **Движок**: Unity 6 (`6000.2.10f1`).
- **Рендер**: Universal Render Pipeline 2D (`com.unity.render-pipelines.universal` 17.2.0).
- **Сетевое взаимодействие**: Пакеты `darkar25.fodinae.*` (data, networking, connection) — подключены как Git-зависимости из [MinesReborn/MinesServerNetworking](https://github.com/MinesReborn/MinesServerNetworking).
- **Интерфейс**: UI Toolkit.
- **Асинхронность**: `UniTask` (vendored в `Assets/Plugins/UniTask/`).

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
    # Корневые скрипты (утилиты и инфраструктура)
    ClientAssetLoader.cs          # Загрузка ассетов с сервера/локально
    PersistentAssetCache.cs       # Стойкий кэш ассетов (ETag, MD5)
    ETagCalculator.cs             # MD5-хэш для ETag-валидации
    DynamicImage.cs               # Компонент Image, загружающий спрайты с сервера
    Dock.cs                       # Enum Dock (Left, Top, Right, Bottom)
    GameConstants.cs              # Константы мира и UI
    MainMenu.cs                   # Главное меню
    PacketUIBuilder.cs            # Сборка UI из серверных пакетов
    TileMaskConverter.cs          # Битмаски авто-тайлинга
    UILine.cs                     # Кастомный VisualElement для линий
    WorldLayer.cs                 # Дисковый стриминг чанков (RLE + LRU кэш)

    # Аудио
    Audio/
      AudioManager.cs             # Синглтон: фоновый звук, SFX, громкость
      SFXPool.cs                  # Объектный пул для звуков и эффектов
      SoundEffectInstance.cs      # Один звуковой эффект (тайл, копка и т.д.)
      WavUtility.cs               # Чтение/конвертация WAV

    # Эффекты (Effekseer)
    Effekseer/
      RuntimeEffekseerLoader.cs   # Загрузчик эффектов Effekseer в рантайме

    # Игровые сущности и менеджеры
    Game/
      Pack.cs                     # Игровой предмет
      Robot.cs                    # Робот (NPC/игрок в мире)
      RobotHeadlight.cs           # Фары/освещение робота
      SFXEffectInstance.cs        # Экземпляр SFX-эффекта от сервера
      Managers/
        MapManager.cs             # Жизненный цикл мира (WorldInit, MapRegion), конфиги ячеек
        MapStorage.cs             # Хранилище карты (чанки 32×32), кэш в .mapb
        RobotManager.cs           # Управление роботами (спавн, движение, деспавн)
        PackManager.cs            # Управление предметами на земле
        SFXEffectManager.cs       # Серверные SFX-эффекты (SFXPacket → спавн)
        ItemRegistry.cs           # Реестр предметов: имена, иконки
        ServerConfig.cs           # Конфигурация с сервера (digCooldown и т.д.)

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
      PlayerMovementController.cs # Ввод (New Input System), клиентская валидация по Passable
      PlayerInteractionController.cs # Обработка кликов и клавиш (копка, использование)
      CameraFollow.cs             # Следование камеры за игроком

    # UI
    UI/
      Builders/                   # PacketUIBuilderFactory + типовые билдеры
        PacketUIBuilderFactory.cs # Фабрика UI-билдеров пакетов
        PacketUIBuilderBase.cs    # Базовый класс билдера
        CanvasPacketBuilder.cs, PanelPacketBuilder.cs, GridPacketBuilder.cs,
        TextPacketBuilder.cs, TextBoxPacketBuilder.cs, ImagePacketBuilder.cs,
        ButtonPacketBuilder.cs (SelectablePacketBuilder),
        SliderPacketBuilder.cs, IntDropdownPacketBuilder.cs,
        StringDropdownPacketBuilder.cs, ScrollViewerPacketBuilder.cs,
        LinePacketBuilder.cs, DockPanelPacketBuilder.cs
      Controls/
        Selectable.cs             # Кастомный Selectable (UI Toolkit)
        RegexTextField.cs         # Текстовое поле с валидацией по regex
      Binding/
        WindowBinding.cs          # SmartFormat-привязка данных для окон GUI
        LogiCalcFormatter.cs      # Форматтер вычислений для SmartFormat
      Programmator/               # Система программатора
        ProgrammatorData.cs       # Данные программатора
        ProgrammatorGrid.cs       # Сетка программатора
        RadialMenu.cs             # Радиальное меню программатора
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
      MinimapController.cs        # Контроллер миникарты
      ModalWindowHandler.cs       # Обработчик модальных окон
      PauseMenu.cs                # Меню паузы (настройки, выход)
      PlayerHUD.cs                # HUD: HP, энергия, баффы, кнопки
      PlayerStatsModel.cs         # Модель статистики игрока
      StyleApplicator.cs          # Применение стилей к UI-элементам
      WorldMapController.cs       # Полноэкранная карта мира (управление)
      WorldMapRenderer.cs         # Рендеринг карты мира (текстура из MapStorage)

    # Системная инфраструктура
    Core/
      SingletonMonoBehaviour.cs   # Базовый класс синглтонов MonoBehaviour

    # Мир и рендеринг
    World/
      SingleMeshTerrainRenderer.cs  # Один меш на весь террейн, 7 UV-каналов
      CoordinateUtils.cs            # Прямая конвертация координат 1:1 (сервер↔Unity)
      FodinaeGizmos.cs             # Визуальные Gizmos отладки мира
      WorldTextureManager.cs        # Загрузка тайлов в TextureAtlas
      TextureAtlas.cs               # Упаковка текстур в атлас
      SurfaceRenderer.cs            # Transit + Perspective поверхности (доп. меши)
      CellTextureCache.cs           # ConcurrentDictionary-кэш текстур ячеек
      AtlasCoordinate.cs            # Координаты ячейки в текстурном атласе
      AnimationContainerDecoder.cs  # Декодинг PNG/GIF/WebP в спрайты
      WorldBackgroundSetup.cs       # Настройка фона сцены
      SceneSetup.cs                 # Инициализация сцены при старте
      StandaloneWorldInitializer.cs # Тестовый мир без сервера
      RenderingConstants.cs         # Константы рендеринга
      Extensions/
        WorldLayerTextureExtensions.cs # Расширения WorldLayer для текстур

    # GIF-декодер
    MgGifDecoder/
      MgGifDecoder.cs             # GIF-декодер (MG.GIF)

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

Новый аудио-домен построен как самостоятельная система с абстракцией бэкенда под FMOD.

**Архитектура:**
```
Audio/
  Clips/                        # Локальные WAV-сэмплы для разработки
    Sfx/                        # Звуковые эффекты (bz.wav, hurt.wav, death.wav, destroy.wav)
    Music/                      # Фоновые треки (evil_huge.wav)
  Core/                         # Архитектура, не зависит от движка
    AudioBusType.cs             # Enum шин: Master, Sfx, Music, Voice, Ambience, Ui, Narrative
    AudioBus.cs                 # Шина: Volume, Pitch, VoiceLimit, дакинг между шинами
    AudioLayer.cs               # Параметры звука: шина, громкость, питч, приоритет, IsSpatial
    AudioEvent.cs               # Семантическое событие: имя → файлы, round-robin/random, PitchVariation
    AudioPlaybackHandle.cs      # Хендл активного голоса: Stop(fadeOut), SetPosition, SetVolume, SetPitch
  Backend/                      # Реализации воспроизведения
    IAudioBackend.cs            # Интерфейс бэкенда (Unity AudioSource / FMOD / Wwise)
    UnityAudioBackend.cs        # Реализация на AudioSource: дакинг, VoiceLimit, fade-out, SpaceBlend
    FmodAudioBackend.cs         # FMOD Studio: загрузка банков с сервера/StreamingAssets, проброс шин
    AudioSystem.cs              # Синглтон (SingletonMonoBehaviour): реестр шин и событий, API Play/Play2D
  Data/
    AudioLibrary.cs             # ScriptableObject для звукорежиссёра: реестр событий в инспекторе
  Spatial/
    AudioSpatial.cs             # Компонент на GameObject: следящий пространственный звук
    AudioZone.cs                # Триггерная зона: меняет громкость шины при входе (пещера, под водой)

  WavUtility.cs                 # Декодер WAV-байтов в AudioClip
```

**FMOD интеграция (MMO):**
1. Банки .bank скачиваются с игрового CDN через `ClientAssetLoader` (ETag-кеширование)
2. Фоллбек для разработки: `StreamingAssets/Audio/*.bank`
3. FMOD проект: `FodinaeAudio/FodinaeAudio.fspro` (в корне репозитория)
4. Шины FMOD мапятся на `AudioBusType` (bus:/Sfx, bus:/Music...)
5. Бэкенд выбирается через `#if FMOD` в `AudioSystem.OnAwake()`:
   - `FMOD` определён → `FmodAudioBackend`
   - Не определён → `UnityAudioBackend` (без FMOD-пакета)

**Примеры использования:**
```csharp
// Звукорежиссёр: просто имя события
AudioSystem.Play("dig_rock");
AudioSystem.Play2D("ui_click");
AudioSystem.PlayAt("explosion", transform.position);

// Дакинг: голос просаживает SFX на 6 dB
var voiceBus = AudioSystem.Instance.GetBus(AudioBusType.Voice);
voiceBus.DuckBus(AudioSystem.Instance.GetBus(AudioBusType.Sfx), -6f, 0.3f, 0.5f);

// AudioSpatial на GameObject — звук следует за объектом
robot.AddComponent<AudioSpatial>().SetEvent("robot_idle");

// AudioZone — триггер меняет громкость шины
caveTrigger.AddComponent<AudioZone>()._targetBus = AudioBusType.Ambience;
```

- **SFXEffectManager** (см. 3.1): Принимает `SFXPacket` от сервера, спавнит `SFXEffectInstance` — визуальный + аудио-эффект в координатах мира.
- **SFXEffectInstance**: Запрашивает пул-слот у `SFXPool`, загружает аудио и визуал (GIF/PNG/Effekseer), позиционируется и проигрывает.
- **RuntimeEffekseerLoader**: Рантайм-загрузка эффектов Effekseer.

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

### Настройка

- `Directory.Build.props` — подключает анализаторы через NuGet во все `.csproj`
- `.stylecop.json` — отключает нерелевантные для Unity правила (XML-доки, file headers)
- `.editorconfig` — severity для каждого правила (`none` / `warning` / `error`)
