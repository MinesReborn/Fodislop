# PROJECT AUDIT — Fodinae / Fodislop-2

**Дата:** 2026-07-19
**Unity:** 6000.2.10f1, URP 2D
**Файлов C#:** 92 (ноль .asmdef)
**Репозиторий:** main, commit `02c7fb9`

---

## 1. РЕПОЗИТОРИЙ И ВОРКФЛОУ

| #  | Проблема | Детали |
|----|----------|--------|
| 1  | VS Code workspace не подходит к воркфлоу проекта | — |
| 2  | `UI_SCALING_AUDIT.md` не трогался 2 месяца | — |
| 3  | README неактуален | — |
| 4  | Артефакт opencode в репозитории | Актуален ли? |
| 5  | Название проекта | Fodinae? effekseer? Fodislop? Непонятно |
| 6  | `GEMINI.md`, `CLAUDE.md` | Если не используются — можно удалить |
| 7  | `.vsconfig` | Непонятный файл, настройки обычно в `.vscode/` |
| 8  | Папка `.vscode` отсутствует | Нет единой конфигурации IDE |
| 9  | `.stylecop.json` | Актуален и используется ли? |
| 10 | Нет pre-commit хука | Сломанный код может быть запушен |
| 11 | `.editorconfig` — правила линтера | Требуют пересмотра |
| 12 | `/docs` не обновляются | — |
| 13 | Нет CI/CD | `.github/` содержит только `ISSUE_TEMPLATE/` и `PULL_REQUEST_TEMPLATE.md`. Ни одного workflow. Билд не проверяется автоматически |
| 14 | Git-зависимости без pinning'а | 5 пакетов с GitHub без коммит-хеша. Любой пуш в main `MinesServerNetworking` может сломать проект |
| 15 | 30+ неиспользуемых built-in модулей | `cloth` (deprecated), `vehicles`, `wind`, `terrain`, `terrainphysics`, `adaptiveperformance`, `screencapture`, `umbra`, `ai`, `director`, `vectorgraphics`, `assetbundle` — для 2D игры на тайлах нужно 8-10 модулей |

---

## 2. АРХИТЕКТУРА

| #  | Проблема | Детали |
|----|----------|--------|
| 1  | Отсутствие слоёв | Networking дёргает UI, UI дёргает World, World дёргает Networking. Нет Presentation/Domain/Infrastructure |
| 2  | Состояние размазано по синглтонам | HP в `PlayerStatsModel`, инвентарь в `InventoryModel`, позиция в `RobotManager`, мир в `MapManager`. Нет единого GameState — нельзя сериализовать/откатить/протестировать |
| 3  | Networking — транслятор, не шлюз | PacketHandler получает пакет и сразу мутирует состояние + UI. Нет промежуточного слоя. Невозможны: replay, client-side prediction, тестирование без сети |
| 4  | UI строится из серверных пакетов | Сервер шлёт `OpenWindowPacket` с полным UI-деревом. PacketUIBuilderFactory строит UI. Клиент не знает свои окна — нельзя оффлайн, кастомизацию UI, пререндеринг |
| 5  | DummyConnection — не мок, а второй клиент | 1,894 строки с игровой логикой (коллизии, passability, диг-валидация) дублирующейся с клиентской. Dual-authority проблема |
| 6  | Нет командной шины | Ввод: WASD/клик → напрямую `NetworkService.Send(packet)`. Результат: PacketHandler → напрямую UI |
| 7  | Y-инверсия — незавершённая абстракция | `CoordinateUtils` есть, но `PlayerMovementController` инлайнит Y-инверсию вместо использования утилиты |
| 8  | Жёсткая привязка к Unity API | Синглтоны на `FindFirstObjectByType`, `GameObject.Find`, `DontDestroyOnLoad`. Игровая логика неотделима от Unity |
| 9  | Синглтоны без интерфейсов | Нет `INetworkService`, `IWorldProvider`. Нельзя подменить для тестов |
| 10 | UI Toolkit: серверный рендерер + клиентская лепка | 13 билдеров конвертируют Packet → VisualElement. Клиентские UI-классы (PlayerHUD, InventoryUI) строят UI вручную без общего framework'а. Нет компонентов, data binding, навигации |

---

## 3. GOD-КЛАССЫ

