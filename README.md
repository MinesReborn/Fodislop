# Fodinae

2D-клиент для [Fodinae](https://github.com/MinesReborn) — реворк клиента давно почившей MMORPG Сергея Мячина.

## Требования

- **Unity 6** (`6000.2.10f1`) — [скачать через Unity Hub](https://unity.com/releases/editor/archive)
- **Git** — для клонирования и подтягивания пакетных зависимостей

## Быстрый старт

```bash
git clone https://github.com/MinesReborn/Fodislop.git
```

1. Откройте проект через **Unity Hub** → `Open` → выберите папку проекта.
2. Unity автоматически подтянет внешние зависимости.
3. Откройте сцену `Assets/Scenes/SampleScene.unity`.
4. Нажмите **Play**. При отсутствии сервера включится автономный тестовый режим.

## Архитектура

Подробные инструкции для разработчиков и описание внутренних систем — в файле [**`AGENTS.md`**](AGENTS.md).

## Roadmap / TODO

- [ ] Формирование детального технического бэклога.
- [ ] Определение ключевых этапов разработки (Milestones).
- [ ] Анализ и приоритизация будущих функциональных модулей.

## Зависимости

### Unity-пакеты (Git)

- [`darkar25.fodinae.data`](https://github.com/MinesReborn/MinesServerNetworking.git?path=/MinesServer.Data/) — типы данных
- [`darkar25.fodinae.networking`](https://github.com/MinesReborn/MinesServerNetworking.git?path=/MinesServer.Networking/) — сетевой протокол
- [`com.netpyoung.webp`](https://github.com/netpyoung/unity.webp.git?path=unity_project/Assets/unity.webp) — декодирование WebP

### Vendored плагины (`Assets/Plugins/`)

- SharpCompress, ZstdSharp, K4os.Compression.LZ4, NetCoreServer, Genumerics
- [UniTask](https://github.com/Cysharp/UniTask) (полный пакет)

## Contributing

Правила оформления PR и стандарты кода — в [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Лицензия

[MIT](LICENSE)
