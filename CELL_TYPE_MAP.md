# Карта значений клетки

## Уже приходит из протокола

```text
CellConfigurationPacket
├─ Properties
│  ├─ Passable       ← gameplay: перемещение и определение фонового пола
│  ├─ Breakable      ← gameplay: возможность сломать
│  ├─ DropsShadow    ← рендер: тени соседних клеток
│  └─ Glowing        ← рендер: источник свечения из серверной конфигурации
├─ Distortion       ← рендер: Cause / Block для смещения terrain-узлов
├─ Animation        ← рендер: тип анимации текстуры
├─ AnimationSpeed   ← рендер
├─ FrameOffset      ← рендер
├─ Color            ← НЕ цвет клетки: сервер шлёт 0xFFFFFFFF заглушку
└─ ReliefGroup      ← рендер: связность и границы рельефа

WorldInitPacket
└─ TileGroups       ← серверная раскладка тайлов
```

## Уже введено на клиенте

```text
CellVisualProtocol (LegacyCellVisualProtocol)
├─ RoundableLoose       ← мягкий контур: песок / лава / кислота
├─ Road                 ← дорога пропускает свет
├─ CrystalSheet         ← непрерывный лист кристаллов
└─ RockSheet            ← непрерывный лист породы
```

Визуальные признаки уже централизованы; источник по умолчанию — legacy-списки CellType. Его можно заменить через `CellVisualProtocolRegistry`.

## Клиент знает сам

```text
Текстура и atlas
├─ Alpha и IsFullyOpaque
├─ UV, AtlasRect, AtlasIndex, UVTileSize
├─ число кадров фактической текстуры
├─ material/shader режимы
├─ выбор animation profile по CellType
├─ семейства декалей и каймы рельефа
└─ правила выборки и blending текстуры
```

Декали: ground — пустой foreground и background; stone — RedRock и NiggerRock. Кайма — у foreground с ненулевым `ReliefGroup`; блокам без фаски сервер задаёт группу 0.

## Геометрия

```text
Смещение узлов = Distortion из packet
Мягкий контур  = RoundableLoose из CellVisualProtocol
UV-лист        = CrystalSheet / RockSheet из CellVisualProtocol
```

Цель: передавать visual flags через серверный provider вместо legacy-списков. Серверу оставить `CellType` и gameplay-свойства; atlas и texture-данные остаются на клиенте.

## Цвет клетки

```text
Карта (мини-карта и большая)  = MapBlockColors — палитра клиента, источник правды
Текстура рельефа без арта     = тот же GetCellMinimapColor32
CellConfigurationPacket.Color = не читается для описанных типов
```

Сервер цвета клеток не моделирует вовсе: в `Cell.cs` нет поля цвета, в `cells.json` его нет, а `ProtocolWorld.BuildCellConfigurations` кладёт `unchecked((int)0xFFFFFFFF)` для всех 256 типов. Клиент разворачивал это в белый, и обе карты становились полностью белыми.

Поэтому палитра клиента — источник правды, и это не откат: цвет в протоколе пришлось бы сначала изобрести на сервере. `MapBlockColors` хранит точные `Color32`, ведёт `IsAuthored` на каждый тип и обязан описывать все 87 значений `CellType` — это проверяет тест `MapBlockColorsTests.EveryNamedCellType_HasAuthoredColor`, падающий при появлении новой породы в пакете `darkar25.fodinae.data`. Неназванные числовые значения получают magenta «нет данных», а не формулу.
