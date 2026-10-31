# Project Kern

2D-клиент для [Project Kern](https://github.com/MinesReborn) — реворк клиента давно почившей MMORPG Сергея Мячина.

## Быстрый старт

```bash
git clone --recurse-submodules https://github.com/MinesReborn/Fodislop.git
```

Для уже скачанного checkout инициализируй сетевой подмодуль:

```bash
git submodule update --init --recursive
```

Открой проект через **Unity Hub** с версией из
[`ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt), сейчас **6000.7.0b4 (beta)**.
Unity разрешает зависимости из [`Packages/manifest.json`](Packages/manifest.json);
сетевые `file:`-пакеты требуют предварительно загруженного подмодуля.

Открой `Assets/Scenes/Bootstrap.unity` и жми **Play**. Обычный путь запуска:
`Bootstrap` → `Gateway` → `MainMenu` → `MainGame`. Переходами управляет
`BootstrapLifetimeScope`: следующая сцена загружается аддитивно, предыдущая
остаётся до готовности её замены. Порядок сцен в Build Settings: 0, 1, 2, 3
соответственно. В Editor старт из другой сцены может передать её как целевую
через `PlayModeSceneBootstrapper`.

В чистой конфигурации включён **dummy/offline transport**. Для реального
подключения выбери серверный транспорт и задай `ServerHost`/`ServerPort`;
текущие сетевые дефолты — `127.0.0.1:8090`, HTTP API — порт `5460`.
Это параметры разработки, а не публичный production endpoint. Автоматического
release-гейта, запрещающего dummy/localhost, в текущем build-коде нет.

## Технологии

**Unity 6** (6000.7.0b4), **URP 2D 17.7.0**, C# 12, UI Toolkit,
FMOD Studio, UniTask и проектный модуль `Kern.Effects`.
Сеть: локальные UPM-пакеты `darkar25.kern.*` из подмодуля
[`MinesServerNetworking`](https://github.com/MinesReborn/MinesServerNetworking).

## Проверки и CI

Standalone-инструменты требуют .NET 10. Локальный набор проверок:

```bash
bash scripts/run-ci.sh
```

Состав и ограничения проверок описаны в [`tools/README.md`](tools/README.md).
GitHub Actions сейчас **не проверяет push и PR автоматически**: единственный
workflow [`CI`](.github/workflows/architecture-lint.yml) имеет только
`workflow_dispatch`, а его job `unified-ci` отключён через `if: false`.
Отсутствие checks не означает успешное прохождение тестов. Unity runtime,
GPU и готовность production-сборки проверяются отдельно.

Подробнее для разработчиков — в [**`AGENTS.md`**](AGENTS.md).

Карта директорий, границы asmdef и каталог проектной документации — в
[`.agents/repository-map.md`](.agents/repository-map.md) и
[`docs/index.html`](docs/index.html).

## Лицензия

[MIT](LICENSE)
