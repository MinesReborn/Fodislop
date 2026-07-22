# Fodinae Client — Backlog (остаток после P0-P5)

> Детальный бэклог незавершённых задач из UI/UX Review.
> Дата: 2026-07-22

---

## 1. P1.1 — PacketHandler God-class

**Приоритет:** Средний | **Сложность:** Высокая | **Оценка:** 3-5 дней

**Файл:** `Assets/Scripts/Networking/PacketHandler.cs` (700 строк + 154 в `PacketHandler.Windows.cs`)

**Проблема:** 46 хендлеров пакетов в одном классе. Мешает навигации, тестированию и распараллеливанию.

**Детали:**

Текущая структура — один монолитный класс со всеми подписками и хендлерами. Уже есть partial class `PacketHandler.Windows.cs` для UI-хендлеров. Нужно продолжить этот паттерн.

**Задачи:**

1. **Создать `PacketHandler.Player.cs`** — вынести хендлеры:
   - `HandlePlayerInfoPacket` (строка 246)
   - `HandleRobotInfoPacket` (строка 239)
   - `HandleRobotPositionPacket` (строка 277)
   - `HandleMovementSpeedPacket` (строка 270)
   - `HandleLevelPacket` (строка 366)
   - `HandleHealthPacket` (строка 372)
   - `HandleCurrencyPacket` (строка 378)
   - `HandleGeologyPacket` (строка 384)
   - `HandleAutoMineStatePacket` (строка 396)
   - `HandleAggressionStatePacket` (строка 405)
   - `HandleDailyBonusStatePacket` (строка 415)
   - `HandleTeleportPacket` (строка 421)
   - `HandleSkillProgressPacket` (строка 445)
   - `HandleShowClanPacket` (строка 640)
   - `HandleHideClanPacket` (строка 652)
   - `HandleMaxDepthPacket` (строка 664)

2. **Создать `PacketHandler.Map.cs`** — вынести хендлеры:
   - `HandleWorldInitPacket` (OnInit)
   - `HandleMapRegionPacket` (строка 297)
   - `HandlePackPacket` (строка 335)
   - `HandleRemovePackPacket` (строка 342)
   - `HandleHBPacket` (строка 349)
   - `HandleBasketPacket` (строка 390)

3. **Создать `PacketHandler.Chat.cs`** — вынести хендлеры:
   - `HandleChatMessageList` (строка 450)
   - `HandleChatList` (строка 462)
   - `HandleLocalChatMessage` (строка 468)
   - `HandleChatMute` (строка 478)

4. **Создать `PacketHandler.Audio.cs`** — вынести:
   - `HandleSFXPacket` (строка 608)

5. **Создать `PacketHandler.Status.cs`** — вынести:
   - `HandleOnlinePacket` (строка 494)
   - `HandlePingPacket` (строка 504)
   - `HandleOutdatedClient` (строка 516)
   - `HandleAddStatusLine` (строка 614)
   - `HandleClearStatusLine` (строка 628)
   - `HandleClearStatus` (строка 634)
   - `HandleMissionInitPacket` (строка 671)
   - `HandleMissionProgressPacket` (строка 684)

6. **Оставить в `PacketHandler.cs`:**
   - Поля (`_packetCount`, `_worldInitPacketsReceived`, `_mapRegionPacketsReceived`, `_isInitialized`, `_uiDocument`, `_modalWindowHandler`, `_openWindows`)
   - `Initialize()` — все `Subscribe<>` вызовы
   - `OnDestroy()` — все `Unsubscribe<>` вызовы
   - `OnWorldInitialized()`, `OnWorldDataLoaded()`
   - `HandleInventoryPacket`, `HandleServerSelectItem`, `HandleServerDeselect` (инвентарь)

**Результат:** `PacketHandler.cs` сократится с 700 до ~250 строк. Каждый partial класс — 50-100 строк.

---

## 2. P2.1 — Миграция инлайн-стилей → USS классы

**Приоритет:** Средний | **Сложность:** Высокая | **Оценка:** 5-7 дней

**Проблема:** Весь UI создаётся inline-стилями в C# (`style.X = value`). USS-файлы созданы (`Assets/Resources/Styles/`), но не подключены к компонентам.

**Задачи по файлам:**

### 2.1.1 PlayerHUD.cs (~200 inline-стилей)
- Найти все `style.X = value` в `InitializeHUD()` и методах создания кнопок
- Заменить на `element.AddToClassList("hud-button")`, `element.AddToClassList("hud-stat")` и т.д.
- Кнопки "Копать", "Агрессия", "Стены" → класс `hud-button`
- HP bar, money, level → класс `hud-stat`
- Оставить в C# только динамические изменения (видимость, цвет по состоянию)