| #  | Файл | Строк | Проблема |
|----|------|-------|----------|
| 1  | `DummyConnection.cs` | 1,894 | Симулирует целый сервер: карта, коллизии, предметы, миссии, телепорты, баффы, чат, ping. Каждая система добавлена отдельным блоком снизу файла с разным стилем кода |
| 2  | `SingleMeshTerrainRenderer.cs` | ~2,500+ (109 KB) | Vertex buffer, flood-fill, atlas indexing, shader interaction, cell cache. Три слоя XML-документации друг на друге |
| 3  | `PacketHandler.cs` | 805 | 41 подписка на пакеты. Управляет окнами, модалками, инвентарём, чатом, миссиями, статами |
| 4  | `Robot.cs` | 617 | Position interpolation, rotation smoothing, tentacle simulation, async asset loading, clan badges |
| 5  | `PlayerHUD.cs` | ~1,600+ (68 KB) | Layout + логика. HP, валюта, скиллы, баффы, миссии, кнопки — всё в одном классе |

---

## 4. СИНГЛТОНЫ (ДУБЛИРОВАНИЕ И НЕКОНСИСТЕНТНОСТЬ)

10 классов с идентичным ~35-строчным синглтон-паттерном:
- `ClientAssetLoader`, `RobotManager`, `MapManager`, `PackManager`, `SFXEffectManager`
- `ConnectionManager`, `NetworkService`, `WorldTextureManager`, `SFXPool`, `PlayerStatsModel`

Необходим `SingletonMonoBehaviour<T>` базовый класс.

**Неконсистентные реализации:**
- `AudioManager` — простой `Instance = this` в Awake, без `_isQuitting`
- `ServerConfig` — устаревший `FindObjectOfType`, без `_isQuitting`
- `MapStorage` — не-MonoBehaviour, без потокобезопасности
- `SFXPool` — `FindAnyObjectByType` вместо `FindFirstObjectByType`
- `PlayerStatsModel` — устаревший `FindObjectOfType`

---

## 5. ПРОИЗВОДИТЕЛЬНОСТЬ

| #  | Severity | Проблема | Файл |
|----|----------|----------|------|
| 1  | **CRITICAL** | `Keyboard.current.allKeys` создаёт новый массив каждый вызов в Update | `PlayerInteractionController.cs:63` |
| 2  | **HIGH** | Аллокации в `AnimationContainerDecoder.Decode()`: новый Texture2D, Color[], Color32[] на каждый кадр GIF/WebP | `AnimationContainerDecoder.cs:75-91` |
| 3  | MEDIUM | `AudioManager.Update()` крутится каждый кадр с пустым `_activeInstances` | `AudioManager.cs:108` |
| 4  | MEDIUM | `FPSCounter` — O(n) суммы массива каждый кадр вместо running sum O(1) | `FPSCounter.cs:58-63` |
| 5  | MEDIUM | `Camera.main` в Update (внутренний FindObjectOfType) | `PlayerInteractionController.cs:19` |
| 6  | LOW | 67 `Debug.Log` в `MapStorage.InitWorld()` | `MapStorage.cs` |
| 7  | LOW | `FindObjectOfType` в рантайме: 39 использований в 14 файлах | Разные |
| 8  | LOW | `ChatInput` — 6 FindObjectOfType на каждый Tab | `ChatInput.cs:14-34` |

---

## 6. UNITY 6: ЧТО ИСПОЛЬЗУЕТСЯ / НЕ ИСПОЛЬЗУЕТСЯ

| Инструмент | Статус | Где должно быть |
|------------|--------|-----------------|
| **Burst + Jobs** | ❌ Нет | `SingleMeshTerrainRenderer` (сборка вершин), flood-fill, `WorldLayer` |
| **Native Collections** | ❌ Нет | Vertex buffers, flood-fill, декодинг анимаций |
| **Incremental GC** | ❌ Не включён | Player Settings — одна галочка |
| **USS стайлинг** | ❌ 1 файл (7 строк) | 99% inline C# стилей. Цвета в каждом UI-классе захардкожены |
| **ScriptableObjects** | ❌ Нет | `GameConstants.cs` — static class вместо `.asset` файлов |
| **LOD / Occlusion** | ⚠️ Frustum only | `SingleMeshTerrainRenderer` — свой viewport culling, без LODGroups |
| **Object Pooling** | ⚠️ Частично | `SFXPool` (601 строка) — хороший пул. Но `SoundEffectInstance` fallback не пулится, `DigEffect` без пула |
| **Async Loading** | ✅ | UniTask + AssetCache + PersistentAssetCache + ETag — грамотно |
| **Addressables** | N/A | Кастомный серверный пайплайн заменяет |
| **Assembly Definitions** | ❌ 0 `.asmdef` (игровой код) | 92 .cs файла в один `Assembly-CSharp.dll` |
| **Нативные модули** | 30+ лишних | См. пункт 1.15 |

