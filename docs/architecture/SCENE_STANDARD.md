# Стандарт сцен и сервисов Kern

Обязателен для всех сцен: `Bootstrap`, `MainMenu`, `MainGame` (вход и онбординг — слой меню `MenuGate`, не сцена). Код — источник истины; если документ расходится с кодом, правится одно из двух в том же изменении.

## 1. Что может быть GameObject'ом

GameObject — только то, у чего есть **место в мире или на экране**: рендер (`MeshRenderer`, `SpriteRenderer`, `Camera`, `Volume`, `UIDocument`), transform-иерархия, физика, звук с позицией, объект, который создаётся и уничтожается как сущность (робот, здание, эффект).

Всё остальное — **обычный C#-класс в контейнере**:

| Было | Стало |
| --- | --- |
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

- Порядок кадровых сервисов задаётся явно порядком регистрации VContainer-тиков (`ITickable`). Между scene-компонентами порядок задаётся только там, где production path от него зависит: например, `TerrainRenderer` (`DefaultExecutionOrder(100)`) публикует committed frame до `LightingEngine` (`DefaultExecutionOrder(200)`). Не полагайся на случайный порядок компонентов на GameObject.
- Никакого опроса «не изменилось ли что» в `Update`, если есть событие или `VisualElement.schedule`.
- В тике — ноль аллокаций на установившемся режиме: без LINQ, строковой интерполяции, лямбд с захватом, `new` коллекций.

## 4. Объекты, создаваемые в игре

- Создаются только через `ISceneObjectFactory` под свой `Runtime/*`-контейнер.
- Часто создаваемые и уничтожаемые (эффекты, всплывающий текст, звуковые события) — из пула.
- Сущность с собственным `Update` — исключение; массовые сущности обновляет один системный тик (как `WorldEntityBatchRenderer`).

## 5. Как переносить сервис

Этот workflow с изменением сцены применим только когда текущая задача явно
разрешает нужную Unity Editor/API операцию. Сам рефакторинг C# такого разрешения
не подразумевает.

1. В разрешённом Editor/API workflow удалить объект сервиса со сцены и сохранить сцену.
2. Переписать класс по §1, заменить `RegisterManager<T>` на `Register`/`RegisterEntryPoint`.
3. `Kern/Architecture/Populate Manager Contract` собирает привязки из вызовов
   `RegisterManager<T>` в коде. После шага 2 мигратор обновляет открытую сцену;
   сохранить результат через Unity API.
4. Unity-компиляцию и проверку консоли выполнять только в рамках явно
   разрешённой Unity-операции. Проверить, что GUID удалённого скрипта не
   остался в сцене: если класс перестал быть `MonoBehaviour`, но объект остался,
   Unity сообщает о missing `ExtensionOfNativeClass` и ссылке на runtime script.
5. Сцены и префабы изменять только через Unity Editor/API, никогда текстом.
   Если Unity-операция не разрешена, ограничиться исходным кодом и отметить
   проверку сцены/runtime как невыполненную.

## 6. Текущая регистрация сценовых сервисов

`GameLifetimeScope.RegisterManager<T>` ограничен `MonoBehaviour`: он получает
компонент из сериализованной группы через `SceneBindings.ResolveTypedBinding<T>`
и регистрирует уже существующий компонент. Группы `World`, `Rendering`, `Audio`
и `UI` — часть контракта сцены, а не список сервисов, ожидающих переноса в
обычный C#.

| Регистрация в `MainGame` | Примеры текущих владельцев |
| --- | --- |
| Сценовые компоненты через `RegisterManager<T>` | `MapManager`, `WorldTextureManager`, `TerrainRenderer`, `WorldBackgroundSetup`, `WorldEntityBatchRenderer`, `VisualEffectPool`, `LightingEngine`, `SurfaceRenderer`, `CameraFollow`, `PlayerHUDView`, `InventoryView`, `PauseMenu`, `MinimapController`, `WorldMapController`, `WorldMapRenderer` |
| Обычные singleton-сервисы через `Register<T>` | `ServerConfig`, `UIInputManager`, `BuildingManager`, `RobotManager`, модели, реестры и инфраструктурные сервисы |
| Entry points через `RegisterEntryPoint<T>` | `PacketHandler`, `GameManager`, `GameBootstrap`, кадровые и стартовые адаптеры |

`DisplayManager` в игровом scope подключается через
`GameUIServiceRegistration`; `MainMenuLifetimeScope` регистрирует собственный
экземпляр как singleton. Состав регистраций — текущий срез реализации, не план
переноса: lifecycle и авторство каждого компонента сверяй по его владельцу и
сценовому binding. Переходы между `Bootstrap`, `MainMenu` и `MainGame` описаны в
[`project-context.md`](../../.agents/project-context.md#2-scenes-and-startup).
