# Visual workspace

`visual/` — независимый UI lab проекта. Он нужен для быстрых визуальных
итераций и генерации проверочных данных; это не Unity runtime и не источник
истины для production-рендера.

## Состав

- `kern-ui/` — статический browser UI lab.
- `kern-brand/` — бренд-книга и дизайн-код (`book/pages/`), токены для переноса (`tokens/export/`), иконки, витрины магазинов и страница загрузки (`distribution/`), соцсети (`social/`); файлы — `node kern-brand/export/tools/export.mjs`.

## Граница production

Изменение в `visual/` само по себе не меняет UI Toolkit в `Assets/`. Для
переноса результата нужны отдельные изменения в production UXML/USS/скриптах
и проверка через production-путь.

Подробная карта всего репозитория:
[`../.agents/repository-map.md`](../.agents/repository-map.md).
