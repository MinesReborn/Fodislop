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
  Editor/              # BuildScript.cs, CsProjFix.cs — тулинг сборки
  Plugins/             # Vendored DLL: SharpCompress, ZstdSharp, LZ4, NetCoreServer, Genumerics
    UniTask/           # Vendored UniTask (полный пакет)
  Scenes/              # SampleScene.unity, TextureStorageTestScene.unity
  Scripts/
    ClientAssetLoader.cs      # Загрузка ассетов с сервера/локально
    WorldLayer.cs              # Дисковый стриминг чанков (RLE + LRU кэш)
    MainMenu.cs                # Главное меню
    TileMaskConverter.cs       # Битмаски авто-тайлинга
    PacketUIBuilder.cs         # Сборка UI из серверных пакетов
    Game/
      Managers/        # MapManager, MapStorage, RobotManager, PackManager
      Robot.cs, Pack.cs
    Networking/
      Connection/      # DummyConnection и реальные коннекторы
      NetworkService.cs, PacketHandler.cs
    Player/            # PlayerMovementController, CameraFollow
    UI/
      Builders/        # PacketUIBuilderFactory и типовые билдеры
      Controls/        # UI-контролы
      FPSCounter.cs, MinimapPlaceholder.cs, StyleApplicator.cs
    World/             # Рендеринг: SingleMeshTerrainRenderer, WorldTextureManager,
                       #   TextureAtlas, WorldBackgroundSetup, SceneSetup,
                       #   StandaloneWorldInitializer, RenderingConstants
  Settings/            # URP и Renderer2D конфиги
  Textures/            # cells/, skin/, clan/, pack/, ui/ — тайлы и UI-ассеты
  UI Toolkit/          # .uxml и .uss файлы
```

## 3. Архитектура систем

### 3.1 Сетевой слой (Networking)

- **NetworkService**: Синглтон. Подписка: `Subscribe<T>` / `Unsubscribe<T>`.
- **PacketHandler**: Получает пакеты и делегирует менеджерам (`MapManager`, `RobotManager`, `PackManager`).
- **Пакетный UI**: Динамическая сборка UI из `OpenWindowPacket` через `PacketUIBuilderFactory`.

### 3.2 Мир и Рендеринг (World & Rendering)

- **MapManager**: Жизненный цикл мира (`WorldInitPacket`, `MapRegionPacket`), конфигурации ячеек, тайл-группы.
- **MapStorage**: Хранилище данных карты (чанки 32x32). Кэширует в `persistentDataPath/*.mapb`. Требует `InitWorld` перед рендерингом.
- **WorldLayer\<T\>**: Дисковый стриминг с LRU-кэшем в RAM. RLE-сжатие. Append-only запись с компактификацией.
- **WorldTextureManager**: Загружает тайл-текстуры из файловой системы (не Resources/Addressables), упаковывает в `TextureAtlas`.
- **SingleMeshTerrainRenderer**: Один меш на весь видимый террейн. 7 UV-каналов (атлас, тайлинг, анимация, тени, рельеф). `Sorting Order = -1000`.
- **Инверсия Y**: Сервер: Y↓ (0 = верх). Unity: Y↑. Формула: `unityY = WorldHeight - 1 - serverY`.

### 3.3 Игрок и Управление

- **PlayerMovementController**: Ввод через New Input System. Клиентская валидация по `Passable` + серверная через `MovePacket`.
- **CameraFollow**: Следование камеры за игроком.

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
- **Сцена содержит**: `[WorldTextureManager]`, `SingleMeshTerrainRenderer`, `UIDocument`, `Main Camera`, `Global Light 2D`, `SceneSetup`, `AutoMapManager`, `MapManager`.

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