---

## 7. ТЕСТЫ

| #  | Проблема | Детали |
|----|----------|--------|
| 1  | Ровно 1 тестовый файл | `PlayerMovementBoundaryTests.cs` — `Assert.Pass("...")`, не тестирует ничего |
| 2  | `com.unity.test-framework` установлен | 1.7.0, полностью простаивает |
| 3  | Легко тестируемые модули без тестов | `CoordinateUtils`, `ETagCalculator`, `TileBitmaskConverter`, `AnimationContainerDecoder.DetectType()`, `WorldLayer<T>` LRU cache eviction, `AssetCache` deduplication |

---

## 8. МЁРТВЫЙ КОД И НЕИСПОЛЬЗУЕМОЕ

| #  | Что | Где |
|----|-----|-----|
| 1  | `using Unity.VisualScripting.Antlr3.Runtime` | `ConnectionManager.cs:10` |
| 2  | `_perspectiveShrink = 0.3f` — serialized field, never read | `SurfaceRenderer.cs:14` |
| 3  | Закомментированный rotation jitter | `Robot.cs:215-218` |
| 4  | `RunTilingTestLoop()` — полностью реализован, закомментирован | `DummyConnection.cs:383` (~75 строк) |
| 5  | `SendMockWindow()`, `CreateMockWindow()`, `CreateComprehensiveMockWindow()` — не вызываются | `DummyConnection.cs` (~200 строк) |
| 6  | `CreateCellTypeLabels()` — debug-only | `DummyConnection.cs` |
| 7  | `CameraFollow` добавляет `PlayerInput` компонент — никогда не используется | `CameraFollow.cs` |

---

## 9. КОД-СТАЙЛ И КАЧЕСТВО

| #  | Проблема | Детали |
|----|----------|--------|
| 1  | Public mutable поля | `ServerConfig.DigCooldown`, `MaxGlobalChatLength`, `MaxLocalChatLength` — невозможна валидация |
| 2  | Хрупкий string-based канал метаданных | `AssetCache` зашивает FPS в имя текстуры: `"Cache_GIF_...|FPS=10|FrameHeight=64"`. `WorldTextureManager` парсит через `Split('|')` |
| 3  | Mixed-language комментарии | Русские: `"✅ Лоадер показан"`, `"Копатель-ученик"`. Английские: structural docs |
| 4  | Оскорбительные имена в enum | `CellType.NiggerRock`, `AliveNigger` из внешней библиотеки, используются по всей кодовой базе |
| 5  | Анализаторы настроены но не enforced | StyleCop, NetAnalyzers, Roslynator — violations повсюду |
| 6  | Namespace-зоопарк | `Fodinae.Scripts`, `Fodinae.Scripts.Game.Managers`, `Fodinae.UI`, `Fodinae.UI.Binding`. Корневые классы без namespace (`WorldLayer`, `TileMaskConverter`) |
| 7  | Нет `Player.prefab` используется программно? | `Assets/Player.prefab` на корневом уровне, но `RobotManager.GetOrCreateRobot()` имеет свой `_robotPrefab` и логику инстанцирования |
| 8  | Hardcoded asset paths | `"audio/evil_huge"`, `"skin/bee.png"`, `"cells/32"`, `$"vfx/{...}"` — раскиданы по коду, нет `AssetPaths` класса |
| 9  | AI-документация кода | AGENTS.md (376 строк) — сам сгенерирован AI. Код содержит ChatGPT-типичные комментарии: "centralized utility class", "supports both development and build environments" |
| 10 | Duplicate XML summaries | `SingleMeshTerrainRenderer.cs` — три разных `<summary>` тега наложены друг на друга |

---

## 10. СТАТИЧЕСКИЙ МУТАБЕЛЬНЫЙ СТЕЙТ

| #  | Что | Где |
|----|-----|-----|
| 1  | `DummyConnection.IgnoreCollision = false` — static field | `DummyConnection.cs:62`, устанавливается в `PlayerMovementController` |
| 2  | `PacketHandler.IsInputBlocked` — static, читается 7 классами | `PacketHandler.cs:36` |
| 3  | `NetworkService.Instance.Send(...)` — прямой доступ из ~80+ мест | 41 файл |