### 2.1.2 InventoryUI.cs (~150 inline-стилей)
- `_fullInventoryPanel` → класс `inventory-root`
- Ячейки сетки → класс `inventory-cell`
- `_hotbarContainer` → класс `hotbar-root`
- Ячейки хотбара → класс `hotbar-cell`
- `_inventoryButton` → класс `btn`

### 2.1.3 PauseMenu.cs (~100 inline-стилей)
- Кнопки меню → класс `btn`
- `_fullscreenButton`, `_simpleGraphicsButton` → класс `btn`
- Labels "Графика", "Фары" → класс `panel-header`
- ScrollContainer → класс `panel-body`

### 2.1.4 GlobalChatUI.cs (~50 inline-стилей)
- `_scrollView` → класс `chat-scroll`
- Labels сообщений → класс `chat-message`
- Input поле → класс `chat-input`
- Контейнер → класс `chat-container`

### 2.1.5 ProgrammatorGrid.cs (~80 inline-стилей)
- Сетка → класс `programmator-grid`
- Ячейки → класс `programmator-cell`
- Тулбар → класс `programmator-toolbar`

### 2.1.6 ModalWindowHandler.cs (~60 inline-стилей)
- Overlay → класс `modal-overlay`
- Panel → класс `modal-window`
- Title → класс `modal-title`
- Description → класс `modal-body`
- Buttons → класс `btn`

### 2.1.7 Остальные файлы
- `FloatingChatBubble.cs` — стиль текста
- `MinimapController.cs` — размеры/позиция
- `WorldMapController.cs` — layout
- `FPSCounter.cs` — позиция, шрифт

**Результат:** Все inline-стили заменены на USS-классы. Theme.uss — единственный источник правды.

---

## 3. P3.4 — Правый клик → context menu

**Приоритет:** Средний | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Файл:** `Assets/Scripts/UI/InventoryUI.cs:436`

**Текущий код:**
```csharp
if (evt.button != 0) return; // Only left click
```

**Задачи:**

1. Добавить обработчик `MouseDownEvent` с `evt.button == 2`
2. При правом клике на ячейке инвентаря показывать context menu:

```
┌──────────────────┐
│ Использовать     │  → отправить ItemUsePacket
│ Выбросить        │  → отправить ItemDropPacket
│ Информация       │  → показать Tooltip с описанием предмета
└──────────────────┘
```

3. Context menu — `VisualElement` с тремя кнопками
4. Позиционировать рядом с курсором (`evt.mousePosition`)
5. Закрывать при клике вне меню или при нажатии Escape
6. Добавить `ItemRegistry` для получения информации о предмете (название, описание, тип)

**Файлы:**
- `Assets/Scripts/UI/InventoryUI.cs` — обработка клика + создание меню
- `Assets/Scripts/Game/Managers/ItemRegistry.cs` — данные о предметах (если есть)

---

## 4. P3.5 — Drop предмета на землю

**Приоритет:** Средний | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Проблема:** Drag-and-drop работает только между слотами инвентаря. Нельзя выбросить предмет на землю.

**Задачи:**

1. В обработчике mouse up проверять, находится ли курсор за пределами inventory panel
2. Если да — отправить `ItemDropPacket` с данными предмета
3. Удалить предмет из локального инвентаря
4. Добавить визуальный фидбэк: анимация "отпускания" предмета

**Файлы:**
- `Assets/Scripts/UI/InventoryUI.cs` — логика drag-and-drop
- `MinesServer.Networking.Client.Packets.Inventory.ItemDropPacket` — серверный пакет (проверить наличие)

---

## 5. P3.6.1 — Навесить Tooltip на HUD кнопки

**Приоритет:** Средний | **Сложность:** Низкая | **Оценка:** 0.5 дня

**Файл:** `Assets/Scripts/UI/PlayerHUD.cs`

**Проблема:** Tooltip component создан (`Assets/Scripts/UI/Tooltip.cs`), но не навешен ни на один элемент.

**Задачи:**

1. Создать экземпляр `Tooltip` в `InitializeHUD()`
2. Навесить на каждую кнопку HUD:
   - "Копать" → "Автоматическое копание блоков"
   - "Агрессия" → "Робот атакует враждебных существ"
   - "Стены" → "Игнорирование коллизий со стенами"
3. Навесить на ячейки инвентаря (после P3.4):
   - При hover показывать: `"Предмет: {name} ({type}) x{count}"`
4. Навесить на кнопки хотбара

