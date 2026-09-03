# AGENTS.md — инструкции для клиента Fodinae

Fodinae — 2D MMORPG-песочница на Unity 6 (`6000.6.0f1`), URP 2D 17.5, C# 12, UI Toolkit, UniTask и сетевые пакеты `darkar25.fodinae.*` из MinesServerNetworking.

---

## 0. Абсолютный запрет на операции с Unity без явного запроса пользователя

**Агенту запрещены любые операции с Unity, Unity Editor и Unity Hub, если пользователь в своём текущем сообщении явно не запросил конкретную операцию с Unity.** Наличие задачи по Unity-проекту, необходимость проверки изменений, ранее запущенный Editor или общая просьба «реализовать» не являются разрешением управлять Unity.

Без отдельного явного запроса пользователя запрещено:

* запускать, открывать, закрывать, перезапускать, завершать, активировать или фокусировать Unity Editor и Unity Hub;
* вызывать `unity`, Unity CLI, Unity MCP/Pipeline, Editor API, menu items, batch mode, build, test runner и любые команды, подключающиеся к Editor или Player;
* проверять состояние Unity (`unity status`, процессы, окна, логи Editor), ждать компиляцию, посылать процессу сигналы или иным способом диагностировать и контролировать запущенный Editor;
* открывать или сохранять сцены, материалы, prefabs и `.asset` через Editor, запускать импорт, reimport, domain reload, компиляцию шейдеров, bake, capture или рендер;
* самостоятельно запрашивать разрешение на такую операцию через системный диалог: инициатива должна исходить от явного сообщения пользователя.

Разрешение действует только на прямо названную пользователем операцию и не распространяется автоматически на последующие Unity-действия. Агент может без запуска Unity редактировать обычные исходные файлы (`.cs`, `.shader`, `.hlsl`, `.uxml`, `.uss` и документацию), анализировать уже предоставленные данные и выполнять инструменты, не запускающие и не контролирующие Unity. Если завершение задачи требует Unity, агент останавливается и сообщает, какая конкретная операция остаётся пользователю или требует его явного запроса.

---

## 0.1. Абсолютный запрет на откат без явного запроса пользователя

**Агенту запрещено откатывать файлы, индекс, коммиты или историю Git, если пользователь в своём текущем сообщении явно не запросил конкретный откат.** Просьбы «исправь», «продолжай», «сделай коммит», «убери из коммита» и системное подтверждение команды не являются разрешением на откат.

Без отдельного явного запроса пользователя запрещено:

* выполнять `git reset` с любыми флагами, `git restore`, `git checkout` для восстановления файлов, `git revert`, `git clean` и эквивалентные операции;
* использовать `git commit --amend`, `git rebase`, force-push и другие команды, переписывающие уже созданные коммиты или историю;
* возвращать содержимое файлов или состояние индекса к `HEAD`, другому коммиту, stash, reflog либо иной сохранённой версии;
* самостоятельно запрашивать системное разрешение на откат: инициатива должна исходить из явного сообщения пользователя и содержать понятную цель отката.

Если агент случайно включил чужие изменения в коммит или обнаружил необходимость отката, он обязан остановиться, точно описать текущее состояние и дождаться прямого указания пользователя. Исправлять такую ситуацию откатом по собственной инициативе запрещено.

Разрешение распространяется только на явно названную операцию и цель. Оно не разрешает дополнительные откаты, очистку рабочего дерева или переписывание других коммитов.

---

## 1. Обязательные стандарты разработки

### C# и Unity-типы

* **Namespace:** Обычные типы используют file-scoped namespace. Типы Unity, наследующие `MonoBehaviour`, `ScriptableObject`, `ScriptableRendererFeature` или `VolumeComponent`, используют block namespace: file-scoped namespace для них может дать `MonoScript.GetClass() == null`.
* **Nullable Reference Types:** Включён `#nullable enable`. Все поля, свойства и параметры явно nullable/non-null (`string?`, `null!`). Использовать primary constructors, `readonly record struct` и collection expressions `[]`, где это уместно.
* **Global Usings:** Находятся в `Fodinae.Core.GlobalUsings` и покрывают `Fodinae.Core`, `AssetPipeline`, `Audio`, `Networking`, `World`, `Game`, `Player`, `UI`, `Effekseer`.
* **StyleCop:**
* Allman braces;
* Обязательные `{}`;
* Пустая строка после `}` (SA1513), но не перед `}` (SA1508);
* Trailing comma в многострочных инициализаторах;
* Пустая строка перед `//`.

* **Именование:**
* Типы / публичные члены / константы — `PascalCase`;
* Private поля — `_camelCase`;
* Параметры и локальные переменные — `camelCase`;
* FMOD events, сетевые теги и CDN-пути — `lowercase/snake_case`.

### Сериализация, ассеты и документация

* `.prefab`, `.unity`, `.asset` нельзя редактировать текстом. Менять их только через Unity Editor API/Inspector; сохранять GUID и `.meta`.
* Имя файла Unity-скрипта должно строго совпадать с классом. После создания/переименования проверять `MonoScript.GetClass()` в Editor (`dotnet build` этого не проверяет).
* `VolumeProfile.Add<T>()` создаёт component только в памяти; editor-код обязан вызвать `AssetDatabase.AddObjectToAsset()` до `SaveAssets()`.
* Документация в `docs/` содержит только автономные HTML с inline `<style>`, без Markdown и внешних зависимостей.

---

## 2. Карта проекта и структура сцен

*(Секция карты проекта находится на пересмотре)*

### Сцены проекта (Build Settings)

Четыре production-сцены (канонический порядок регистрации в Build Settings — см. `BuildSettingsFix.EnsureScenesInBuildSettings`):