---

## 11. ПРОБЛЕМНЫЕ ФАЙЛЫ

| Файл | Строк | Основная проблема |
|------|-------|-------------------|
| `SingleMeshTerrainRenderer.cs` | ~2,500+ (109 KB) | Самый большой файл. Нужно разбить на TerrainMeshBuilder + TerrainVertexCache + TerrainFloodFiller |
| `DummyConnection.cs` | 1,894 | Разбить на MockMissionService, MockBuffService, MockMapService, MockItemService, MockTeleportService, MockChatService |
| `PacketHandler.cs` | 805 | Domain-специфичные handlers: ChatPacketHandler, InventoryPacketHandler, WorldPacketHandler, PlayerStatsHandler |
| `PlayerHUD.cs` | ~1,600+ (68 KB) | Отделить View от логики. Вынести стили в USS |
| `MapStorage.cs` | 487 | 67 Debug.Log, 5 fallback-методов. Половина файла — error handling |
| `TextureStorageManager.cs` | 362 | Fuzzy file matching, ConcurrentDictionary, fallback random textures — для "загрузить файл с диска" переусложнено |
| `SFXPool.cs` | 601 | Сам по себе хорош, но fallback SoundEffectInstance не пулится |

---

## 12. БЕЗОПАСНОСТЬ

| #  | Проблема | Где |
|----|----------|-----|
| 1  | Hardcoded placeholder credentials | `ConnectionManager.cs:118`: `"fingerprint"`, `"token"` |
| 2  | MD5 для ETag | `ETagCalculator.cs:13` — криптографически сломан, SHA256 — стандарт |
| 3  | Client-side authoritative константы | `ServerConfig.DigCooldown = 0.3f` — клиент решает когда можно копать. `GameConstants.WorldDarknessFactor = 0.8f` — "hardcoded for all players" |

---

## 13. BUILD / PIPELINE

| #  | Проблема | Где |
|----|----------|-----|
| 1  | BuildScript с рефлексией | `BuildScript.cs:95-103` — `Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings...")`, может упасть молча |
| 2  | `CsProjFix.cs` | Симптом что проект не "просто открывается" в IDE |
| 3  | Effekseer embedded as local package | `"file:Packages/org.effekseer.effekseerforunity"` — обновления ручные и неотслеживаемые |
| 4  | Prebaked map copy from StreamingAssets | `MapStorage` копирует `.mapb` файлы при инициализации — подвержено ошибкам прав доступа |

---

## 14. `[EXECUTEALWAYS]` ОПАСНОСТИ

| #  | Файл | Проблема |
|----|------|----------|
| 1  | `MapManager` | `[ExecuteAlways]` + `OnDrawGizmosSelected` читает `MapStorage` (файловый I/O) в редакторе |
| 2  | `StandaloneWorldInitializer` | `[ExecuteAlways]` — создаёт тестовые миры в редакторе, трогает MapStorage |

---

## 15. КЛИЕНТСКИЕ КОНСТАНТЫ, КОТОРЫЕ ДОЛЖНЫ БЫТЬ СЕРВЕРНЫМИ

| Константа | Значение | Файл |
|-----------|----------|------|
| `DigCooldown` | 0.3f | `ServerConfig.cs:29` |
| `WorldDarknessFactor` | 0.8f | `GameConstants.cs:11` |
| `DEFAULT_MOVE_SPEED` | 15f | `GameConstants.cs:31` |
| `REFERENCE_MOVE_SPEED` | 25f | `GameConstants.cs:32` |
| `MaxGlobalChatLength` | 50 | `ServerConfig.cs:30` |
| `MaxLocalChatLength` | 20 | `ServerConfig.cs:31` |

---

---

## 17. AI-ГЕНЕРАЦИЯ КОДА

Полностью 100% AI-сгенерированный код через несколько моделей/сессий, без рефакторинга между генерациями.

**Паттерн "append-bottom" в DummyConnection.cs:**
- Каждая система (миссии, баффы, телепорты, глубина, чат) добавлена отдельным блоком в конец файла новой AI-сессией
- Разные стили внутри одного файла: switch expression vs if-else, разное форматирование
- ~275 строк мёртвого AI-кода в одном файле (неиспользуемые mock-методы + закомментированный tiling test loop)