**Пример:**
```csharp
var tooltip = new Tooltip();
tooltip.Initialize(_doc);
Tooltip.AttachTo(_autoMineButton, "Автоматическое копание блоков", tooltip);
```

---

## 6. P4.1 — Robot: TextMesh → TextMeshPro

**Приоритет:** Средний | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Файл:** `Assets/Scripts/Game/Robot.cs:146-154`

**Проблема:** Каждый ник робота = отдельный `TextMesh` = отдельный draw call. При 50 роботах = 50 extra draw calls.

**Задачи:**

1. Добавить TextMeshPro в проект (если ещё нет): `com.unity.textmeshpro`
2. Заменить `TextMesh` на `TextMeshProUGUI` или `TextMeshPro` (3D)
3. Для batching — рассмотреть общий `MeshRenderer` с instancing для ников
4. Альтернатива: использовать `DynamicImage` или кастомный шейдер для отрисовки ников в одном draw call

**Файлы:**
- `Assets/Scripts/Game/Robot.cs` — замена TextMesh
- `Packages/manifest.json` — добавить TMP если нужно

---

## 7. P4.3 — Robot: тентаклы без пулинга

**Приоритет:** Низкий | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Файл:** `Assets/Scripts/Game/Robot.cs:250-272`

**Проблема:** 4 `LineRenderer` + 4 `Material` на каждого робота. При 50 роботах = 200 LineRenderers + 200 Materials.

**Задачи:**

1. Создать пул `LineRenderer` (аналогично `ObjectPool<T>`)
2. При despawn робота — возвращать LineRenderer'ы в пул
3. Кэшировать материалы — переиспользовать один `Material` с разными `textureOffset`
4. Рассмотреть общий `Mesh` для всех тентаклов одного типа

**Файлы:**
- `Assets/Scripts/Game/Robot.cs` — логика тентаклов (класс `Tentacle`)
- `Assets/Scripts/Core/ObjectPool.cs` — переиспользовать существующий пул

---

## 8. P5.5 — PersistentAssetCache: синхронный I/O в вызывающем коде

**Приоритет:** Низкий | **Сложность:** Низкая | **Оценка:** 0.5 дня

**Файл:** `Assets/Scripts/AssetPipeline/PersistentAssetCache.cs`

**Проблема:** Async методы добавлены (`GetAssetAsync`, `SaveAssetAsync`, `GetETagAsync`), но вызывающий код (`ClientAssetLoader`) всё ещё использует синхронные версии.

**Задачи:**

1. Найти все вызовы `PersistentAssetCache.GetAsset()` в `ClientAssetLoader.cs`
2. Заменить на `await PersistentAssetCache.GetAssetAsync()`
3. То же для `SaveAsset` → `SaveAssetAsync` и `GetETag` → `GetETagAsync`
4. Проверить, что UniTask используется для async/await

**Файлы:**
- `Assets/Scripts/AssetPipeline/ClientAssetLoader.cs` — вызывающий код

---

## 9. P6.1 — Programmator: имена операторов

**Приоритет:** Средний | **Сложность:** Низкая | **Оценка:** 0.5 дня

**Файл:** `Assets/Scripts/UI/Programmator/ProgrammatorData.cs:18-19`

**Текущий код:**
```csharp
public static readonly int[] WOPERATORS = { 29, 31, 32, 33, 35, 131 };
public static readonly int[] SHIFTWOPERATORS = { 29, 31, 33, 36, 37, 132 };
```

**Задачи:**

1. Добавить словарь имён операторов:
```csharp
public static readonly Dictionary<int, string> OPERATOR_NAMES = new()
{
    [29] = "Двигаться вперёд",
    [31] = "Повернуть направо",
    [32] = "Повернуть налево",
    [33] = "Копать",
    [35] = "Взять",
    [131] = "Ждать",
    [36] = "Повернуть назад",
    [37] = "Положить",
    [132] = "Прыгнуть",
    // ... все остальные операторы
};
```
2. Добавить описание каждого оператора для тултипов
3. Использовать в `ProgrammatorGrid` для отображения имён на ячейках

**Файлы:**
- `Assets/Scripts/UI/Programmator/ProgrammatorData.cs`

---

## 10. P6.2 — Programmator: undo/redo

**Приоритет:** Средний | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Файл:** `Assets/Scripts/UI/Programmator/ProgrammatorData.cs:11`

**Текущий код:**
```csharp
public static int[] Codes = new int[TOTAL_CELLS]; // Нет истории
```

**Задачи:**

