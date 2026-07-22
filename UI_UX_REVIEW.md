# Fodinae Client — UI/UX/AI Review & Fix Roadmap

> Handoff-документ для глобального улучшения клиентского кода.
> Дата: 2026-07-22

---

## Содержание

1. [P0 — Критические баги](#p0--критические-баги)
2. [P1 — Архитектурные проблемы](#p1--архитектурные-проблемы)
3. [P2 — UI: Дизайн-система и консистентность](#p2--ui-дизайн-система-и-консистентность)
4. [P3 — UX: Навигация и взаимодействие](#p3--ux-навигация-и-взаимодействие)
5. [P4 — AI/Роботы](#p4--aiроботы)
6. [P5 — Производительность и GC](#p5--производительность-и-gc)
7. [P6 — Программатор](#p6--программатор)
8. [P7 — Полировка и accessibility](#p7--полировка-и-accessibility)

---

## P0 — Критические баги

Баги, которые ломают стабильность или безопасность. Фиксить первыми.

### 0.1 PacketHandler: leak подписок

**Файл:** `Assets/Scripts/Networking/PacketHandler.cs`

В `OnDestroy` (строки 160-237) отписываются не все пакеты. Подписки на `LevelPacket`, `HealthPacket`, `CurrencyPacket`, `GeologyPacket`, `BasketPacket` (строки 114-118) **не отменяются**.

**Фикс:** Добавить `NetworkService.Unsubscribe<LevelPacket>(...)` и аналогичные для 4 оставшихся типов в `OnDestroy`.

---

### 0.2 MainMenu: debug-режим в проде

**Файл:** `Assets/Scripts/UI/MainMenu.cs:180`

```csharp
RobotManager.ShowDebugVisuals = true;
```

Вызывается при нажатии Play. Должно быть `false` или убрано entirely.

---

### 0.3 Robot: Shader.Find в runtime

**Файл:** `Assets/Scripts/Game/Robot.cs:573`

```csharp
_material = new Material(Shader.Find("Sprites/Default"));
```

`Shader.Find` ищет шейдер по имени в runtime. В билдах шейдер может быть strip-нут.

**Фикс:** Кэшировать в статическом поле или передавать через serialized reference.

---

## P1 — Архитектурные проблемы

Структурные issues, которые мешают масштабированию и поддержке.

### 1.1 PacketHandler — God-class

**Файл:** `Assets/Scripts/Networking/PacketHandler.cs` (712 строк)

40+ подписок в одном классе. Мешает навигации и тестированию.

**Фикс:** Разбить на domain-specific хендлеры:

| Хендлер | Пакеты |
|---|---|
| `MapPacketHandler` | WorldInit, MapRegion, CellUpdate |
| `PlayerPacketHandler` | PlayerInfo, RobotPosition, Move, Health, Level |
| `ChatPacketHandler` | ChatMessage, LocalChat |
| `UIPacketHandler` | OpenWindow, CloseWindow, ModalWindow |
| `InventoryPacketHandler` | Inventory, Basket |
| `AudioPacketHandler` | SFX |

Каждый хендлер — отдельный `MonoBehaviour`, подписывается в `OnEnable`, отписывается в `OnDisable`.

---

### 1.2 FindAnyObjectByType разбросан повсюду

Вызовы `FindAnyObjectByType<T>()` в runtime — scene scan, который должен быть заменён на кэшированные ссылки.

| Файл | Строки | Вызов |
|---|---|---|
| `PacketHandler.cs` | 404, 414, 432, 503, 513 | `FindAnyObjectByType<PlayerMovementController>()` |
| `PacketHandler.cs` | 258, 292, 651, 669 | `FindGameObjectWithTag("Player")` |
| `PauseMenu.cs` | 357, 373 | `FindAnyObjectByType<SingleMeshTerrainRenderer>()` |
| `PauseMenu.cs` | 56, 190 | `FindObjectsByType<Canvas>()` |
| `PlayerHUD.cs` | 132, 157, 862, 887, 912 | `FindAnyObjectByType<UIDocument>()`, `<PlayerMovementController>()` |
| `InventoryUI.cs` | 109 | `FindAnyObjectByType<UIDocument>()` |
| `GlobalChatUI.cs` | 21, 58 | `FindAnyObjectByType<GlobalChatUI>()`, `<UIDocument>()` |
| `WorldMapController.cs` | 30-37 | 7 штук в одном `Start()` |

**Фикс:** Singleton-кэши: `PlayerMovementController.LocalPlayer`, `SingleMeshTerrainRenderer.Instance`. UIDocument — передавать через serialized field или DI.

---

### 1.3 Object pooling отсутствует

| Объект | Создаётся/уничтожается | Файл |
|---|---|---|
| Robot GameObjects | `Instantiate`/`Destroy` при каждом spawn/despawn | `RobotManager.cs:68,105` |
| FloatingChatBubble | `new GameObject` на каждое сообщение | `FloatingChatManager.cs:53` |
| Chat Labels | `new Label` на каждое сообщение | `GlobalChatUI.cs:324` |
| Tentacle LineRenderers | `new GameObject` + `LineRenderer` на каждую тентаклу | `Robot.cs:569` |
| InventoryUI floating item | `new VisualElement` при каждом drag | `InventoryUI.cs:453` |

**Фикс:** Внедрить простой object pool (`Assets/Scripts/Core/ObjectPool.cs`) и использовать для Robot, ChatBubble, ChatLabel.

---

### 1.4 AssetCache: нет eviction policy

**Файл:** `Assets/Scripts/AssetPipeline/AssetCache.cs`

RAM-кэш растёт бесконечно. Для MMO с сотнями типов ячеек может достигать сотен MB.

**Фикс:** Добавить LRU eviction с max size (например 256 MB), выгружать текстуры вне viewport.

---

### 1.5 PersistentAssetCache: синхронный I/O

**Файл:** `Assets/Scripts/AssetPipeline/PersistentAssetCache.cs`

`File.ReadAllBytes` блокирует thread pool на 50-100ms для больших текстур.

**Фикс:** Заменить на `File.ReadAllBytesAsync` или `UniTask.RunOnThreadPool` с `FileStream.ReadAsync`.

---

## P2 — UI: Дизайн-система и консистентность

### 2.1 Создать USS-тему

**Текущее состояние:** Весь UI создаётся inline-стилями в C#. Нет UXML, нет USS (кроме `chat-input.uss`).

**Цель:** Единый источник правды для стилей.

**Структура:**

```
Assets/Resources/Styles/
  Theme.uss              # Основные variables
  Panel.uss              # Стили панелей
  Button.uss             # Стили кнопок
  Input.uss              # Стили полей ввода
  Inventory.uss          # Инвентарь
  HUD.uss                # HUD
  Chat.uss               # Чат
  Programmator.uss       # Программатор
  Modal.uss              # Модальные окна
```

**Theme.uss — ключевые variables:**

```css
:root {
  /* Цвета */
  --color-panel-bg: rgba(20, 20, 20, 0.9);
  --color-panel-border: rgba(90, 90, 90, 1);
  --color-accent: rgba(178, 166, 128, 1);
  --color-accent-hover: rgba(204, 191, 153, 1);
  --color-text: white;
  --color-text-dim: rgba(180, 180, 180, 1);

  /* Кнопки */
  --btn-bg: rgba(38, 38, 38, 1);
  --btn-hover: rgba(90, 90, 90, 1);
  --btn-border: rgba(102, 102, 102, 1);
  --btn-padding: 8px 20px;
  --btn-min-width: 180px;
  --btn-font-size: 14px;

  /* Размеры */
  --panel-padding: 12px;
  --panel-border-width: 2px;
  --cell-size: 50px;
  --cell-gap: 10px;
  --icon-size: 36px;
}
```

---

### 2.2 Рефакторинг Inline-стилей

Для каждого UI-компонента:

1. Заменить все `style.X = value` на `element.AddToClassList("class-name")`
2. Стили вынести в соответствующий `.uss` файл
3. Оставить в C# только динамические изменения (可见性, цвет по состоянию)

**Пример рефакторинга кнопки:**

```csharp
// Было:
btn.style.backgroundColor = _btnBg;
btn.style.borderTopWidth = 2;
btn.style.borderBottomWidth = 2;
// ... 10+ строк

// Стало:
btn.AddToClassList("hud-button");
```

---

### 2.3 Альфа-канал панелей

**Проблема:** Разные компоненты используют разный opacity:

| Компонент | Альфа |
|---|---|
| PlayerHUD | 0.85 |
| InventoryUI | 0.85 |
| PauseMenu | 0.95 |
| GlobalChatUI | 0.9 |
| ProgrammatorGrid | 0.95 |
| ModalWindow | 0.95 |

**Фикс:** Единое значение `--color-panel-bg: rgba(20, 20, 20, 0.9)` в Theme.uss.

---

### 2.4 Accent color дублируется

`new Color(0.7f, 0.65f, 0.5f, 1f)` встречается в 10+ файлах:

- `PlayerHUD.cs:36`
- `InventoryUI.cs:23`
- `PauseMenu.cs:20`
- `GlobalChatUI.cs:130`
- `ProgrammatorGrid.cs:77`
- `ModalWindowHandler.cs:63`

**Фикс:** Одно определение в `Theme.uss` как `--color-accent`.

---

## P3 — UX: Навигация и взаимодействие

### 3.1 Нет анимаций переходов

Все открытия/закрытия окон — мгновенные `display: flex/none`.

**Фикс:** Универсальная утилита:

```csharp
// Assets/Scripts/UI/UIAnimator.cs
public static class UIAnimator
{
    public static UniTask FadeIn(VisualElement el, float duration = 0.2f);
    public static UniTask FadeOut(VisualElement el, float duration = 0.2f);
    public static UniTask SlideIn(VisualElement el, Vector2 from, float duration = 0.25f);
    public static UniTask SlideOut(VisualElement el, Vector2 to, float duration = 0.25f);
}
```

Применить к: InventoryUI, GlobalChatUI, PauseMenu, ProgrammatorGrid, все popup-окна.

---

### 3.2 Escape-логика запутана

**Текущее поведение:**

| Состояние | Escape |
|---|---|
| Ничего не открыто | Открывает PauseMenu |
| PauseMenu открыт | Закрывает PauseMenu |
| Settings открыты | Возврат в PauseMenu |
| Server window открыт | Отправляет `ElementClickPacket` (закрытие на сервере) |
| Chat открыт | Закрывает Chat |
| Inventory открыт | **Ничего не делает** |

**Фикс:** Единый стек окон:

```
UIStack:
  push(window) → показать окно, заблокировать ввод
  pop() → закрыть верхнее окно, разблокировать ввод если стек пуст
  Escape → pop() или открыть PauseMenu если стек пуст
```

---

### 3.3 Hotbar исчезает при открытии инвентаря

**Файл:** `Assets/Scripts/UI/InventoryUI.cs:637`

```csharp
_hotbarContainer.style.display = _isInventoryOpen ? DisplayStyle.None : DisplayStyle.Flex;
```

**Проблема:** Нельзя использовать хотбар с открытым инвентарём.

**Фикс:** Показывать хотбар всегда. Инвентарь — отдельная панель поверх. Hotbar остаётся видимой.

---

### 3.4 Нет правого клика в инвентаре

**Файл:** `Assets/Scripts/UI/InventoryUI.cs:436`

```csharp
if (evt.button != 0) return; // Only left click
```

**Фикс:** Добавить `MouseDownEvent` с `evt.button == 2` → context menu:

```
┌──────────────┐
│ Использовать │
│ Выбросить    │
│ Информация   │
└──────────────┘
```

---

### 3.5 Нет drop-на землю

Drag-and-drop работает только между слотами. Нельзя выбросить предмет.

**Фикс:** Если mouse up за пределами inventory panel → отправить `ItemDropPacket`.

---

### 3.6 Нет tooltip для大部分 элементов

- Кнопки HUD ("Копать ✗", "Агрессия ✗", "Стены ✗") — нет описания
- Операторы программатора — магические числа без имён
- Предметы инвентаря — tooltip показывается только при выборе, не при hover

**Фикс:**

1. Добавить `Tooltip` component для VisualElement
2. При hover показывать всплывающую подсказку
3. Для программатора — `ProgrammatorData.OPERATOR_NAMES[id] = "Повернуть"` и т.д.

---

### 3.7 Нет подтверждения выхода

**Файл:** `Assets/Scripts/UI/PauseMenu.cs:425-432`

```csharp
private void QuitGame()
{
    Application.Quit(); // Без подтверждения
}
```

**Фикс:** Показать ModalWindow: "Вы уверены? Несохранённые данные будут потеряны."

---

### 3.8 Нет настройки разрешения

PauseMenu имеет fullscreen toggle, но нет выбора разрешения.

**Фикс:** Добавить dropdown с доступными разрешениями:

```csharp
var resolutions = Screen.resolutions;
// Dropdown с опциями: 1920x1080, 1280x720, etc.
```

---

## P4 — AI/Роботы

### 4.1 Robot: TextMesh — legacy, не batching-friendly

**Файл:** `Assets/Scripts/Game/Robot.cs:146-154`

Каждый ник = отдельный draw call. При 50 роботах = 50 extra draw calls.

**Фикс:** Заменить на TextMeshPro или общий `MeshRenderer` с instancing.

---

### 4.2 Robot: нет object pooling

**Файл:** `Assets/Scripts/Game/Managers/RobotManager.cs:60-86`

Каждый spawn = `Instantiate`, каждый despawn = `Destroy`.

**Фикс:**

```csharp
// RobotPool.cs
public class RobotPool : SingletonMonoBehaviour<RobotPool>
{
    private Queue<Robot> _pool = new();

    public Robot Get()
    {
        return _pool.Count > 0 ? _pool.Dequeue() : Instantiate(_prefab).GetComponent<Robot>();
    }

    public void Return(Robot robot)
    {
        robot.gameObject.SetActive(false);
        _pool.Enqueue(robot);
    }
}
```

---

### 4.3 Robot: тентаклы без пулинга

**Файл:** `Assets/Scripts/Game/Robot.cs:250-272`

4 `LineRenderer` + 4 `Material` на каждого робота.

**Фикс:** Пулить LineRenderer'ы и переиспользовать материалы.

---

## P5 — Производительность и GC

### 5.1 Keyboard.current.allKeys итерация каждый кадр

**Файл:** `Assets/Scripts/Player/PlayerInteractionController.cs:90`

```csharp
foreach (var key in Keyboard.current.allKeys)
```

Аллоцирует enumerator каждый кадр.

**Фикс:** Кэшировать `allKeys` в `Start()` или использовать `InputSystem.onAnyButtonPress`.

---

### 5.2 LINQ в hot paths

| Файл | Строка | Вызов |
|---|---|---|
| `PacketHandler.cs` | 363 | `hbPacket.Payload.Any(p => p is MapRegionPacket)` |
| `NetworkService.cs` | 112 | `handlers.Any(s => s.OriginalHandler == handler)` |

**Фикс:** Заменить на `for` loop или `HashSet`.

---

### 5.3 MapManager classification methods

**Файл:** `Assets/Scripts/Game/Managers/MapManager.cs:267-291`

`IsLooseRockType` и `IsRoundableLoose` — длинные `||` цепочки, вызываются для каждой ячейки.

**Фикс:** `HashSet<CellType>` или `bool[]` lookup table:

```csharp
private static readonly HashSet<CellType> LooseRockTypes = new() { CellType.Rock1, CellType.Rock2, ... };

public static bool IsLooseRockType(CellType type) => LooseRockTypes.Contains(type);
```

---

### 5.4 Main Menu fallback texture

**Файл:** `Assets/Scripts/UI/MainMenu.cs:124`

```csharp
Color[] pixels = new Color[width * height]; // 1920*1080*16 = 33 MB
```

**Фикс:** Использовать `Color32[]` (8x меньше) или сделать fallback texture smaller (192x108).

---

## P6 — Программатор

### 6.1 Магические числа операторов

**Файл:** `Assets/Scripts/UI/Programmator/ProgrammatorData.cs:18-19`

```csharp
public static readonly int[] WOPERATORS = { 29, 31, 32, 33, 35, 131 };
public static readonly int[] SHIFTWOPERATORS = { 29, 31, 33, 36, 37, 132 };
```

**Фикс:**

```csharp
public static readonly Dictionary<int, string> OPERATOR_NAMES = new()
{
    [29] = "Двигаться вперёд",
    [31] = "Повернуть направо",
    [32] = "Повернуть налево",
    [33] = "Копать",
    [35] = "Взять",
    [131] = "Ждать",
    // ...
};
```

---

### 6.2 Нет undo/redo

**Файл:** `Assets/Scripts/UI/Programmator/ProgrammatorData.cs:11`

```csharp
public static int[] Codes = new int[TOTAL_CELLS]; // Нет истории
```

**Фикс:** Добавить `Stack<int[]>` undo history,最大 50 шагов.

---

### 6.3 Нет серверной валидации

Клиент записывает код в массив без проверки.

**Фикс:** Отправить `ProgrammatorSubmitPacket` на сервер, получить `ProgrammatorResultPacket` с ошибками/успехом.

---

### 6.4 Нет визуальных подсказок

Hover на ячейке не показывает что делает оператор.

**Фикс:** Tooltip при hover: `"Ячейка [3,5]: Копать — робот копает блок перед собой"`.

---

## P7 — Полировка и accessibility

### 7.1 Loading states

HUD показывается сразу, данные появляются позже — мерцание.

**Фикс:** Skeleton/spinner для HP bar, money, level пока данные не загружены.

---

### 7.2 FloatingChatBubble: нет фона

**Файл:** `Assets/Scripts/UI/FloatingChatBubble.cs`

Текст плохо читается на светлом фоне.

**Фикс:** Добавить полупрозрачный background sprite или outline на TextMesh.

---

### 7.3 Minimap: нет toggle

**Файл:** `Assets/Scripts/UI/MinimapController.cs`

Миникарта всегда видна.

**Фикс:** Кнопка toggle или привязка к клавише (например `N`).

---

### 7.4 WorldMap: нет маркеров

**Файл:** `Assets/Scripts/UI/WorldMapController.cs`

Карта показывает только terrain. Нет зданий, точек интереса,其他玩家.

**Фикс:** Добавить overlay-слой с маркерами из серверных данных.

---

### 7.5 Keyboard navigation

**Файл:** `Assets/Scripts/UI/PlayerHUD.cs:180-194`

Tab/Arrow keys заблокированы. Нельзя навигировать по UI с клавиатуры.

**Фикс:** Разрешить навигацию когда `PacketHandler.IsInputBlocked == true` (открыто окно).

---

### 7.6 Responsive layout

Все размеры захардкожены в пикселях. На экранах < 1280px UI налезает друг на друга.

**Фикс:**

1. Использовать `%` и `Length.Percent` вместо фиксированных пикселей
2. Добавить media-query аналог через `ResolvedStyle.width` checks
3. Компактный режим для маленьких экранов

---

## Чек-лист для реализации

### Фаза 1: Критические фиксы (1-2 дня)

- [ ] PacketHandler OnDestroy — добавить unsubscribe для 5 типов
- [ ] MainMenu — убрать `ShowDebugVisuals = true`
- [ ] Robot — кэшировать `Shader.Find`

### Фаза 2: Архитектура (3-5 дней)

- [ ] Создать USS тему (`Assets/Resources/Styles/`)
- [ ] Singleton-кэши для PlayerMovementController, SingleMeshTerrainRenderer
- [ ] Разбить PacketHandler на domain-specific хендлеры
- [ ] Object pool для Robot, ChatBubble, ChatLabel

### Фаза 3: UI рефакторинг (5-7 дней)

- [ ] Мигрировать все inline-стили → USS классы
- [ ] Привести альфа-канал к единому 0.9
- [ ] Вынести accent color в Theme.uss
- [ ] Анимации переходов (fade, slide)

### Фаза 4: UX улучшения (3-5 дней)

- [ ] UI stack для Escape-логики
- [ ] Hotbar всегда видима
- [ ] Правый клик → context menu
- [ ] Drop на землю
- [ ] Tooltips для всех кнопок
- [ ] Подтверждение выхода
- [ ] Настройка разрешения

### Фаза 5: AI/Роботы (2-3 дня)

- [ ] Robot pool
- [ ] Кэшировать shader/material для тентаклов
- [ ] Programmator: имена операторов, tooltip, undo

### Фаза 6: Полировка (2-3 дня)

- [ ] Loading states для HUD
- [ ] Фон для FloatingChatBubble
- [ ] Minimap toggle
- [ ] WorldMap маркеры
- [ ] Keyboard navigation
- [ ] Responsive layout

---

## Файлы для изменения

| Файл | Изменения |
|---|---|
| `PacketHandler.cs` | Разбиение на хендлеры, unsubscribe leak |
| `MainMenu.cs` | Убрать debug flag |
| `Robot.cs` | Кэшировать shader, pool тентаклов |
| `RobotManager.cs` | Object pooling |
| `PlayerHUD.cs` | USS миграция, tooltips, loading states |
| `InventoryUI.cs` | Right-click, drop, hotbar fix, USS |
| `GlobalChatUI.cs` | USS, pooling labels |
| `PauseMenu.cs` | Confirm quit, resolution, USS |
| `ProgrammatorGrid.cs` | USS, tooltip, undo |
| `ProgrammatorData.cs` | Operator names, undo history |
| `FloatingChatBubble.cs` | Background, pooling |
| `FloatingChatManager.cs` | Pooling |
| `MinimapController.cs` | Toggle |
| `WorldMapController.cs` | Markers |
| `ModalWindowHandler.cs` | Animations, keyboard nav |
| `AssetCache.cs` | Eviction policy |
| `PersistentAssetCache.cs` | Async I/O |
| `MapManager.cs` | Lookup table для classification |
| `PlayerInteractionController.cs` | Кэшировать allKeys |
| `CameraFollow.cs` | Bounds clamping |
| **НОВЫЙ:** `Assets/Resources/Styles/Theme.uss` | Дизайн-система |
| **НОВЫЙ:** `Assets/Scripts/UI/UIAnimator.cs` | Анимации |
| **НОВЫЙ:** `Assets/Scripts/UI/UIStack.cs` | Стек окон |
| **НОВЫЙ:** `Assets/Scripts/UI/Tooltip.cs` | Tooltip component |
| **НОВЫЙ:** `Assets/Scripts/Core/ObjectPool.cs` | Пул объектов |
