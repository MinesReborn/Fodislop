# Kern agent guidance

Kern — 2D MMORPG-пісочниця на Unity 6 (`6000.6.0f1`), URP 2D 17.6, C# 12, UI Toolkit, UniTask і пакетах `darkar25.fodina.*`.

## Межі дозволів

- Не запускайте, не відкривайте, не закривайте й не контролюйте Unity Editor/Hub; не викликайте Unity CLI, MCP, Editor API, batch mode, build, tests, імпорт або читання Editor-логів, якщо поточний запит користувача прямо не називає конкретну Unity-операцію. Не просіть системного дозволу на Unity-дію з власної ініціативи.
- Дозвіл охоплює лише прямо названу Unity-операцію. Якщо без Unity не можна завершити перевірку, зупиніться та назвіть конкретну операцію, яка лишилася користувачеві.
- Не виконуйте Git-відкат або переписування історії без прямого запиту в поточному повідомленні: `reset`, `restore`, checkout для відновлення, `revert`, `clean`, amend, rebase чи force-push. Не відновлюйте файли з `HEAD`, stash або reflog і не просіть такого дозволу з власної ініціативи.
- Не редагуйте текстом `.prefab`, `.unity` або `.asset`; змінюйте їх лише через явно дозволений Unity Editor API/Inspector. Зберігайте GUID і `.meta`.
- Наявні зміни в робочому дереві належать користувачеві. Не перезаписуйте й не включайте їх у свої зміни без потреби.
- НІКОЛИ НЕ РОБИ --no-verify!
- Комміть завжди все: усі зміни робочого дерева одним коммітом (`git add -A && git commit`), без дроблення на частини і без вибіркового стейджингу, якщо користувач прямо не попросив інакше.

## Виконання задач

Для нетривіальної роботи визначте результат, внесіть зміни й продовжуйте до перевіреного завершення, якщо не потрібне нове рішення користувача. Без окремого погодження дозволено запускати релевантні локальні перевірки, які не керують Unity, не мають production-доступу та використовують disposable fixtures. Виправляйте спричинені вашою зміною збої й повторюйте відповідні перевірки.

Не повертайте користувачеві проміжний блокер або опис симптомів як результат роботи. Самостійно локалізуйте першопричину, перевіряйте альтернативні безпечні шляхи й продовжуйте до фактичного результату. Зупиняйтеся лише коли вичерпані доступні варіанти та подальший крок справді потребує нового дозволу, зовнішньої зміни або рішення користувача; тоді повідомляйте конкретний доведений блокер без виправдань і повторів.

Не читайте всю документацію або повну карту репозиторію за замовчуванням. Починайте з файлів, яких торкається задача, і відкривайте лише релевантні розділи [.agents/project-context.md](.agents/project-context.md):

- сцени, переходи, DI або startup pipeline — розділи 2–3;
- мережа, UI, світ, рендеринг, аудіо чи Programmator — відповідний підрозділ 4;
- архітектурні інваріанти — розділ 5;
- діагностика або продуктивність — розділ 6.

Код є джерелом істини, якщо довідковий контекст застарів.

## C# і структура

- Увімкнено `#nullable enable`; reference types позначайте nullable/non-null явно. Використовуйте C# 12 (`primary constructors`, `readonly record struct`, collection expressions), коли це доречно.
- Звичайні типи мають file-scoped namespace. Типи-нащадки `MonoBehaviour`, `ScriptableObject`, `ScriptableRendererFeature` або `VolumeComponent` мають block namespace, інакше `MonoScript.GetClass()` може повернути `null`.
- Дотримуйтеся Allman braces, обов'язкових `{}`, SA1513/SA1508 і trailing comma у багаторядкових ініціалізаторах. Приватні поля — `_camelCase`; публічні члени й типи — `PascalCase`.
- Ім'я Unity-скрипта має збігатися з класом. Перевірка `MonoScript.GetClass()` потребує окремого явного дозволу на Unity.
- Не створюйте менеджери через `AddComponent` у `Configure`. Scene-компоненти реєструйте через `RegisterComponent`; prefab/entity створюйте через `ISceneObjectFactory`. `IObjectResolver` допустимий лише в composition roots і фабриках.
- `Kern.Contracts` — нижній шар: не посилається на жодну збірку `Kern.*`. Контракти модуля лежать поруч із модулем у папці `Contracts/` з `Kern.Contracts.asmref`; без нього тип потрапляє у збірку модуля й ламає всіх, хто нижче. Тип, що залежить від реалізації, до `Contracts/` не кладіть. Стереже `ContractsAssemblyBoundaryTests`.
- Документи в `docs/` мають бути автономним HTML з inline `<style>`, без Markdown і зовнішніх залежностей.

## Критичні інваріанти

