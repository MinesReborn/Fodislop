# Handoff: SDR/HDR, освещение, debug UI

## Запрос и приоритет пользователя

Нужен чистый, простой и идиоматичный рендеринг Unity 6 (6000.6.0f1) с сохранением мощного собственного постпроцессинга: SDR и HDR до 1300 нит. HDR10+ нельзя объявлять реализованным: отдельная платформенная интеграция и проверка отсутствуют.

Пользователь отвергает временные подавляющие слои и обходы. Исправлять источник проблемы. Если необходимая операция запрещена, сообщить границу, не создавать обход.

## Ограничения

- Не управлять Unity, Editor, Hub, процессами или логами без конкретного разрешения в текущем сообщении. Разрешение «юзай мсп» ранее относилось к очистке Volume-профилей; не считать его постоянным разрешением.
- Не редактировать `.asset`, `.unity`, `.prefab` текстом. Только Editor API при разрешении.
- Не откатывать Git/файлы, не переписывать историю без прямого запроса.
- В дереве много параллельных пользовательских изменений. Не присваивать их себе и не затрагивать без необходимости. Коммитов агент не делал.
- Обычные исходники редактировать через apply_patch. Этот Markdown в корне прямо затребован пользователем.

## Архитектура, уже реализованная в исходниках

- Одна постоянная экранная камера Bootstrap через `IGameplayCamera`. Offscreen-камера планеты допустима отдельно.
- Свои scene-linear оптические эффекты, bloom и художественный грейдинг выполняются до штатного URP.
- URP Neutral / BT2390 владеет тонмаппингом и SDR/HDR-выводом.
- Свои экранные эффекты выполняются после URP postprocessing, до окончательного кодирования вывода.
- `PostProcessRenderPass` разделён на scene/display экземпляры, compute-шейдеры клонируются для изоляции keywords.
- Display pass нормализует абсолютные HDR-ниты на paper white, переводит gamut штатными HDR helpers, затем возвращает единицы URP.
- История temporal-эффектов разделена по стадиям; смена выходных параметров инвалидирует историю. Полноценной motion-vector reprojection нет.
- `HDROutputReconciler` создаёт runtime Volume только с Tonemapping и обновляет калибровку. Paper white ограничен 400 нит согласно параметру URP.
- Подписи роботов и floating chat перенесены в существующий UI Toolkit panel через `IWorldLabels` / `WorldLabels`, вместо дополнительной экранной камеры.

Ключевые файлы:

- `Assets/Scripts/Rendering/Settings/HDROutputReconciler.cs`
- `Assets/Scripts/Rendering/Settings/HDROutput.cs`
- `Assets/Scripts/Rendering/PostProcessing/Pipeline/PostProcessRendererFeature.cs`
- `Assets/Scripts/Rendering/PostProcessing/Pipeline/PostProcessRenderPass.cs`
- `Assets/Scripts/Rendering/PostProcessing/Pipeline/PostProcessPassExecutor.cs`
- `Assets/Resources/Shaders/PostProcessing/PostProcess.compute`
- `Assets/Resources/Shaders/PostProcessing/ColorGrading.hlsl`
- `Assets/Scripts/Rendering/PostProcessing/Scopes/`
- `Assets/Resources/Shaders/PostProcessing/Scopes.compute`

## Исправленные ошибки

1. HDR gamut запрашивался даже в SDR, что падало при отключённом HDR в Player Settings. В обоих проходах теперь условное чтение: активный HDR → `hdrDisplayColorGamut`, иначе `ColorGamut.sRGB`.
2. HDR helpers требовали Common.hlsl и Color.hlsl перед HDROutput.hlsl. Includes добавлены; локальный дубль `Luminance` удалён после подтверждённой ошибки Metal.
3. Scopes считали HDR gamut как Rec.709. Теперь перед измерением используется штатное преобразование в Rec.709; сигнал нормируется на настроенный peak. Это диагностическое представление, не проверка HDR10+.
4. Подписи роботов могли оставаться скрытыми после повторной активации; исправлено взаимодействие OnEnable/OnDisable и RobotCuller.

## Очистка штатных эффектов — завершена через MCP

