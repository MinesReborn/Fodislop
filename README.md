# Fodinae

2D-клиент для [Fodinae](https://github.com/MinesReborn) — реворк клиента давно почившей MMORPG Сергея Мячина.

## Требования

- **Unity 6** (`6000.2.10f1`) — [скачать через Unity Hub](https://unity.com/releases/editor/archive)
- **Git** — для клонирования и подтягивания пакетных зависимостей

## Быстрый старт

```bash
git clone https://github.com/MinesReborn/Fodinae.git
```

1. Откройте проект через **Unity Hub** → `Open` → выберите папку проекта.
2. Unity автоматически подтянет внешние зависимости.
3. Откройте сцену `Assets/Scenes/SampleScene.unity`.
4. Нажмите **Play**. При отсутствии сервера включится автономный тестовый режим.

## Архитектура

Подробные инструкции для разработчиков и описание внутренних систем — в файле [**`AGENTS.md`**](AGENTS.md).

## Состояние проекта

Проект находится в активной разработке. Реализованы базовые системы:

- ✅ Тайловый рендеринг мира (один меш на весь террейн, UV-каналы)
- ✅ Сетевое взаимодействие через MinesServerNetworking
- ✅ Динамическая сборка UI из серверных пакетов
- ✅ Аудиосистема (фоновый звук, SFX)
- ✅ Инвентарь, HUD, экипировка
- ✅ Внутриигровой чат (глобальный, локальный, всплывающие сообщения)
- ✅ Миникарта и полноэкранная карта мира
- ✅ Меню паузы с настройками
- ✅ Эффекты на базе Effekseer и пул звуков/эффектов (SFXPool)
- ✅ Система визуального программирования (программатор)
- ✅ Система телепортации
- ✅ Автономный тестовый режим без сервера

### Git LFS

`.gitattributes` настроен — EOL-нормализация и Unity YAMLMerge для `.unity/.prefab/.asset` работают; мелкие тайлы осознанно НЕ под LFS. Открытый вопрос — только крупные бинарники (будущие большие текстуры/аудио/видео): если начнут расти, перевести их в LFS. Миграция уже закоммиченных бинарников (`git lfs migrate`, переписывает историю) — только по согласованию с командой.

## Зависимости

### Unity-пакеты (Git)

- [`darkar25.fodinae.data`](https://github.com/MinesReborn/MinesServerNetworking.git?path=/MinesServer.Data/) — типы данных
- [`darkar25.fodinae.networking`](https://github.com/MinesReborn/MinesServerNetworking.git?path=/MinesServer.Networking/) — сетевой протокол
- [`darkar25.fodinae.connection`](https://github.com/MinesReborn/MinesServerNetworking.git?path=/MinesServer.Connection/) — управление подключением
- [`com.netpyoung.webp`](https://github.com/netpyoung/unity.webp.git?path=unity_project/Assets/unity.webp) — декодирование WebP

### Vendored плагины (`Assets/Plugins/`)

- **Сжатие/кодинг**: SharpCompress, ZstdSharp, K4os.Compression.LZ4
- **Сеть**: NetCoreServer
- **Математика**: Genumerics, ExtendedNumerics.BigDecimal
- **UI/шаблоны**: SmartFormat, NCalc, Parlot, ZString
- **Асинхронность**: [UniTask](https://github.com/Cysharp/UniTask) (полный пакет)

## Contributing

Правила оформления PR и стандарты кода — в [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Лицензия

[MIT](LICENSE)