1. **`Bootstrap.unity`** (Build Index 0) — `BootstrapLifetimeScope`, DontDestroyOnLoad-менеджеры.
2. **`Gateway.unity`** (Build Index 1) — гейт авторизации.
3. **`MainMenu.unity`** (Build Index 2) — только UI главного меню, без DI-графа.
4. **`MainGame.unity`** (Build Index 3) — весь `GameLifetimeScope`, DI-граф и геймплей. Offline-режим обеспечивает `DummyConnection`.

### Флоу загрузки и выгрузки сцен (SceneTransitionTicket)

* **Единственный путь перехода:** `BootstrapLifetimeScope.TransitionAsync(sceneName)`. На каждый переход создаётся `SceneTransitionTicket`, передаётся в дочерний скоуп через `LifetimeScope.EnqueueParent` + `Enqueue(builder => builder.RegisterInstance(ticket))`. Сериализованные `ParentReference` в сценах запрещены; поиск скоупа по загруженным сценам запрещён.
* **Handshake тикета:** дочерний composition root обязан вызвать `ticket.Attach(scene)` ровно один раз (второй attach — исключение) → `RequestActivation` → `MarkStartupReady` → `MarkPresentationReady`. Фаза хранится в `SceneTransitionPhase`, наружу публикуется единым `ISceneNavigator.TransitionChanged`; исключение подписчика не влияет на транзакцию.
* **Провал:** любое исключение старта закрывает тикет через `Fail(ex)` — Bootstrap отменяет переход, выгружает кандидат-сцену и публикует `TransitionChanged` с фазой `Failed`; UI возвращается в рабочее состояние с одной диагностической ошибкой.
* **Роли скоупов:** `BootstrapLifetimeScope` — application-сервисы + `ApplicationBootstrap`; `GatewayLifetimeScope`/`MainMenuLifetimeScope` — только typed references, контроллер и свой bootstrap; `GameLifetimeScope` — models, processors, factories, typed scene contract и `GameBootstrap`.
* **Выход в меню (Game → Menu):** Реализован через `BootstrapLifetimeScope.ReturnToMainMenu()`: disconnect → teardown (`GameLifetimeScope.PrepareForUnload`) → повторная загрузка `MainMenu`.
* **MainMenu живёт до готовности мира:** при входе в игру `MainMenu` остаётся загруженной и показывает loader, пока `GameManager.WorldReady` не опубликует готовность; только после этого тикет получает `MarkPresentationReady` и меню выгружается.
* **Контракт сцены:** типизированные serialized-ссылки (roots, `UIDocument`, `Services`, камера, робот игрока) на скоупе; `Services` авторствуется НЕактивным и активируется только после DI (`ActivateSceneServices`). Проверка — read-only `Fodinae/Architecture/Validate Production Scene Contracts` (`ProductionSceneContractValidator`), а также перед билдом. Никаких авто-починок сцены: сцена — данные автора, валидатор только сообщает о нарушениях. Переносить объекты и менять Build Settings разрешено только через Unity MCP/Editor API, не текстовой правкой YAML.

---

## 3. Архитектура: DI и жизненный цикл

### Контейнеры и скоупы (VContainer)

`CompositionRoot` и `SingletonMonoBehaviour` удалены. Используется штатный VContainer (vendor-код не модифицируется). Асинхронность — UniTask, межсистемная связь — через модели/event gateways и `Action`.

Регистрация двухуровневая:

* **`BootstrapLifetimeScope`** (`DontDestroyOnLoad`, `DefaultExecutionOrder -30000`): менеджеры, переживающие переходы сцен (`ConnectionManager`, `NetworkService`, `AudioSystem`, `ClientConfigManager`, `ClientAssetLoader`), плюс application-tier состояние (`LocalPlayerState`/`ILocalPlayerState` — публикуется игроком, читается сетевым слоем и UI).
* **`GameLifetimeScope`** (в сцене `MainGame`, `DefaultExecutionOrder -20000`): игровые сервисы; зарегистрирован как entry point `GameBootstrap`.
* **Регистрация компонентов:** `RegisterManager<T>` требует сериализованный типизированный контракт (`Core/Lifecycle/ManagerBinding`, список `_managerBindings` на скоупе) и регистрирует concrete reference через `RegisterComponent`. Поиск по именам и runtime fallback запрещены; отсутствие или дублирование binding — `SceneContractException`.
* **Заполнение контракта:** one-way editor-инструмент `Fodinae/Architecture/Populate Manager Contract` (`Editor/ManagerContractMigrator.cs`) читает вызовы `RegisterManager<T>(builder, "group")` прямо из `GameLifetimeScope.cs`, находит каждый менеджер в сцене и пишет `ManagerBinding`. Он ничего не чинит и не перемещает — только привязывает ссылки. Пустой и частичный контракт являются ошибками `ProductionSceneContractValidator`.

### Запрет на ручной `AddComponent` в `Configure`

**Запрещено создавать менеджеров вручную через `AddComponent` внутри `Configure`.**

`Configure` выполняется до сборки контейнера: прямой `AddComponent` мгновенно вызывает `Awake`/`OnEnable`, пока `[Inject]`-поля не заполнены. Это порождает критические гонки (резолв `UIDocument` из Bootstrap, захват меню-камеры в `Awake`, NRE в `Start`).

Порядок инициализации графа — в `GameStartupPipeline`; `GameBootstrap` только связывает pipeline с `SceneTransitionTicket`. Порядок читается в типизированном коде и охраняется линтером и тестами:

1. ожидание `ticket.WaitForActivationAsync()` (Bootstrap активирует сцену после attach);
2. `scope.ActivateSceneServices()` — активация авторственного неактивного `Services` root (Awake/OnEnable менеджеров выполняются только после DI);
3. infrastructure-фаза: config/сеть/процессоры/ассеты/terrain-подписки (fail-fast на недоступных подписках);
4. применение настроек: `TerrainRenderer.ApplyClientConfig` → `PostProcessController.EnsureVolumeSetup` → `LightingEngine.EnsureInitialized` → `SurfaceRenderer.ApplyClientConfig` (линтер охраняет применение каждой настройки на старте);
5. UI-сервисы: `GameManager.EnsureUISetup` → `PlayerHUDView.EnsureInitialized` → `InventoryView.EnsureInitialized`;
6. валидация обязательных шейдеров, compute-шейдеров и `ProjectDefaults`, затем `_connection.Connect()` — соединение не стартует при критической ошибке контракта;
7. `ticket.MarkStartupReady()`;
8. ожидание полной готовности мира (`GameManager.IsWorldLoaded` — серверная позиция, terrain, surface, lighting, ассеты) и завершения загрузки обязательных FMOD-банков; отсутствие банков фиксируется как `Degraded`, но не блокирует игру;
9. `scope.MarkReady()` + `ticket.MarkPresentationReady()` — только теперь Bootstrap выгружает предыдущую сцену.

> **Контракт:** добавление подсистемы = регистрация в `GameLifetimeScope.Configure` и включение в соответствующую фазу `GameStartupPipeline`. Этот список — краткое описание, код pipeline является источником истины.

### Регистрация зависимостей

* **Инстансы:** `MapStorage`, `InventoryModel`, `PlayerStatsModel`.
* **Менеджеры:** полный список живёт в `GameLifetimeScope.Configure` (`RegisterManager<T>(builder, group)`) и в `BootstrapLifetimeScope.Configure` — код есть источник истины. Каждый менеджер обязан лежать в своей группе `Services/<Group>/<Name>` в сцене.
* **Модели и gateways (DI-синглтоны):** `NetworkStatusModel` (ping/online из `StatusProcessor`, читается UI), `ChatEventGateway`, `WindowCommandStream` (пакеты → презентация окон через `ServerWindowPresenter`), `MapModeState`, `InputBlockState` (композиция `IInputBlocker`).
* Ambient session resolver отсутствует. Игровые зависимости получают через прямой `[Inject]`; `IObjectResolver` разрешён только в composition root и фабриках.
* `RegisterInstance` не инжектит зависимости в созданные вручную объекты; для scene-компонентов использовать `RegisterComponent`, для prefab/entity — `ISceneObjectFactory`.

---

## 4. Подсистемы клиента

### Сетевой стек, кэширование и авторизация

* Клиент получает от сервера лёгкое состояние (координаты и идентификаторы), а тяжёлые текстуры, спрайты и FMOD-банки загружает on-demand один раз.
* **Иерархия кэширования:** RAM (`AssetCache`, `CellTextureCache`) → диск (`PersistentAssetCache`, versioned manifest с ETag/length/SHA-256) → CDN/сервер. Рендеринг после загрузки выполняется локально.
* `NetworkService` / `ConnectionManager` — подписки, транспорт, авторизация и реконнект. `PacketHandler` — чистый диспетчер: связывает тип пакета с процессором и владеет временем жизни подписок; он не содержит UI, менеджеров сцены и состояния игрока. Логика пакетов — в `Networking/Processors/*` (обновляют модели, gateways и доменные сервисы: `WorldInitProcessor`, `AuthTokenProcessor`, `PlayerInfoProcessor`, `StatusProcessor` и т.д.).
* `DummyConnection` — оффлайн-транспорт. В `HappyPath` окна авторизации не вызывает: пермиссивно принимает VK-токены (`fdn_vk_*`), знакомые токены из `temporaryCachePath/server_tokens.json`, а для пустого/незнакомого токена сам выдаёт новый (первый вход без экрана; клиентский токен — в PlayerPrefs `AuthToken6`). Инъецируемый `IOfflineScenarioSettings` фиксирует на жизненный цикл один из детерминированных негативных сценариев: auth reject, disconnect during handshake, handshake timeout или world-init timeout.
* Процессоры пакетов обрабатывают: `world`, `map`, `chat`, `clan`, `audio`, `windows`, `inventory`, `stats`, `player`, `robots`, `packs`, `missions`, `config`.

### UI Toolkit, слои и оконная система

* **Управление UI:** Единственный источник обычного UI — `GameManager.SetupUI()` под выключенным `_uiRoot`; метод `AuthorizeUI()` активирует его (`FindAnyObjectByType` не видит неактивные объекты).
* **Packet UI:** Строится из `OpenWindowPacket` через `PacketUIBuilderFactory`. Сервер строго авторитетен при закрытии окон: нажатие ESC или кнопки UI шлёт запрос серверу, локально окно не скрывать — закрытие выполняется только по факту получения `CloseWindowPacket`. `WindowBinding` использует SmartFormat.
* **Компоненты интерфейса:**
* `Inventory`: модель / presenter / view, сетка 9×6 + хотбар.
* `HUD`: HP, энергия, баффы, авто-копка, Programmator.
* `Chat`: global, local, floating компоненты.
* `FPSCounter`: использует `UIDocument`, создание legacy `Canvas` запрещено.
* `MainMenu`: загружает `Resources/UI/MainMenu.uxml`; после сборки UI фиксированный `PanelSettings` нужно восстановить, иначе элементы могут отображаться, но не принимать события.

* **Слои UIDocument (контракт):**
* `MainMenu` UIDocument: `sortingOrder 100` (полноэкранный лоадер «спуска»).
* `MainGame` UIDocument: `sortingOrder 0` (находится ПОД лоадером — во время загрузки игровой UI скрыт, чтобы элементы HUD/миникарты не мелькали по отдельности).
* Серверные окна (`OpenWindowPacket` — auth, кланы, миссии) открываются в игровом UIDocument. При открытии такого окна `MainMenu.DismissDescentIfServerWindowOpened()` скрывает слой лоадера меню (сцена меню при этом НЕ выгружается до `OnWorldLoaded`). Не поднимать игровой `sortingOrder` выше меню и не скрывать игровой UI иначе.

#### Идиоматика UI Toolkit (обязательна для нового и переписываемого кода)

