# Fodinae

2D-клиент для [Fodinae](https://github.com/MinesReborn) — тайловая игра с процедурным миром, сетевым мультиплеером и динамическим рендерингом террейна. TODO: получше описание.

## Скриншоты

> TODO: Добавить скриншоты геймплея.

## Требования

- **Unity 6** (`6000.2.10f1`) — [скачать через Unity Hub](https://unity.com/releases/editor/archive)
- **Git** — для клонирования и подтягивания пакетных зависимостей

## Быстрый старт

```bash
git clone https://github.com/MinesReborn/fodinae-audio-rt-engine.git
```

1. Откройте проект через **Unity Hub** → `Open` → выберите папку проекта.
2. Unity автоматически подтянет пакеты из `Packages/manifest.json` (включая `darkar25.fodinae.*`).
3. Откройте сцену `Assets/Scenes/SampleScene.unity`.
4. Нажмите **Play**. Если сервер недоступен, `StandaloneWorldInitializer` создаст тестовый мир.

## Архитектура

Подробное описание архитектуры, структуры проекта и стандартов разработки — в [`AGENTS.md`](AGENTS.md).

### Ключевые системы

| Система | Описание |
|---|---|
| `SingleMeshTerrainRenderer` | Рендеринг видимого террейна в один меш (7 UV-каналов) |
| `WorldLayer<T>` | Дисковый стриминг чанков 32×32 с LRU-кэшем и RLE-сжатием |
| `WorldTextureManager` | Загрузка тайл-текстур из файловой системы, упаковка в атласы |
| `NetworkService` | Подписка на серверные пакеты (`Subscribe<T>`) |
| `PacketUIBuilder` | Динамическая сборка UI из серверных пакетов |

## Зависимости

### Unity-пакеты (Git)

- [`darkar25.fodinae.data`](https://github.com/MinesReborn/MinesServerNetworking) — типы данных
- [`darkar25.fodinae.networking`](https://github.com/MinesReborn/MinesServerNetworking) — сетевой протокол
- [`com.netpyoung.webp`](https://github.com/netpyoung/unity.webp) — декодирование WebP

### Vendored плагины (`Assets/Plugins/`)

- SharpCompress, ZstdSharp, K4os.Compression.LZ4, NetCoreServer, Genumerics
- [UniTask](https://github.com/Cysharp/UniTask) (полный пакет)

## Контрибуция

См. [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Лицензия

[MIT](LICENSE)
