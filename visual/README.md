# Visual workspace

`visual/` — независимый UI lab проекта. Он нужен для быстрых визуальных
итераций и генерации проверочных данных; это не Unity runtime и не источник
истины для production-рендера.

## Состав

- `kern-ui/` — статический browser UI lab.
- `kern-brand/` — бренд-книга и дизайн-код (`book/pages/`), токены для переноса (`tokens/export/`), иконки, витрины магазинов и страница загрузки (`distribution/`), соцсети (`social/`). Экспортный сценарий запускается из корня репозитория: `node visual/kern-brand/export/tools/export.mjs`. Ему нужен локальный HTTP-сервер, запущенный из каталога над репозиторием, чтобы UI lab был доступен по `/Fodislop/visual` (этот путь зашит в сценарии). Например, в первом терминале из корня репозитория запусти `cd .. && python3 -m http.server 8765 --bind 127.0.0.1`, затем во втором — экспорт. Доступные режимы указаны в шапке скрипта.

## Граница production

Изменение в `visual/` само по себе не меняет UI Toolkit в `Assets/`. Для
переноса результата нужны отдельные изменения в production UXML/USS/скриптах
и проверка через production-путь.

Некоторые данные переносятся генераторами, а не копированием всей lab:

- `kern-ui/css/tokens.css` — исходник цепочки токенов. Сначала он
  преобразуется в `kern-brand/tokens/export/tokens.json`, `tokens.js` и
  `tokens.uss`; эти производные проверяет `--check`:

  ```sh
  node visual/kern-brand/tokens/export/build-tokens.mjs --check
  ```

  После изменения CSS запусти ту же команду без `--check`, чтобы обновить
  производные. Затем `emit-uss-tokens` переносит `tokens.uss` и три ссылки на
  SDF-шрифты в
  `Assets/Resources/UI/Theme/Tokens/Styles/KernTokens.uss`. Без `--check`
  команда перезаписывает production-файл; с флагом только сверяет его:

  ```sh
  dotnet run --project tools/generators/Kern.DesignSystem -- emit-uss-tokens --check
  ```
- SVG `<symbol>` в `kern-ui/index.html` — источник HUD-иконок. Из корня
  репозитория запусти:

  ```sh
  python3 -I visual/kern-ui/tools/emit-hud-icons.py .
  ```

  Скрипт перезаписывает production SVG и
  `Assets/Resources/UI/Theme/Kern/Styles/Icons.uss`, а для новых файлов создаёт
  `.meta`. Он не имеет режима проверки; не редактируй эти выходы вручную.

Старые component comparison/layout-fit отчёты требуют `component-map.json`.
В текущем `kern-ui/` его нет, поэтому соответствующие команды не могут заново
собрать отчёты из этого checkout; сохранённые числа остаются историческими.

Подробная карта всего репозитория:
[`../.agents/repository-map.md`](../.agents/repository-map.md).