1. **Один источник стилей:** `PanelSettings.themeUss` → `FodinaeTheme.tss`, который `@import`'ит все `Resources/Styles/*.uss`. Запрещено в контроллерах вызывать `element.styleSheets.Add(Resources.Load<StyleSheet>(...))` и дублировать импортированные стили.
2. **Структура в UXML, а не в коде:** Статическая разметка (панели, кнопки, контейнеры) — строго в `.uxml` в `Resources/UI/`. В C# через `VisualTreeAsset.CloneTree()` и `tree.Q<T>("Name")` выполняется только привязка обработчиков и данных. Вызов `new VisualElement()` в коде допустим исключительно для динамических сеток/списков.
3. **Размер панели определяется PanelSettings:** Запрещено задавать `root.style.width/height` из `Screen.width/height` и использовать абсолютные координаты `top/left`. Рут и контейнеры растягиваются через `position: absolute; left: 0; right: 0; top: 0; bottom: 0` или `flex-grow: 1` в USS; выравнивание — flexbox (`align-items`, `justify-content`).
4. **Переключение видимости — `display: Flex/None`:** Применять через USS-класс или inline-стиль на существующем элементе UXML. Не добавлять/удалять оверлеи из иерархии каждый кадр и не переключать `pickingMode` ради прозрачности — z-порядок слоёв задаётся в UXML/USS.
5. **Однократная сборка UI:** Сборка выполняется один раз (под guard-флагом в `OnEnable`/`Start`), а не при каждом показе. Подписки на `clicked`/`RegisterCallback` делать один раз; отписываться в `OnDisable`.
6. **Ненадежность `Screen.width` в редакторе:** До первого layout панель может вернуть `NaN` (причина некликабельности). Диагностировать по `root.layout` (размер ≠ NaN) и `element.worldBound`. После `CloneTree` не модифицировать `root` кроме `Add(tree)`.
7. **Полное отключение клавиатурной навигации UI:** `EventSystem` и `InputSystemUIInputModule` не создаются ни в одном скоупе (в проекте 100% UI Toolkit, uGUI отсутствует). `PlayerHUDView.InitializeHUD` безусловно подавляет навигационные события (`NavigationMoveEvent`, `NavigationSubmitEvent`, Tab в `KeyDownEvent`) через TrickleDown. Стрелки, WASD, Enter и Tab не должны перемещать фокус по кнопкам. Всё UI-взаимодействие — только мышью.
8. **Экранные координаты в панель — строго через `RuntimePanelUtils.ScreenToPanel`:** `PanelSettings` настроен на `ScaleWithScreenSize` (reference 1200×800). Ручной пересчёт Y (`new Vector2(x, Screen.height - y)`) не учитывает scale и даёт промах по координатам (например, при 1920×1080 клики по миникарте проваливались в мир и слали `ClickCellPacket`, вызывая ложное движение/копку робота).

### Рендеринг планеты в главном меню (Fodinae/UI/PlanetSurface + PlanetAtmosphere) — контракт фотореализма
* **Цель:** физически правдоподобная планета, снятая камерой с орбиты. Никакого масляного, жидкого, пластикового, мраморного или стилизованного вида. Сцена — одна сфера с минимальным GPU-бюджетом; камера только облёт/подлёт (максимум 3.6 радиуса), посадка не рендерится.
* **Источник истины вида:** дефолты в `Assets/Shaders/UI/PlanetSurface.shader` и `Assets/Shaders/UI/PlanetAtmosphere.shader`. `.mat`-ассеты (`PlanetSurface.mat`, `PlanetAtmosphere.mat`) правятся только через Unity Editor/Inspector и обязаны оставаться в тех же диапазонах, что и дефолты (см. ниже).
* **Обязательные диапазоны параметров поверхности:** `_Roughness` 0.65–0.9 (матовая сухая порода), `_ReliefStrength` ≤ 0.5 (рельеф не должен гнуть освещение в видимые «волны» с орбиты), `_MagmaIntensity` ≤ 2.0, `_PoolIntensity` ≤ 2.0 и `_PoolGloss` ≤ 512 (никаких мокрых/глянцевых пятен), `_DetailStrength` ≤ 0.12, `_SunIntensity` 4.0–5.0.
* **Запрещено:** один процедурный шум, питающий одновременно albedo + roughness + normals + emission; Voronoi/cellular как непосредственно видимый паттерн поверхности; glossy/wet-ответ; marble/liquid domain-warp как видимый узор; художественное усиление контраста вразрез с физикой света.
* **Порядок работы над видом:** сначала скриншот текущей сцены, затем изменение ОДНОЙ параметрической оси, затем контрольный скриншот. Одновременная правка нескольких осей в шейдере и материале запрещена — именно она повторно воспроизводила «масляный» вид.
* **GPU-бюджет:** рендер планеты должен оставаться пренебрежимым. Попиксельная процедурная генерация в рантайме запрещена: поля запекаются `PlanetFieldBaker` в кубические карты и в шейдере только сэмплируются (≤ ~5 texture samples на пиксель). Запрещены tessellation, displacement, raymarching, volumetrics и per-pixel noise в горячем пути.

### Мир, чанки и координаты

* **Система координат:** Серверные координаты — левый верхний угол `(0, 0)`, ось X направлена вправо, ось Y — вниз (Top-Left). Все преобразования производить исключительно через `CoordinateUtils`, всегда учитывая `MapManager.WorldHeight`.
* **Хранение данных мира:** `MapManager` принимает `WorldInitPacket`/`MapRegionPacket`. `MapStorage` хранит чанки 32×32 (`persistentDataPath/*.mapb`) и оповещает рендерер через `OnCellChanged()`. Рендеринг ожидает `MapStorage.IsReady = true` (наступает после `WorldInitPacket`). В `DummyConnection` конфигурации клеток `_cellConfigs` должны создаваться ДО `WorldInitPacket`.
* **Стриминг:** `WorldLayer<T>` реализует дисковый streaming, LRU RAM-кэш, RLE и append-only запись с компактификацией. Текстуры загружаются из файловой системы, а не из Resources/Addressables (при билде папка `Textures/` копируется вручную).