**Паттерн "слоёного пирога" в SingleMeshTerrainRenderer.cs:**
- Три слоя XML-документации наложены друг на друга (AI перегенерировал summary не удалив старые)
- FBPW-алгоритм (Frontier-Based Parallel Wavefront) — академические аббревиатуры, неиспользуемые в остальном проекте

**Data enumeration:**
- 87 cell types перечислены вручную в `CreateTestCellConfigurations()` вместо data-файла
- 40+ item descriptions на русском в 80-строчном switch expression `GetItemInfo()`
- Человек положил бы в JSON/ScriptableObject

**Признаки разных моделей:**
- 10 копипастных синглтонов — механическая точность (GPT-3.5/4)
- PacketHandler window management — элегантная архитектура, DFS, композиция (Claude 3.5/4 Sonnet)
- MapStorage 5-уровневый fallback с "CRITICAL:" логами — verbose perfectionism (Claude)
- AGENTS.md (376 строк) — идеальная структура, кросс-референсы, "Notable edge cases" (Claude/GPT-4o)

**Последствия для поддерживаемости:**
- Нет рефакторинга между сессиями — код накапливается слоями
- Неконсистентный стиль внутри файлов
- Никто (включая автора промптов) не знает всю кодовую базу целиком
- AGENTS.md сгенерирован AI по AI-коду — документация может врать о реальном поведении

---

## 18. UI "ДВИЖОК" — ДЕТАЛЬНЫЙ РАЗБОР

Два несвязанных подхода, ни один не является фреймворком:

### 18a. Серверно-управляемые окна (PacketHandler + PacketUIBuilderFactory)
- 13 билдеров: Text, Image, Panel, Line, DockPanel, Canvas, Grid, ScrollViewer, TextBox, Selectable, Slider, IntDropdown, StringDropdown
- Превращают серверные `IGUIComponentPacket` → UI Toolkit `VisualElement`
- Окна в LIFO стеке (`_openWindows`)
- Клики: DFS-обход дерева → `ClickContextResolver` → `ElementClickPacket`
- Значения: SmartFormat + `LogiCalcFormatter` через `WindowBinding`
- **Это минимальный транслятор, не движок** — нет переиспользуемых компонентов, стейт-менеджмента, анимаций, переходов

### 18b. Хардкоженые UI-классы
- `PlayerHUD.cs` (1,600+ строк, 68 KB) — HP, валюта, скиллы, баффы, миссии, все кнопки
- `InventoryUI.cs` (~600 строк, 22 KB) — hotbar, инвентарь, drag-and-drop, tooltip
- `PauseMenu.cs` — настройки, ESC-логика
- `GlobalChatUI.cs`, `LocalChatPopup.cs`, `FloatingChatManager.cs` — чат
- `WorldMapController.cs`, `MinimapController.cs`, `WorldMapRenderer.cs` — миникарта
- `ProgrammatorGrid.cs`, `RadialMenu.cs` — Programmator
- `ModalWindowHandler.cs` — модальные окна

**Что дублируется между ними:**
- Каждый хранит свои константы цветов (одинаковый `_panelBgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f)` в PlayerHUD и InventoryUI)
- Каждый делает `_doc = FindObjectOfType<UIDocument>()` по-своему
- Каждый сам подписывается на `PlayerStatsModel.Instance.OnXxxChanged`
- Нет общего подхода к show/hide, анимациям, фокусу, модальности

**Что отсутствует как UI framework:**
| Возможность | Где нужно | Почему нет |
|------------|-----------|------------|
| Data binding (MVVM/MVP) | Все UI классы | PlayerHUD вручную обновляет все Label'ы при OnStatsChanged |
| Переиспользуемые компоненты | Все окна/панели | Каждый класс с нуля |
| Система переходов/анимаций | Открытие/закрытие окон | Окна просто появляются/исчезают (display: none/flex) |
| Управление фокусом ввода | Все контроллеры | `IsInputBlocked` проверяется вручную в 7+ классах |
| Тема / стилизация | Все UI | Цвета и размеры в C# константах, не в USS |
| Навигация / роутинг | Окна, модалки, попапы | LIFO стек только для серверных окон |

---

## 19. ПОЛНЫЙ СПИСОК НЕИСПОЛЬЗУЕМЫХ BUILT-IN МОДУЛЕЙ

