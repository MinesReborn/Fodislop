# Стандарт сцен и сервисов Kern

Обязателен для всех сцен: `Bootstrap`, `Gateway`, `MainMenu`, `MainGame`. Код — источник истины; если документ расходится с кодом, правится одно из двух в том же изменении.

## 1. Что может быть GameObject'ом

GameObject — только то, у чего есть **место в мире или на экране**: рендер (`MeshRenderer`, `SpriteRenderer`, `Camera`, `Volume`, `UIDocument`), transform-иерархия, физика, звук с позицией, объект, который создаётся и уничтожается как сущность (робот, здание, эффект).

Всё остальное — **обычный C#-класс в контейнере**:

| Было | Стало |
|---|---|
| `class XManager : MonoBehaviour` без рендера и transform | `sealed class XManager` + `builder.Register<XManager>(Lifetime.Singleton)` |
| `Awake`/`Start` | конструктор / `IStartable.Start` |
| `Update` / `LateUpdate` / `FixedUpdate` | `ITickable` / `ILateTickable` / `IFixedTickable` |
| `OnDestroy` | `IDisposable.Dispose` (вызывает контейнер при уничтожении scope) |
| `[Inject]` в поле | параметр конструктора (primary constructor) |
| `Destroy(obj)` своих объектов | через `ISceneObjectFactory` / явный владелец |

Регистрация сервисов с тиком — `builder.RegisterEntryPoint<T>()` (он же регистрирует `ITickable`, `IStartable`, `IDisposable`), при необходимости `.AsSelf().AsImplementedInterfaces()`.

## 2. Сцена

- Ровно один корень — `LifetimeScope` сцены (уже стережёт `ProductionSceneContractValidator.ValidateSingleRoot`).
- Под корнем только объекты из §1. Пустых «папок» без смысла нет; `Runtime/*` — контейнеры для сущностей, создаваемых в игре.
- `Services/<группа>` содержит только компоненты, которым действительно нужен GameObject (рендер, камера, UI-документ). Каждый такой компонент — в `ManagerBinding`. Чистый C# туда не кладётся.
- Отладочное (`InGameDebugOverlay`, окна F1) — не в продовой иерархии как обязательный сервис; включается кодом.

## 3. Кадр

- Порядок кадра задаётся явно: тики контейнера (`ITickable` в порядке регистрации), а не порядком скриптов на объектах и не `DefaultExecutionOrder`, где можно без него.
- Никакого опроса «не изменилось ли что» в `Update`, если есть событие или `VisualElement.schedule`.
- В тике — ноль аллокаций на установившемся режиме: без LINQ, строковой интерполяции, лямбд с захватом, `new` коллекций.

## 4. Объекты, создаваемые в игре

- Создаются только через `ISceneObjectFactory` под свой `Runtime/*`-контейнер.
- Часто создаваемые и уничтожаемые (эффекты, всплывающий текст, звуковые события) — из пула.
- Сущность с собственным `Update` — исключение; массовые сущности обновляет один системный тик (как `WorldEntityBatchRenderer`).

## 5. Как переносить сервис

1. Удалить объект сервиса со сцены и **сразу сохранить сцену**.
2. Переписать класс по §1, заменить `RegisterManager<T>` на `Register`/`RegisterEntryPoint`.
3. `Kern/Architecture/Populate Manager Contract` — привязки собираются из вызовов `RegisterManager<T>` в коде, поэтому после шага 2. Меню правит уже открытую сцену и сохраняет её.
4. Перекомпилировать, проверить консоль и **файл сцены на диске**: GUID удалённого скрипта не должен встречаться в `.unity`. Если класс перестал быть `MonoBehaviour`, а объект остался в файле, Unity пишет «missing the class attribute 'ExtensionOfNativeClass'» и «references runtime script in scene file. Fixing!».
4. Сцены и префабы — только через Unity Editor, никогда текстом.

## 6. Статус переноса MainGame

| Сервис | Статус |
|---|---|
| `ServerConfig` | перенесён: `Register<ServerConfig>` |
| `UIInputManager` | перенесён: `Register<UIInputManager>` |
| `PacketHandler` | перенесён: `RegisterEntryPoint`, `IStartable` + `IDisposable`, без `partial` |
| `DisplayManager` | перенесён: `RegisterEntryPoint`, `IStartable` |
| `BuildingManager`, `RobotManager` | перенесены: `Register`, сущности через `ISceneObjectFactory` |
| `GameManager` | перенесён: `RegisterEntryPoint`, `ITickable` + `IDisposable` |
| `MapManager` | в очереди: гизмо и превью для редактора выносятся отдельно, `Update` → `ITickable`, пауза/выход → `IDisposable` + хук приложения |
| `WorldTextureManager` | в очереди: сериализованные ссылки на ассеты → настройки через `ScriptableObject` или `RegisterInstance` |
| `WorldBackgroundSetup`, `TerrainRenderer`, `SurfaceRenderer`, `WorldEntityBatchRenderer`, `CameraFollow`, `LightingEngine`, `VfxPool` | остаются компонентами (рендер/камера), тик — к явному порядку §3 |
| UI-контроллеры (`PlayerHUDView`, `InventoryView`, `PauseMenu`, …) | в очереди: презентеры поверх одного `UIDocument` |