### Рендеринг террейна и закартовых поверхностей

* **TerrainRenderer:** Отрисовывает видимый мир единым mesh (7 UV-каналов, sorting order `-1000`) и применяет дифференциальные обновления.
* **SurfaceRenderer:** Рендерит закартовые поверхности. Регистрируется и резолвится через `GameLifetimeScope` до этапа startup validation. `SceneSetup` только загружает обязательные текстуры; вручную создавать второй `SurfaceRenderer` запрещено.
* **Кэширование геометрии:**
* `TerrainCellCache` привязан к мировой сетке с шагом 8: при перемещении сохраняется область пересечения и достраиваются только новые полосы.
* Zoom-кэш квантуется по 32 клеткам, сжатие происходит через 0.4 с после стабилизации (без покадровых аллокаций).
* Зависящая от камеры геометрия (`SurfaceRenderer` и аналоги) содержится в квантованном coverage-кэше с запасом. Запрещено перестраивать mesh на каждом кадре плавного движения/зума по точному сравнению `transform.position`, `orthographicSize` или `aspect`.

* **Геометрия верхней поверхности мира (авторский контракт):**
* От верхней границы мира идут: слой `Transit` (высота `2` world cells, ширина тайла `32`), затем слой `Perspective` (высота `2`, ширина тайла `5`); выше располагается фон/небо.
* Текстуры повторяются по X и зажимаются (clamp) по Y.
* Красноскал бесконечен слева, справа и снизу карты, но НЕ сверху. Запрещено подменять эти размеры размерами PNG или границами камеры.

* **Анимации и текстуры тайлов:**
* `AnimationContainerDecoder` поддерживает форматы PNG/GIF/WebP; анимация тайла не изменяет его окклюзию или emission.
* `CellConfigurationPacket.Animation` задаёт shader-анимацию и **не означает**, что исходный PNG является frame-атласом. Только `FrameOffset > 0` задаёт высоту кадра в клетках; `FrameOffset == 0` валиден для UV/color-анимаций (например, Lava использует animation type `4`, серверную скорость и `FrameOffset = 0` для UV-скролла tiled sheet). Запрещено выводить число кадров из условия `Animation != None` и занулять `AnimationSpeed` при отсутствии атласа.
* Production runtime-текстуры создаются/декодируются строго через `RuntimeTextureFactory`: канонический формат `RGBA32`, без mipmaps, с явными color space, filter и wrap modes. Прямые вызовы `new Texture2D(...)` и `LoadImage(...)` вне фабрики запрещены.
* Копирование в terrain atlas предварительно проверяет совпадение размеров и graphics format. Случайная диагностическая текстура при отсутствии ассета — обязательный функционал, не удалять.
* Материал террейна не затемнять градиентами (`u-v`/`u+v`) или relief/connectivity; затемнение производится исключительно через `_WorldLightTexture`.

### Игрок, ввод и блокировки

* **Позиция:** Единственный источник истины позиции игрока — `PlayerMovementController.Position` (`Vector2Int`, server Top-Left). Возврат устаревших полей `ClientPosition` и `ServerPosition` запрещён.
* **Управление (`PlayerInputHandler`):** WASD/стрелки — передвижение, Space — копка, E — авто-копка, L — агрессия, Shift — бег. Валидация: локально по `Passable`, на сервере через `MovePacket`.
* **Обработка кликов:** `PlayerInteractionController.HandleMouseClick` отправляет `ClickCellPacket` только если `IsPointerOverUI` вернул `false` (с обязательным использованием `RuntimePanelUtils.ScreenToPanel`).
* **Тайминги и механики:** `DigCooldown = 0.3f` блокирует повторную копку и движение. Направление задаётся `_lastSentDirection` (по умолчанию `Direction.Down`). В Dummy SFX пустой клетки отправляется до проверки на `Empty`.
* **Блокировка ввода (`IInputBlocker`):**
* Единственная реализация — `UI/InputBlockState` (UI-слой): композиция `ChatInput.IsFocused || ServerWindowPresenter.HasOpenWindows || IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen`. `PacketHandler` и сетевой слой не реализуют блокировку ввода.
* Фокус чата входит в блокировку: пока открыт ввод текста, движение, копка, геймплейные клавиши и камера заблокированы. Потребители (`PlayerMovementController`, `PlayerInteractionController`, `CameraFollow`, HUD) инжектят `IInputBlocker` — статический доступ к UI-синглтонам извне `Assets/Scripts/UI` запрещён линтером.
* **Локальный игрок:** единственный типизированный источник — `ILocalPlayerState` (application-tier, `LocalPlayerState`, публикуется `PlayerMovementController`). Статические `PlayerMovementController.LocalPlayer`/`OnLocalPlayerSpawned` удалены; использование запрещено линтером.
* Клавиша Enter отправляет сообщение чата даже при `IsInputBlocked` (условие в `GlobalChatUI.Update` проверяет `ChatInput.IsFocused`).
* Клавиша ESC в `PauseMenu` сначала передаёт управление в Программатор, затем шлёт серверный запрос на закрытие верхнего окна.

### Освещение (Lighting Pipeline & Ambient Occlusion)

* **Активный конвейер:** GPU Radiance Cascades из `WorldLighting.compute`:
`LightingMaterialField` / `EmissionField` → `SolveCascade` → `ResolveDirect` → `SolveDiffuseBounce` → `CompositeLighting`.
Старые проходы (SDF, raymarch, AO-neighbour, blur, CPU sweep, GPU readback, runtime fallbacks) запрещены к возврату.
* **Поля материалов и излучения:**
* Единственный источник эмиссии — серверный флаг `CellConfigProperties.Glowing` (в Dummy выставлять его же), а не `CellType` или клиентские списки. Цвет берётся из `CellConfigurationPacket.Color`.
* `MaterialField.rgb` — surface albedo для одного diffuse bounce; `MaterialField.a` — физическая occupancy.
* `EmissionField` содержит излучение.
* Альфа атласа, visual blending, анимации и песок поверх валуна не изменяют физическую массу. Соседние `DropsShadow`-клетки формируют непрерывный контур без внутренних границ.

