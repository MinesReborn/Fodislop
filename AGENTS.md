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

    # Утилиты
    Utils/
      CoordinateUtils.cs          # Конвертация координат сервер↔Unity (инверсия Y)
      FodislopGizmos.cs           # Визуальные Gizmos для отладки

    # Мир и рендеринг
    World/
      SingleMeshTerrainRenderer.cs  # Один меш на весь террейн, 7 UV-каналов
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
    mfGif/
      mgGif.cs                    # GIF-декодер (MG.GIF)

  Settings/            # URP и Renderer2D конфиги
  Textures/            # cells/, skin/, clan/, pack/, ui/, items/, audio/,
                       #   Crystalls/, vfx/, tail/, exported/ — тайлы, UI, экипировка
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
- **Инверсия Y**: Сервер: Y↓ (0 = верх). Unity: Y↑. Формула: `unityY = WorldHeight - 1 - serverY`. Централизована в `CoordinateUtils`.

### 3.3 Игрок и Управление

- **PlayerMovementController**: Ввод через New Input System. Клиентская валидация по `Passable` + серверная через `MovePacket`.
- **PlayerInteractionController**: Обработка кликов и клавиш (копка, использование предметов). Отправляет `DigRequestPacket`, `ItemUsePacket` и т.д.
- **CameraFollow**: Следование камеры за игроком.

### 3.4 Аудио и Эффекты (Audio & Effects)

- **AudioManager**: Синглтон. Управление фоновой музыкой (`AudioSource`), SFX-эффектами. Громкость сохраняется в `PlayerPrefs`.
- **SoundEffectInstance**: Один SFX-экземпляр (тайл, копка). Загружает WAV из файловой системы, воспроизводит через `PlayOneshot`.
- **WavUtility**: Чтение WAV-файлов в `AudioClip` (поддержка разных форматов, копирование памяти только при необходимости).
- **SFXEffectManager** (см. 3.1): Принимает `SFXPacket` от сервера, спавнит `SFXEffectInstance` — визуальный + аудио-эффект в координатах мира.
- **SFXEffectInstance**: Отдельный экземпляр эффекта. Запрашивает пул-слот у `SFXPool`, загружает аудио и визуал (кадры анимации из GIF/PNG или эффекты Effekseer), позиционируется и проигрывает их.
- **SFXPool**: Объектный пул для звуков и эффектов, предотвращающий избыточный `Instantiate` и снижающий аллокации.
- **RuntimeEffekseerLoader**: Компонент для рантайм-загрузки и управления эффектами Effekseer.
- **Пайплайн воспроизведения звука**:
  1. Код запрашивает `AudioClip` через `ClientAssetLoader.GetAudioAsync`.
  2. Данные проверяются в RAM-кэше `AssetCache` и на диске в `PersistentAssetCache` (с валидацией ETag).
  3. Сырые байты WAV-файла передаются в `WavUtility.ToAudioClip`.
  4. `WavUtility` парсит RIFF/WAVE заголовок (находит `"fmt "` и `"data"` чанки), извлекает PCM-сэмплы, преобразует и нормализует их во `float` (делением на `32768f` для 16-бит) и заполняет ими созданный в памяти `AudioClip` через `SetData`.
  5. Свободный `AudioSource` из пула `SFXPool` воспроизводит готовый клип.

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