1. Добавить стек истории:
```csharp
private static readonly Stack<int[]> _undoStack = new();
private static readonly Stack<int[]> _redoStack = new();
private const int MAX_UNDO_STEPS = 50;
```
2. При каждом изменении ячейки — сохранять копию массива в `_undoStack`
3. Очищать `_redoStack` при новом изменении
4. Ограничивать размер `_undoStack` до 50 шагов
5. Добавить методы `Undo()` и `Redo()`
6. Привязать к Ctrl+Z / Ctrl+Y в `ProgrammatorGrid`

**Файлы:**
- `Assets/Scripts/UI/Programmator/ProgrammatorData.cs` — логика истории
- `Assets/Scripts/UI/Programmator/ProgrammatorGrid.cs` — привязка клавиш

---

## 11. P6.3 — Programmator: серверная валидация

**Приоритет:** Низкий | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Проблема:** Клиент записывает код в массив без проверки. Сервер не знает о changes.

**Задачи:**

1. Добавить кнопку "Отправить" в `ProgrammatorGrid`
2. При нажатии — отправить `ProgrammatorSubmitPacket` с текущим кодом
3. Получить `ProgrammatorResultPacket` с ошибками/успехом
4. Показать результат в UI (зелёная галочка / красное сообщение об ошибке)

**Файлы:**
- `Assets/Scripts/UI/Programmator/ProgrammatorGrid.cs` — кнопка отправки
- `MinesServer.Networking.Client.Packets.Actions.ProgrammatorSubmitPacket` — проверить наличие

---

## 12. P6.4 — Programmator: tooltip при hover

**Приоритет:** Низкий | **Сложность:** Низкая | **Оценка:** 0.5 дня

**Задачи:**

1. Использовать созданный `Tooltip` component
2. При hover на ячейке сетки показывать:
   - `"Ячейка [{col},{row}]: {operator_name} — {description}"`
3. Использовать `OPERATOR_NAMES` из P6.1

**Файлы:**
- `Assets/Scripts/UI/Programmator/ProgrammatorGrid.cs`

---

## 13. P7.1 — Loading states для HUD

**Приоритет:** Средний | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Файл:** `Assets/Scripts/UI/PlayerHUD.cs`

**Проблема:** HUD показывается сразу, данные появляются позже — мерцание.

**Задачи:**

1. Добавить skeleton/spinner для HP bar, money, level
2. Пока данные не загружены — показывать placeholder:
   - HP: серый бар с пульсацией
   - Money: "..." вместо числа
   - Level: "???" вместо номера
3. При получении данных — плавно заменить placeholder на реальные значения
4. Использовать `UIAnimator.FadeIn()` для плавного появления

**Файлы:**
- `Assets/Scripts/UI/PlayerHUD.cs`
- `Assets/Scripts/UI/PlayerStatsModel.cs` — событие загрузки данных

---

## 14. P7.2 — FloatingChatBubble: нет фона

**Приоритет:** Низкий | **Сложность:** Низкая | **Оценка:** 0.5 дня

**Файл:** `Assets/Scripts/UI/FloatingChatBubble.cs`

**Проблема:** Текст плохо читается на светлом фоне.

**Задачи:**

1. Добавить `MeshRenderer` с полупрозрачным фоном за TextMesh
2. Или добавить outline/shadow на TextMesh
3. Или использовать кастомный шейдер с фоном

**Файлы:**
- `Assets/Scripts/UI/FloatingChatBubble.cs`

---

## 15. P7.3 — Minimap toggle

**Приоритет:** Низкий | **Сложность:** Низкая | **Оценка:** 0.5 дня

**Файл:** `Assets/Scripts/UI/MinimapController.cs`

**Проблема:** Миникарта всегда видна.

**Задачи:**

1. Добавить привязку к клавише `N` (или другой)
2. При нажатии — переключать видимость миникарты
3. Добавить кнопку toggle в HUD (опционально)
4. Сохранять состояние в PlayerPrefs

**Файлы:**
- `Assets/Scripts/UI/MinimapController.cs`

---

## 16. P7.4 — WorldMap: нет маркеров

**Приоритет:** Низкий | **Сложность:** Средняя | **Оценка:** 2-3 дня

**Файл:** `Assets/Scripts/UI/WorldMapController.cs`

**Проблема:** Карта показывает только terrain. Нет зданий, точек интереса, других игроков.

**Задачи:**

1. Добавить overlay-слой с маркерами
2. Маркеры из серверных данных:
   - Зелёные точки — другие игроки
   - Жёлтые точки — точки интереса
   - Красные точки — враги
   - Синие точки — здания/базы
3. Каждый маркер — кастомный спрайт с масштабированием
4. Обновлять позиции маркеров при обновлении данных сервера
5. Добавить фильтры маркеров (показать/скрыть категории)