30+ модулей в `Packages/manifest.json`, из которых для 2D игры на тайлах нужно ~8-10:

| Модуль | Нужен? |
|--------|--------|
| `com.unity.modules.accessibility` | Нет |
| `com.unity.modules.adaptiveperformance` | Нет |
| `com.unity.modules.ai` | Нет |
| `com.unity.modules.androidjni` | Нет (если нет Android-специфичного кода) |
| `com.unity.modules.assetbundle` | Нет |
| `com.unity.modules.cloth` | **Deprecated** |
| `com.unity.modules.director` | Нет |
| `com.unity.modules.imageconversion` | Нет |
| `com.unity.modules.imgui` | Возможно (Editor) |
| `com.unity.modules.particlesystem` | Возможно (VFX) |
| `com.unity.modules.physics` | Нет (2D проект) |
| `com.unity.modules.physics2d` | Возможно |
| `com.unity.modules.physicscore2d` | Возможно |
| `com.unity.modules.screencapture` | Нет |
| `com.unity.modules.terrain` | Нет |
| `com.unity.modules.terrainphysics` | Нет |
| `com.unity.modules.umbra` | Нет |
| `com.unity.modules.unityanalytics` | Нет |
| `com.unity.modules.unitywebrequest*` (5 модулей) | Возможно (WebRequest) |
| `com.unity.modules.vectorgraphics` | Нет |
| `com.unity.modules.vehicles` | Нет |
| `com.unity.modules.video` | Нет |
| `com.unity.modules.wind` | Нет |
| `com.unity.modules.xr` | Нет |

**Оставить:** animation, audio, jsonserialize, tilemap, ui, uielements, physics2d, particlesystem (для VFX), webrequest — ~10 модулей.

---

## 20. РАСШИРЕННЫЙ BUILD / PIPELINE

| #  | Проблема | Детали |
|----|----------|--------|
| 5  | UniTask vendored | `Assets/Plugins/UniTask/` — копия библиотеки в проекте вместо package reference. Обновления ручные |
| 6  | Effekseer embedded as local package | `"file:Packages/org.effekseer.effekseerforunity"` — 9 asmdef внутри, не обновляется через package manager |
| 7  | `com.unity.visualscripting` установлен | 1.9.11 — Bolt/Visual Scripting, не используется в проекте |
| 8  | Нет Assembly Versioning | Нет `AssemblyInfo.cs`, нет версионирования сборок |
| 9  | IL2CPP не протестирован | Код содержит рефлексию (`BuildScript`), может не работать под IL2CPP |
| 10 | Текстуры копируются вручную | AGENTS.md: "Textures/ must be copied manually (not Resources/Addressables pipeline)" |

---

## 21. ДОПОЛНИТЕЛЬНЫЕ ПРОБЛЕМЫ

| #  | Проблема | Детали |
|----|----------|--------|
| 1  | `SurfaceRenderer` — два меша перестраиваются каждый кадр | `LateUpdate`, bounds 100×100×10. Transit + Perspective mesh rebuild every frame |
| 2  | `WorldMapRenderer` — обновление каждые ~30 FPS | 512×height пикселей текстура, RGBA32, перестраивается даже когда миникарта не видна |
| 3  | `WorldTextureManager` — атлас 4096×4096 | При превышении максимального размера новые текстуры просто не попадают в атлас — нет обработки переполнения |
| 4  | `gif` декодер — `mgGif.cs` | Старый сторонний код в `Assets/Scripts/MfGif/`, вероятно не обновляется, упомянут в `TODO.md` |
| 5  | Асинхронная загрузка без CancellationToken | `ClientAssetLoader.LoadAndApplyTexture()` возвращает `UniTaskVoid` — нельзя отменить загрузку при выходе из сцены |
| 6  | Нет обработки `OnApplicationFocus` / `OnApplicationPause` | Синглтоны не сбрасывают состояние при сворачивании игры |
| 7  | `InventoryModel` — разделение между несколькими InventoryUI | Статический синглтон, но InventoryUI создаёт экземпляр в Start. Кто владеет состоянием инвентаря? |
| 8  | PlayerHUD биндит кнопки напрямую к NetworkService.Send | `"join_clan"`, `"leave_clan"`, `"open_missions"`, `"test_modal"` — хардкоженые строки-тэги, нет проверки что сервер обработает |
| 9  | UI Toolkit themeUss GUID | AGENTS.md: "bound by GUID — broken PanelSettings = blank UI" — нет fallback |
| 10 | `DummyConnection` — `_allCellTypes` 87 entries | Включает портированные из MinesServer проблемные имена |