* **Физика света:**
* Ослабление света рассчитывается по Beer–Lambert extinction; итоговая величина — surviving fraction (пропускание). Direct radiance, transmission и AO — строго раздельные величины (поглощение нельзя называть «AO»).
* Receiver self-skip разрешён только внутри исходной клетки; при выходе наружу соседняя масса снова ослабляет свет.

* **Contact / Cavity Ambient Occlusion (`LIGHTING_AO_PLAN.md`):**
* Legacy `nearSolidPath` pseudo-AO удалён. Реализуется отдельный полноразрешённый contact/cavity AO из occupancy.
* AO создаёт слабую тень у открытой границы, усиливается в 90° углах и щелях, не создаёт внутренних швов в массиве пород. Влияет **только** на ambient и diffuse bounce; direct radiance и emission не модифицирует.
* Данные AO хранятся отдельно в persistent `RHalf`, упаковываются в alpha-канал итоговой `_WorldLightTexture` и пересчитываются **только** при: изменении geometry revision, смене региона/размера поля или изменении настроек AO. Смена источников света пересчёт AO не триггерит.
* Ambient добавляется ровно один раз; Eigengrau не относится к lighting reconstruction.

* **Сетка и масштабирование Lighting Field:**
* Статическое поле геометрии растеризуется фактическим terrain mesh через единый command buffer; динамические источники добавляются через GPU draw. Не загруженные или закартовые клетки не должны попадать в submesh indices или подменять cell type `0`.
* Размер lighting field обязан быть **строго целым числом** текселей на клетку (`4`, `3`, `2`, `1`). При нехватке видеопамяти выбирается максимальный помещающийся **целый** масштаб — дробное сжатие запрещено.
* Lighting region смещается по сетке шагом 8 клеток. Дробный `pixelsPerCell` сбивает фазу текселей/проб при reanchor, делая освещение зависимым от направления движения камеры.
* Повторное использование текстур полей допускается только при точном совпадении с размером `gridSize * integerScale`.
* Внешний контракт `LightingEngine`: `_WorldLightTexture`, `_WorldLightRect`, `InvalidateCell`.
* Профили качества (`Low`/`Medium`/`High`/`Ultra`) меняют только точность/стоимость шагов существующего алгоритма (скрытые fallback-коэффициенты и отдельные пресеты запрещены). Normal map и Lambert не реализованы.

### Рендер-архитектура, камеры и пост-процессинг

* **Разрешение игровой камеры:**
* Игровую камеру DI-компоненты получают через **инъекцию `IGameplayCamera`** (`Core/Interfaces/IGameplayCamera.cs`, регистрируется в `BootstrapLifetimeScope` поверх persistent application-камеры), вызов `Camera.main` напрямую **запрещён** (ищет по тегу во всех сценах, а `MainMenu` живёт параллельно до завершения загрузки). Переиспользование `IGameplayCamera` запрещено из Render Feature: единственное оставшееся статическое место — `PostProcessRendererFeature`, которому нельзя сделать field-инъекцию (ScriptableRendererFeature), он определяет игровую камеру через `cameraData.camera == _mainCamera`.
* Потребители `IGameplayCamera`: `PostProcessController`, `TerrainRenderer`, `SurfaceRenderer`, `FloatingChatManager`, `FloatingChatBubble`, `MissionArrowUI`, `InGameDebugOverlay`, `MapManager`, `DiagnosticRunner`, `Robot`, `PlayerInteractionController`.
* Статический хелпер `GameplayCamera.Resolve()`/`ResolveIn(scene)` — только для независимых от VContainer мест (Render Feature); в новых DI-компонентах использовать `IGameplayCamera`.

* **Камеры Главного Меню:**
* Все камеры меню обязаны иметь тег `Untagged` (выставляется в `BuildMenuSceneryRig`); тег `MainCamera` запрещён.
* Меню не рисует в экран мировую геометрию: звёзды генерируются через `MenuStarfield` (`Graphics.Blit` в RT, без меша и камеры), сцены планеты/станции рендерятся `MenuSceneryCamera` в RT. UI отображает обе текстуры как `Image`. Старый квад звёзд на камере (`StarfieldQuad`/слой `MenuBackdrop`) запрещён — любая камера с `cullingMask: Everything` отрисует его поверх игры.
* Риг `MenuScenery` размещён по координатам `(0, 20000, 0)` (far plane камер равен 1000, попадание исключено). Дополнительно слой `MenuScenery` исключается из игровой камеры в `PostProcessController` (`cullingMask &= ~MenuLayerMask()`, `volumeLayerMask = ~MenuLayerMask()`).

* **Порядок глубин (Depth) камер меню и URP Overlay:**
* `MenuSceneryCamera` обязана иметь `depth = -101` (строго ниже `MenuDisplayBackdropCamera` с `depth = -100`).
* URP рендерит оверлей UI Toolkit только после последней base-камеры, выводящей изображение в экран (`rendersOverlayUI && isLastBaseCamera`). Так как `MenuSceneryCamera` рендерит в RT, оказавшись последней по глубине, она сломает рендер UI Toolkit (чёрный экран).
* `MenuDisplayBackdropCamera` (`depth = -100`, `cullingMask = 0`, Solid Color) обязана быть последней base-камерой меню.
* Игровая камера (`depth = -1`) становится последней base-камерой после загрузки мира и держит свой UI-оверлей. Запрещено поднимать глубину Scenery-камеры выше -100 или опускать Backdrop-камеру до 0.