**Файлы:**
- `Assets/Scripts/UI/WorldMapController.cs`
- `Assets/Scripts/UI/WorldMapRenderer.cs`

---

## 17. P7.5 — Keyboard navigation

**Приоритет:** Низкий | **Сложность:** Средняя | **Оценка:** 1-2 дня

**Файл:** `Assets/Scripts/UI/PlayerHUD.cs:180-194`

**Проблема:** Tab/Arrow keys заблокированы. Нельзя навигировать по UI с клавиатуры.

**Задачи:**

1. Когда `PacketHandler.IsInputBlocked == true` (открыто окно) — разрешить навигацию
2. Tab — переход к следующему элементу
3. Shift+Tab — переход к предыдущему
4. Arrow keys — навигация по спискам/网格
5. Enter/Space — активация элемента
6. Escape — закрытие текущего окна (через UIStack)

**Файлы:**
- `Assets/Scripts/UI/PlayerHUD.cs`
- `Assets/Scripts/UI/UIStack.cs` — интеграция

---

## 18. P7.6 — Responsive layout

**Приоритет:** Низкий | **Сложность:** Высокая | **Оценка:** 3-5 дней

**Проблема:** Все размеры захардкожены в пикселях. На экранах < 1280px UI налезает друг на друга.

**Задачи:**

1. Заменить фиксированные пиксели на `%` и `Length.Percent`:
   - `width: 400px` → `width: 30%`
   - `left: 10px` → `left: 1%`
2. Добавить media-query аналог:
```csharp
private bool IsCompactMode => _doc.rootVisualElement.resolvedStyle.width < 1280;
```
3. Компактный режим для маленьких экранов:
   - Уменьшить размеры кнопок
   - Скрыть необязательные элементы
   - Изменить layout с column на row
4. Протестировать на разрешениях: 1280x720, 1366x768, 1920x1080

**Файлы:**
- Все UI-файлы с inline-стилями (после P2.1 миграции на USS)
- `Assets/Resources/Styles/Theme.uss` — добавить CSS custom properties для размеров

---

## Оценка общего объёма

| Задача | Дни | Сложность |
|---|---|---|
| P1.1 PacketHandler split | 3-5 | Высокая |
| P2.1 USS миграция | 5-7 | Высокая |
| P3.4 Right-click menu | 1-2 | Средняя |
| P3.5 Drop на землю | 1-2 | Средняя |
| P3.6.1 Tooltip на HUD | 0.5 | Низкая |
| P4.1 TextMesh → TMP | 1-2 | Средняя |
| P4.3 Тентаклы pooling | 1-2 | Средняя |
| P5.5 Async I/O в вызывающем коде | 0.5 | Низкая |
| P6.1 Имена операторов | 0.5 | Низкая |
| P6.2 Undo/redo | 1-2 | Средняя |
| P6.3 Серверная валидация | 1-2 | Средняя |
| P6.4 Tooltip programmator | 0.5 | Низкая |
| P7.1 Loading states | 1-2 | Средняя |
| P7.2 Фон чата | 0.5 | Низкая |
| P7.3 Minimap toggle | 0.5 | Низкая |
| P7.4 WorldMap маркеры | 2-3 | Средняя |
| P7.5 Keyboard navigation | 1-2 | Средняя |
| P7.6 Responsive layout | 3-5 | Высокая |
| **ИТОГО** | **20-40 дней** | |

### Рекомендуемый порядок (quick wins first):

1. **P6.1** Имена операторов (0.5 дня)
2. **P6.4** Tooltip programmator (0.5 дня)
3. **P3.6.1** Tooltip на HUD (0.5 дня)
4. **P7.2** Фон чата (0.5 дня)
5. **P7.3** Minimap toggle (0.5 дня)
6. **P5.5** Async I/O (0.5 дня)
7. **P3.4** Right-click menu (1-2 дня)
8. **P3.5** Drop на землю (1-2 дня)
9. **P6.2** Undo/redo (1-2 дня)
10. **P7.1** Loading states (1-2 дня)
11. **P4.1** TextMesh → TMP (1-2 дня)
12. **P4.3** Тентаклы pooling (1-2 дня)
13. **P7.5** Keyboard navigation (1-2 дня)
14. **P6.3** Серверная валидация (1-2 дня)
15. **P7.4** WorldMap маркеры (2-3 дня)
16. **P1.1** PacketHandler split (3-5 дней)
17. **P2.1** USS миграция (5-7 дней)
18. **P7.6** Responsive layout (3-5 дней)