---

## 22. ТОП ФИКСОВ ПО ПРИОРИТЕТУ

### Критические (блокируют разработку):
1. **DummyConnection** — разбить: MockMapService, MockItemService, MockMissionService, MockBuffService, MockChatService, MockTeleportService. Невозможно поддерживать 1,894 строки в одном файле где перемешаны 10+ систем
2. **Assembly Definitions** — добавить `.asmdef`: Fodinae.World, Fodinae.UI, Fodinae.Game, Fodinae.Networking, Fodinae.Audio. 92 .cs в один DLL — полная перекомпиляция при любом изменении
3. **SingleMeshTerrainRenderer** — разбить: TerrainMeshBuilder (vertex buffer), TerrainVertexCache (cell lookup), TerrainFloodFiller (background fill). 2,500+ строк, 109 KB
4. **PacketHandler** — domain-специфичные handlers: ChatPacketHandler, InventoryPacketHandler, PlayerStatsPacketHandler, MissionPacketHandler, WorldPacketHandler

### Высокие (мешают повседневной работе):
5. **Синглтон базовый класс** `SingletonMonoBehaviour<T>` — убить 300+ строк дубликата и 5 разных реализаций в 10 файлах
6. **FindObjectOfType в рантайме** — 39 вызовов в 14 файлах. Заменить на кеширование/singleton/SerializeField. ChatInput: 6 вызовов на каждый Tab
7. **Incremental GC** — включить в Player Settings (одна галочка)
8. **MapStorage** — 67 Debug.Log, 5 fallback-методов → оставить 1-2 fallback'а
9. **TextureStorageManager** — 362 строки для "загрузить файл с диска". Упростить до 30-50 строк
10. **PlayerHUD** — отделить View от логики. Вынести стили в USS. 1,600+ строк в одном классе
11. **Тесты** — CoordinateUtils, ETagCalculator, TileBitmaskConverter, AnimationContainerDecoder.DetectType(), WorldLayer LRU eviction. 1 placeholder-тест на весь проект — неприемлемо

### Средние (качество жизни):
12. **`Keyboard.current.allKeys` в Update** — критическая аллокация каждый кадр. PlayerInteractionController.cs:63
13. **USS стайлинг** — вынести все цвета/шрифты из C# в `.uss`. Сейчас 1 trivial USS на 7 строк
14. **GameConstants → ScriptableObjects** — hardcoded static class заменить на `.asset` файлы
15. **AnimationContainerDecoder — pooling/NativeArray** — для декодинга GIF/WebP кадров. Каждый кадр: новый Texture2D + Color[] + Color32[]
16. **Git dependency pinning** — добавить коммит-хеши в manifest.json для 5 GitHub-пакетов
17. **Почистить manifest.json** — убрать 20+ неиспользуемых модулей (cloth, vehicles, wind, terrain, umbra, xr...)
18. **Мёртвый код** — удалить RunTilingTestLoop (~75 строк), SendMockWindow/CreateMockWindow/CreateComprehensiveMockWindow (~200 строк), using Unity.VisualScripting.Antlr3.Runtime, закомментированный rotation jitter
19. **Выпилить Effekseer** — если не используется: local package, 9 asmdef, 30+ C# файлов. Если используется: перевести на versioned package reference
20. **Hardcoded credentials** — `"fingerprint"`, `"token"` в ConnectionManager → нормальный auth flow
21. **`com.unity.visualscripting`** — удалить если не используется

### Низкие (накопительный эффект):
22. **Namespace cleanup** — привести к folder structure. Убрать безымянные классы из корня Scripts/
23. **Выключить `[ExecuteAlways]`** на MapManager и StandaloneWorldInitializer
24. **Camera.main → кеш** — не вызывать в Update (внутренний FindObjectOfType)
25. **FPSCounter: O(n) → O(1)** — running sum вместо пересчёта всего массива каждый кадр
26. **MD5 → SHA256** — ETagCalculator
27. **Pre-commit hook** — линтер/форматтер перед коммитом
28. **CI/CD** — GitHub Actions: билд + тесты
29. **README, /docs** — актуализировать
30. **Оскорбительные enum-имена** — NiggerRock, AliveNigger. Почистить на уровне клиента даже если приходят из внешней библиотеки