* **RenderTexture меню:** `targetTexture` камеры сериализуется как `null`; RT создаётся и управляется исключительно через `MenuSceneryController` (1800×1800, mip-цепочка + trilinear). Встроенный в сцену RT (900×900 `MenuSceneryRT_Premultiplied`) удалён.
* **Настройки конвейера рендера:**
* `Renderer2D.asset` содержит ровно одну активную `PostProcessRendererFeature`. Post-process применяется к базовой камере.
* World-space UI на слое `UI` рисуется отдельной Overlay-камерой `WorldUICamera` без пост-процессинга. UI Toolkit / Screen Space Overlay рендерится поверх.
* HDR-буфер (`supportsHDR`) включён для lighting/bloom. HDR output на дисплей управляется переключателем в PauseMenu: `HDROutputSettings.RequestHDRModeChange(true/false)` через `DisplayManager.SetHDREnabled`. Финальное сжатие диапазона делает **собственный тонмап AgX** в `PostProcess.compute` (`ToneMapAgX`), а не URP-овский `Tonemapping` override: последнего в `PostProcessVolumeProfile` нет. **Тонмап безусловен и работает в обоих режимах вывода**; различается только белая точка: при SDR её берут из `ColorGradingComponent`, при `isHDROutputActive` считают как пик дисплея, делённый на paper white (`DisplayManager` отдаёт калибровку в `PostProcessRenderPass.SetDisplayCalibration`). Ни настройки «включить тонмап», ни тира «постпроцесс выключен», ни отладочного байпаса, ни гейта `IsEnabled` не существует, и досрочного выхода прохода по набору эффектов тоже нет: без кривой всё ярче белой точки клиппится в плоский белый, то есть выключённый постпроцесс давал не дешёвый кадр, а неверный. Держится проверкой `checkToneMapMatrices`. Тиров `Essential`/`Full` больше нет — перечисление `PostProcessQualityMode` удалено, стек одинаков на всех пресетах. Внутренний HDR-буфер (`camera.allowHDR`) остаётся включённым всегда — освещение и bloom рассчитываются в HDR вне зависимости от режима дисплея.
* **Матрицы тонмапа:** референс AgX — GLSL, где `mat3(...)` заполняется по столбцам, а HLSL `float3x3(...)` — по строкам. Литералы, перенесённые один в один, дают транспонированную матрицу, и нейтрали красятся. Инвариант: сумма каждой строки обеих матриц равна 1.0; держится проверкой `checkToneMapMatrices` в `scripts/check-architecture.js`. Второй инвариант того же места: кривая AgX выдаёт display-encoded значение, а camera target линеен и кодируется ещё раз при финальном блите, поэтому `ToneMapAgX` обязан завершаться `pow(color, 2.2)`. Без этого возврата кодирование идёт дважды и кадр становится молочным — та же проверка следит и за этим.
* **Motion Blur:** строит вектор скорости (velocity) только для удалённых `Robot` с компонентом `MotionBlurTag`. Локальный игрок исключается. Передаются реальные текстуры спрайтов и матрицы GPU; delta при телепортации сбрасывается.

### Звуковой движок (FMOD)

* `AudioSystem` и `FmodAudioBackend` базируются на FMOD Studio C++ Engine.
* FMOD-проект расположен в `FodinaeAudio/FodinaeAudio.fspro`.
* Банки скачиваются через `ClientAssetLoader`, кэшируются на диске и загружаются через `loadBankFile`; feature-банки подключаются и выгружаются on-demand.
* 3D-звук позиционируется нативно через `AttachInstanceToGameObject`. Зоны используют Snapshots и глобальные параметры.
* Иерархия шин: `Master`, `SFX`, `Music`, `Voice`, `Ambience`, `UI`.
* Базовые вызовы: `Play2D`, `PlayAttached`, `PlaySnapshot`, `SetGlobalParameter`, `SetBusVolume`.
* `ServerAudioEventManager` принимает `SFXPacket`, инициирует 3D-звук и порождает визуальное событие.

### Программатор (`ProgrammatorGrid`)

* Визуальный редактор алгоритмов робота: Список программ → Сетка → Действия (Save / Run / Stop).
* Данные сессионные (`_programItems` хранятся в RAM); единственный сохраняемый файл — `programmator.json` (через `JsonUtility`). Кнопки Run/Stop на текущем этапе визуальные.
* **Геометрия сетки:** 16×12 ячеек, `CELLSIZE = 32`, `CELL_GAP = 2`.
* Ширина контейнера: 608 px (рассчитывается по формуле `COLS * (CELLSIZE + CELL_GAP * 2 + 2f)`, где `+2f` обязателен из-за border).
* Ширина панели: 648 px.

* **Иерархия элементов:** `_popup` содержит `dimmer`, `_programListPanel`, `_panel`; диалог создания `_createDialog` представляет собой абсолютный overlay.
* **Навигация ESC:**
* Из режима сетки — возврат к списку с автоматическим сохранением;
* Из списка программ — закрытие окна программатора;
* Диалог создания закрывается только по кнопкам «×» или «Отмена».

---

## 5. Свод критических инвариантов и нюансов