Первоначально агент ошибочно добавил 17 нейтрализующих runtime overrides. Пользователь отверг это как костыль. Эти overrides и `ConfigureNeutralEffects` впоследствии УДАЛЕНЫ.

В `DefaultVolumeProfile.asset` действительно были Gaussian DepthOfField и ScreenSpaceLensFlare intensity=3, а также другие штатные компоненты. Через отдельный Editor menu item удалены штатные post effects, включая авторственный Tonemapping; runtime Tonemapping Bootstrap сохранён. Собственные компоненты и ProbeVolumesOptions сохранены, их дубли отдельно не чистились.

- Изменён `Assets/Settings/DefaultVolumeProfile.asset`: удалено 742 строки штатных subassets через Editor, не текстом.
- `PostProcessVolumeProfile.asset` и `MenuSceneryVolumeProfile.asset` не нуждались в изменениях.
- `Assets/Editor/Rendering/HDRSDRDualModeSetup.cs` содержит отдельные команды:
  - `Kern/Rendering/Clean Display Volume Profiles`
  - `Kern/Rendering/Validate Display Volume Profiles`
- Отдельная очистка не меняет Player Settings, сцены и URP asset; сохраняет только затронутые профили через SaveAssetIfDirty.
- Из старого setup удалён автоматический `InitializeOnLoadMethod`, менявший HDR Player Settings при загрузке. Явная широкая setup-команда остаётся; не запускать её для обычной проверки.
- `Assets/Scripts/Tests/Editor/Core/DisplayOutputProfileTests.cs` проверяет отсутствие штатных post effects в трёх авторственных профилях. Тест переписан с проверки временного подавления на проверку данных.
- В тестовый asmdef добавлены ссылки Core/Universal Render Pipeline Runtime.

Подтверждения: MCP-компиляция без ошибок (одно предупреждение про неиспользуемое `_sceneObjects` в PostProcessController); валидатор после повторной компиляции подтвердил чистоту всех трёх профилей.

ВАЖНО: MCP `execute_menu_item` может вернуть success даже при исключении внутри команды. Нужно проверять фактический результат и сообщения. Первый cleanup-запрос завершился timeout без сохранённых изменений; повторный успешно очистил профиль, что подтверждено diff и логом валидатора.

## Debug UI — последние изменения только в исходниках

Пользователь прислал скриншот: заголовки debug-окон накладываются на содержимое, текст мелкий на MacBook Retina.

Исправлено:

- `ToolTheme`: отдельный кешированный `WindowTitle` style, `contentOffset` окна обнулён.
- `ToolWindows`: GUI.Window получает GUIContent.none; заголовок рисует `ToolWindow` в выделенной верхней полосе с ограничением ширины до кнопок.
- Удалён вызов рисования scanlines поверх содержимого окна.
- Default scale debug UI на Retina увеличен с 1.35 до 2.0, общий масштаб игрового UI не менялся.
- `ToolbarWindow`: кнопки −/+ с шагом 0.25 и отображением процентов; подпись кешируется.
- `ToolLayoutStore`: масштаб сохраняется в существующем `tool_layout.json`, без PlayerPrefs; допустимый диапазон 1–2.5, старые файлы используют авто-default.
- Смена масштаба применяется на Layout, не посередине Repaint; hit testing использует общий Scale.

Файлы: `Assets/Scripts/Tools/IMGUI/ToolTheme.cs`, `ToolWindow.cs`, `ToolWindows.cs`, `ToolLayoutStore.cs`, `Windows/ToolbarWindow.cs`.

Внешняя компиляция C# прошла без ошибок. Визуальная проверка этих изменений НЕ выполнялась. До правок эти файлы уже содержали чужие изменения — сохранять их.

## Главная незавершённая задача: слишком тёмная сцена ДО тонкоррекции

Последняя содержательная жалоба пользователя: сцена до коррекции крайне слабо освещена, из неё невозможно получить информацию. Это ещё НЕ исследовано и НЕ исправлено.

Следующий агент должен проверить источник линейного сигнала, а не компенсировать проблему новой кривой:

1. Значения света в LightingEngine и передача в terrain/surface/entity shaders.
2. Умножение света на albedo: нет ли повторного затемнения, неверного sRGB/Linear преобразования, неподходящих единиц.
3. Форматы промежуточных RT, ранние saturate/clamp и потеря HDR-диапазона.
4. Реальный вход custom scene pass и значения до/после каждого шага.
5. Только после правильного освещения — художественный look.

Не утверждать заранее, что информация потеряна: низкий линейный сигнал в float RT может оставаться сохранным. Нужны измерения. GPU/capture/Editor проверки требуют нового конкретного разрешения.

## Текущий блокер Metal toolchain

Пользователь получил предупреждения `Scopes(Clone)` для VectorscopeResolve/WaveformResolve: Metal toolchain отсутствует или не работает.

Агент предложил `xcodebuild -downloadComponent MetalToolchain`, но пользователь выполнил команду и получил:

```text
xcode-select: error: tool 'xcodebuild' requires Xcode, but active developer directory '/Library/Developer/CommandLineTools' is a command line tools instance
```

Подтверждено только то, что выбран CommandLineTools. НЕ установлено, есть ли полный Xcode на диске. Нельзя утверждать, что Xcode отсутствует.

Следующий шаг при разрешённой работе с окружением: проверить наличие полного Xcode. Если установлен, использовать его Developer directory (возможно через DEVELOPER_DIR для одной команды, без глобальной смены xcode-select), затем загрузить MetalToolchain. Если полного Xcode нет — потребуется его установка пользователем/с отдельным разрешением. Ничего из этого агент не выполнял. Не повторять исходную команду без устранения причины.

Предупреждение toolchain — отдельная проблема от слабого света. Компиляция C# ничего не подтверждает о работоспособности Metal kernels. Шейдеры и HDR-картинка ещё не прошли полноценную верификацию.

## Прочие известные долги рендера

- Планета меню ранее тонмапилась/ограничивалась внутри shader и шла через ARGB32/offscreen SDR. Полный перенос в общую HDR-цепочку не завершён. Не менять вид планеты вслепую: по проектному контракту сначала screenshot, затем одна параметрическая ось, контрольный screenshot.
- Старые параметры собственной output curve (path-to-white, curve slope) УДАЛЕНЫ из кода и формата `color_grade.json` (версия 23; старые файлы читаются). Мёртвые слайдеры прибрані з воркбенча. Проверить остаточные bypass/solo/compare controls и не выдавать неработающие регуляторы за рабочие.
- PostProcessRenderPass всё ещё довольно тяжёлый, с двумя экземплярами, копиями и общими bindings. Оптимизацию не подменять новым слоем абстракций.
- HDR1300, переключение SDR/HDR и конечная яркость UI визуально/инструментально не подтверждены.

## Инструменты и проверки

- Фактический Unity: 6000.6.0f1, URP17.6.0.
- URP source: `Library/PackageCache/com.unity.render-pipelines.universal@8457e85b8184`.
- Core source: `Library/PackageCache/com.unity.render-pipelines.core@2d66c71e606e`.
- Source linter: `dotnet run --project tools/Kern.ArchitectureLinter --no-restore -- --rule KERN-DISPLAY-TRANSFORM` — проходил, защищает разделение стадий, includes, отсутствие дубля Luminance и guarded HDR gamut access.
- Временный внешний compile harness: `/private/tmp/kern-hdr-check.EMH9rP/Check.csproj`, лог `build.log`. Сборка runtime исходников без запуска Unity: 0 errors, 43 warnings. Это не полноценная asmdef/Unity/Metal верификация.
- Generated Unity csproj ранее содержали устаревшие пути; не путать ошибки окружения с ошибками исходников.
- Упоминавшийся ранее `tools/Kern.SettingsProbe` теперь отсутствует. Не заявлять повторный прогон 2771 теста: свежая попытка завершилась отсутствием пути.

## Начать следующую сессию

Прочитать актуальный git diff, не предполагать неизменность дерева. Приоритет — разобраться с Metal/Xcode блокером и исследовать слабое освещение ДО постпроцесса. Не заявлять весь HDR-рефактор завершённым. Не запускать Unity по старому разрешению, не делать rollback, не возвращать временные 17 overrides.