- Серверні координати мають початок згори ліворуч і Y вниз; перетворення виконуйте лише через `CoordinateUtils` з `MapManager.WorldHeight`.
- UI Toolkit використовує єдине дерево стилів через `KernTheme.tss`; статична структура живе в UXML. Видимість перемикається класом `is-hidden` через `UIState`, а координати екрана — через `RuntimePanelUtils.ScreenToPanel`.
- Кожна сцена має рівно один корінь — свій `LifetimeScope`; усі authored-об'єкти лежать під ним. Об'єкт поруч зі scope контейнер не бачить. Стереже `ProductionSceneContractValidator.ValidateSingleRoot`, виправляє меню `Kern/Architecture/Move Scene Roots Under Composition Root`.
- `RegisterInstance` не інжектить вручну створені об'єкти. Не резолвіть контейнер у `Awake`, `OnEnable` або `Start`.
- `VolumeProfile.Add<T>()` створює компонент лише в пам'яті; editor-код має додати його через `AssetDatabase.AddObjectToAsset()` перед збереженням.
- Не маскуйте дефекти очищенням Unity cache, повторною компіляцією, FPS-cap, frame skipping або throttling. Зміни гарячих шляхів робіть лише після відтворення чи строгого підтвердження причини.
- Освітлення ЗАБОРОНЕНО обмежувати частотою оновлення (герцовкою, таймером на кшталт «20 разів на секунду», налаштуванням частоти solve) — ні для джерел, ні для геометрії. Динамічне світло (джерела роботів) заборонено й прив'язувати до переходу в нову клітинку. Світло джерела перераховується щокадру разом із плавним рухом робота, джерело центроване на його точній позиції. Дорогий прохід треба здешевлювати, а не пропускати.
- Якщо після власної зміни агента продуктивність погіршилась або не покращилась, агент ЗОБОВ'ЯЗАНИЙ сам пояснити причину з коду цієї зміни: що саме вона додала на кадр (кількість dispatch-ів і потоків, кроків марширування, записів і читань текстур, розміри нових текстур і буферів) і чому це не дало виграшу. ЗАБОРОНЕНО відповідати «не можна сказати», «невідомо, що гальмує» чи перекладати пошук причини на користувача замірами, коли зміну щойно зробив сам агент. До зміни гарячого шляху GPU агент рахує її вартість на кадр у тих самих одиницях, порівнює з тим, що вона замінює, і враховує, що дешевше в CPU-моделі не означає дешевше на GPU (запис у великі текстури, вкладені динамічні цикли, відсутність раннього виходу).
- VSync НІКОЛИ не пояснює продуктивність. Заборонено в будь-якій формі: як причину низького FPS, як «стелю» чи «очікування екрана» в замірах, як застереження «заміри були з синхронізацією», як пораду для заміру, у коментарях до коду й у звітах. Число часу кадру чи відеокарти пояснюйте лише роботою коду проєкту. Якщо користувач сам повідомляє про дефект перемикача VSync — це окремий баг налаштувань: виправте його й звітуйте лише про налаштування, не пов'язуючи з FPS чи замірами.
- «Editor overhead» (EditorLoop, Scene view GPU, PlayerConnection сокет) НІКОЛИ не є поясненням низького FPS у грі. Ці категорії у Profiler — артефакти вимірювання в Play Mode всередині Editor, а не гальма гри. Якщо у Profiler видно `EditorLoop`, `CFRunLoopRun`, `Socket.Accept` — це шум замірів, ігноруй їх і шукай причину в категоріях `Scripts`, `Rendering`, `Physics`, `GarbageCollector` або конкретних маркерах ігрового коду. Закрита Scene view у Editor знижує GPU-шум, але не є обов'язковою умовою аналізу.

## Lighting

Перед редагуванням lighting кода (shaders або C#):

1. Прочитай [LIGHTING_ARCHITECTURE.md](../LIGHTING_ARCHITECTURE.md) — там dataflow и контракты стадий.
2. Определи, к какой стадии относится изменение:
   - `LightingTypes.hlsl`, `Extinction.hlsl`, `GeometryField.hlsl`, `DDA.hlsl` — общие примитивы
   - `GeometryCache/GeometryCache.hlsl` — `BuildCellSolidMask`
   - `Cascades/CascadeTrace.hlsl` — `SolveCascade` (DDA traversal)
   - `Cascades/CascadeResolve.hlsl` — `ResolveDirect` (atlas lookup)
   - `Dynamic/DynamicPolar.hlsl` — `TraceDynamicPolar`, `DynamicRadianceFromPolar` (DDA)
   - `Dynamic/DynamicLightTrace.hlsl` — `SolveDynamicLighting`, `ComposeDynamicLighting`
   - `Bounce/BounceCache.hlsl` — `BuildBounceTaps`, `BuildBounceFilter` (DDA)
   - `Bounce/BounceSolve.hlsl` — `SolveDiffuseBounce`
   - `Composite/CompositeLighting.hlsl` — `CompositeLighting`
   - `Block/BlockLighting.hlsl` — PerBlock tier
3. **Запрещено** добавлять вызовы DDA (`TraceLightSegment`, `TraceRadianceSegment`) в:
   - `CascadeResolve`
   - `DynamicLightTrace`
   - `BounceSolve`
   - `CompositeLighting`
   - `BlockLighting`
4. Любое изменение transport стадий (CascadeTrace, DynamicPolar, BounceCache) должно увеличивать `LightingDdaSegments` или `LightingDdaTexelVisits` в `IFrameTelemetry` — это ключевые метрики стоимости.
5. После изменения transport math проверь:
   - Все debug views (0–9) показывают ожидаемую картину
   - `LightingDdaSegments` / `LightingDdaTexelVisits` не выросли неожиданно
   - FPS не упал (сравни с baseline)
6. Не добавляй «quality step budget» или frame skipping в DDA — стены должны быть точными.
7. Static/dynamic split сохраняется: каскады кэшируются до изменения terrain/emission, динамический свет решается per-frame.