1. **Готовность рендера:** Рендеринг ждёт `MapStorage.IsReady = true`, выставляемое после `WorldInitPacket`.
2. **Конфигурации клеток:** В `DummyConnection` структуры `_cellConfigs` и мок-данные должны быть инициализированы строго до `WorldInitPacket`, иначе `MapManager` не сможет обрабатывать разрушение клеток.
3. **Ориентация осей:** Постоянно контролировать Top-Left серверные координаты и инверсию Y относительно `WorldHeight`.
4. **Хранение ассетов:** Текстуры не хранятся в `Resources`; при сборке билда папка `Textures/` копируется во внешнюю директорию.
5. **Инъекция в существующие объекты:** `RegisterInstance` не выполняет инъекцию зависимостей автоматически — для ручных объектов вызывать `resolver.Inject()`.
6. **Резолв зависимостей в Lifecycle-методах:** Не обращаться к контейнеру из `Awake`/`OnEnable`/`Start`. Scene-компоненты получают зависимости через `[Inject]`, а запуск выполняется явным entrypoint после сборки scope.
7. **Безопасный Teardown окон:** При закрытии/уничтожении серверных окон (`Dispose`/`OnDestroy`) возможна гонка с выгрузкой сцены (когда `UIDocument` уже уничтожен). Операции очистки (`rootVisualElement.Remove`) оборачивать в null-check и блок `try/catch` — ошибки очистки UI не должны прерывать `OnDestroy`.
8. **Ограничения CSS/USS:** UI Toolkit не поддерживает функцию `calc()`: расчетные значения вычисляются заранее или задаются inline-стилем из C#.
9. **Свойства террейн-анимаций:** Шейдерная анимация и покадровый атлас не связаны: `AnimationSpeed` работает и для одиночного кадра; значение `FrameOffset = 0` является валидным и не должно трактоваться как ошибка.
10. **Инварианты системы ввода:** EventSystem отсутствует, навигация с клавиатуры в UI отключена, блокировка ввода — только через `IInputBlocker` (`InputBlockState`), перевод координат мыши — только через `ScreenToPanel`. Нарушение ведёт к багам спонтанного движения/копки персонажа.

---

## 6. Workflow, диагностика и оптимизация

* **Кэш Unity никогда не является причиной дефекта:** Запрещено списывать баги на `Library/`, кэш шейдеров, кэш импорта или layout-кэш редактора. Причина всегда кроется в исходном коде, сериализованных данных, конфигурациях или runtime-состоянии. Очистка кэша не признаётся решением проблемы.
* **Перекомпиляция — не универсальное объяснение:** Запрещено оправдывать баги фразами «Unity не перекомпилировал скрипты» или «нужно обновить домен». Сначала проверяются реализация, сериализованные ссылки, свойства инспекторов и логи. Проблема со сборкой может указываться только как доказанный блокер, если бинарный код гарантированно разошёлся с исходным.
* **Компиляция ≠ работоспособность игры:** Успешный `dotnet build` лишь подтверждает корректность типов и синтаксиса. Запрещено судить о работоспособности проекта только по отсутствию ошибок компилятора. Поведение проверяется исключительно прогоном сценариев в Play Mode или через Unity MCP.
* **Запрет на перекладывание тестов на пользователя:** Если доступен Unity MCP, агент обязан проводить диагностику самостоятельно: запускать Play Mode через MCP, активировать Debug View (`SetDebugView`), инспектировать консоль (`get_console_logs`), состояние сцены и объектов (`get_gameobject`, `get_scene_info`), запускать тесты (`run_tests`). Запросы к пользователю вида «запусти сам и проверь» допустимы только при исчерпании возможностей MCP с описанием конкретного блокера.
* **Разрешение экрана и Retina — не оправдание плохой производительности:** Запрещено оправдывать падение FPS высоким разрешением, Retina-экранами или размером окна Game View. 2D-песочница обязана выдавать стабильный высокий фреймрейт на любом стандартном разрешении. Причину искать в алгоритмической сложности, избыточных вызовах и неэффективной работе с CPU/GPU.
* **VSync и частота обновления монитора — не причина спайков:** Запрещено списывать просадки производительности и долгие кадры на герцовку монитора или вертикальную синхронизацию. Анализу подлежит исключительно «чистое» время выполнения алгоритмов и аллокации памяти.
* **Запрет на маскировку просадок FPS ограничением частоты:** Запрещено вводить искусственные FPS-caps, пропуск кадров (frame skipping), искусственные задержки, троттлинг или снижение частоты тиков симуляции/рендера для видимого «исправления» нагрузки. Оптимизация должна сокращать реальный объём работы за один кадр (убирать лишние rebuild/upload геометрии, аллокации, обходы коллекций и дублирующие расчёты), сохраняя покадровое обновление везде, где оно заложено архитектурой.
* **Проверка исходных намерений пользователя перед правкой настроек:** Прежде чем заявлять о «баге в проводке настроек», необходимо выяснить, не были ли параметры выставлены пользователем вручную. *(Прецедент: оверлей отображал профиль Ultra — `px/cell 4`, `max steps 16`, атлас `1280²×4`. Был сделан ошибочный вывод о поломке логики в `LightingQualityResolver`, хотя пользователь намеренно выставил Ultra для стресс-теста).*
* **Запрет правок по непроверенным гипотезам:** Код модифицируется только после воспроизведения или строгого подтверждения дефекта по кодовой базе. Изменения «наугад» в горячих путях рендера или цепочках конфигурации ломают осознанное поведение системы. Недоказанная идея должна сначала озвучиваться как гипотеза с планом проверки.
* **Использование встроенного диагностического инструментария:** Перед созданием новых логов/счётчиков использовать готовые тулзы:
* `F3`: оверлей `InGameDebugOverlay` (FPS, frametime, замеры CPU Meshing / FloodFill, счётчики ребилдов террейна, трассировка каскадов в ray-steps и atlas taps, размер управляемой кучи);
* `F3` (детальный режим `FPSCounter`): отображение `FrameProfiler.GcAllocPerFrameBytes` в формате `GC: X KB/f` (аллокации главного потока за кадр);
* `F4` / `F5` / `F6` / `F7` / `F8`: покадровое переключение и изоляция подсистем (`BypassLightingCompute`, `BypassPostProcessPass`, `BypassTerrainDraw`, `BypassCpuMeshRebuild`, динамический свет) для мгновенной бисекции источников просадок за одну сессию.



ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
ЗАПРЕЩЕНО ГОВОРИТЬ ПРО ВСИНК ВООБЩЕ. НИКОГДА.
