# Инвентарь игрового арта

Файл машинный: правки затираются. Пересобрать:

```
dotnet run --project tools/generators/Kern.DesignSystem -- inventory-art
```

Всего PNG: **402** в 214 семействах. Текстовых ассетов прочитано: 1409.

## Сводка по семействам

| семейство | шт | размеры | метрика | занятость | мягкость | оттенок, секторов | насыщ., разброс | без ссылок |
| --- | ---: | --- | ---: | --- | --- | ---: | ---: | ---: |
| `Assets/Resources/Effects/Crystals/Digits` | 1 | 192x12 | ok | 0,99–0,99 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Resources/Programmator/Icons/Actions/Buildings/Services` | 2 | 21x14, 21x17 | ok | 1,00–1,00 | 0,03–0,06 | 2/12 | 0,04 | 2/2 |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons` | 5 | 20x15, 19x19, 20x17 | ok | 1,00–1,00 | 0,00–0,02 | 4/12 | 0,30 | 5/5 |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building` | 5 | 13x13, 19x19 | ok | 1,00–1,00 | 0,00–0,06 | 2/12 | 0,16 | 5/5 |
| `Assets/Resources/Programmator/Icons/Actions/Effects/Audio` | 1 | 13x13 | ok | 0,72–0,72 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Selection` | 4 | 13x13 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 4/4 |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer` | 8 | 16x22 | ok | 1,00–1,00 | 0,00–0,00 | 2/12 | 0,17 | 8/8 |
| `Assets/Resources/Programmator/Icons/Actions/Items/Activation` | 4 | 20x17, 13x13 | ok | 1,00–1,00 | 0,00–0,08 | 3/12 | 0,64 | 4/4 |
| `Assets/Resources/Programmator/Icons/Actions/Mining/Digging` | 3 | 19x19, 13x13 | ok | 1,00–1,00 | 0,00–0,00 | 2/12 | 0,05 | 3/3 |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation` | 8 | 13x13, 16x13 | ok | 0,74–1,00 | 0,00–0,00 | 2/12 | 0,08 | 8/8 |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation` | 5 | 13x13 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 5/5 |
| `Assets/Resources/Programmator/Icons/Actions/Player/Recovery` | 4 | 19x19, 22x13, 21x13 … (+1) | ok | 1,00–1,00 | 0,00–0,03 | 1/12 | 0,01 | 4/4 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological` | 8 | 15x15 | ok | 1,00–1,00 | 0,00–0,06 | 6/12 | 0,50 | 8/8 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials` | 15 | 15x15 | ok | 1,00–1,00 | 0,00–0,06 | 4/12 | 0,61 | 15/15 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Mining` | 2 | 15x15 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 2/2 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Occupancy` | 3 | 15x15 | ok | 1,00–1,00 | 0,00–0,00 | 3/12 | 0,52 | 3/3 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Physics` | 3 | 15x15 | ok | 1,00–1,00 | 0,00–0,00 | 2/12 | 0,01 | 3/3 |
| `Assets/Resources/Programmator/Icons/Conditions/Logic/Boolean` | 2 | 19x19 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 2/2 |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Capacity` | 2 | 15x15 | ok | 1,00–1,00 | 0,00–0,06 | 2/12 | 0,14 | 2/2 |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Health` | 2 | 15x15 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 2/2 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Automation` | 4 | 15x13, 14x18, 16x18 | ok | 0,92–1,00 | 0,00–0,54 | 2/12 | 0,57 | 4/4 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation` | 8 | 27x28, 28x28 | ok | 0,89–0,96 | 0,00–0,00 | 1/12 | 0,23 | 8/8 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Return` | 2 | 27x28 | ok | 0,75–0,75 | 0,00–0,00 | 1/12 | 0,00 | 2/2 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Debugging` | 2 | 23x15 | ok | 1,00–1,00 | 0,06–0,06 | 1/12 | 0,03 | 2/2 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Events` | 4 | 11x11, 22x29 | ok | 0,80–1,00 | 0,00–0,00 | 2/12 | 0,40 | 4/4 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution` | 6 | 11x11, 30x10, 28x13 … (+1) | ok | 0,82–1,00 | 0,00–0,00 | 2/12 | 0,58 | 6/6 |
| `Assets/Resources/Programmator/Icons/ControlFlow/State` | 2 | 31x10, 16x10 | ok | 1,00–1,00 | 0,02–0,06 | 1/12 | 0,10 | 2/2 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Subroutines` | 4 | 31x10, 17x10, 16x10 | ok | 0,82–1,00 | 0,00–0,06 | 2/12 | 0,25 | 4/4 |
| `Assets/Resources/Programmator/Icons/Grid/Empty` | 1 | 27x7 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Constants` | 4 | 29x28 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,01 | 4/4 |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/State` | 4 | 29x26 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,01 | 4/4 |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Variables` | 4 | 26x28 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 4/4 |
| `Assets/Resources/Programmator/Icons/Memory/Assignment` | 3 | 26x23, 25x23, 29x28 | ok | 0,88–1,00 | 0,00–0,00 | 2/12 | 0,10 | 3/3 |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants` | 6 | 30x30, 29x30 | ok | 1,00–1,00 | 0,00–0,06 | 1/12 | 0,04 | 6/6 |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State` | 6 | 27x22, 28x22, 28x23 | ok | 0,91–1,00 | 0,00–0,00 | 1/12 | 0,08 | 6/6 |
| `Assets/Resources/Programmator/Icons/Memory/Rounding` | 3 | 25x19 | ok | 1,00–1,00 | 0,06–0,06 | 1/12 | 0,07 | 3/3 |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions` | 8 | 30x30 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 8/8 |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Relative` | 4 | 30x30 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 4/4 |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Directions` | 4 | 30x30 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 4/4 |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Relative` | 4 | 30x30 | ok | 1,00–1,00 | 0,00–0,02 | 1/12 | 0,00 | 4/4 |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved` | 12 | 15x15, 29x28, 23x28 … (+1) | ok | 0,00–1,00 | 0,00–0,06 | 4/12 | 0,45 | 12/12 |
| `Assets/Resources/Rendering/Calibration/Textures` | 1 | 512x512 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Resources/UI/HUD/Player/Skills` | 1 | 11x6 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Resources/UI/HUD/Player/Skills/Icons` | 23 | 73x73, 74x74, 74x73 … (+1) | **16** | 0,99–1,00 | 0,59–1,16 | 9/12 | 0,68 | 13/23 |
| `Assets/Textures/Items/Buildings/auto` | 1 | 42x42 | ok | 0,82–0,82 | 0,88–0,88 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/bombShop` | 1 | 42x42 | ok | 0,82–0,82 | 0,53–0,53 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/botSpot` | 1 | 42x42 | ok | 0,84–0,84 | 0,72–0,72 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/clans` | 1 | 42x42 | ok | 0,79–0,79 | 0,63–0,63 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/compressor` | 1 | 42x42 | ok | 0,79–0,79 | 0,94–0,94 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/constructionBot` | 1 | 42x42 | ok | 0,86–0,86 | 0,71–0,71 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/craft` | 1 | 42x42 | ok | 0,86–0,86 | 0,83–0,83 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/disassembler` | 1 | 42x42 | ok | 0,68–0,68 | 0,98–0,98 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/generator` | 1 | 42x42 | ok | 0,86–0,86 | 0,64–0,64 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/jobs` | 1 | 42x42 | ok | 0,91–0,91 | 0,88–0,88 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/market` | 1 | 42x42 | ok | 0,82–0,82 | 0,43–0,43 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/poly` | 1 | 42x42 | ok | 0,82–0,82 | 0,42–0,42 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/scienceCentre` | 1 | 42x42 | ok | 0,77–0,77 | 1,01–1,01 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Buildings/storage` | 1 | 42x42 | ok | 0,77–0,77 | 0,42–0,42 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Combat/c190` | 1 | 42x42 | ok | 0,88–0,88 | 0,71–0,71 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Combat/charge` | 1 | 42x42 | ok | 0,88–0,88 | 0,36–0,36 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Combat/gun` | 1 | 42x42 | ok | 0,82–0,82 | 0,94–0,94 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Items/Combat/plasmBomb` | 1 | 42x42 | ok | 0,86–0,86 | 0,71–0,71 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Combat/protonBomb` | 1 | 42x42 | ok | 0,88–0,88 | 0,38–0,38 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Combat/razBomb` | 1 | 42x42 | ok | 0,88–0,88 | 0,59–0,59 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Detection/aliveRadar` | 1 | 42x42 | ok | 0,91–0,91 | 0,78–0,78 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Detection/robotRadar` | 1 | 42x42 | ok | 0,91–0,91 | 0,82–0,82 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Detection/scanner` | 1 | 42x42 | ok | 0,41–0,41 | 0,74–0,74 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Detection/vulkanRadar` | 1 | 42x42 | ok | 0,91–0,91 | 0,78–0,78 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Economy/cred` | 1 | 42x42 | ok | 0,68–0,68 | 0,73–0,73 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Economy/currency` | 1 | 42x42 | ok | 0,54–0,54 | 0,62–0,62 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Economy/eMI` | 1 | 42x42 | ok | 0,63–0,63 | 0,98–0,98 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Items/Economy/fED` | 1 | 42x42 | ok | 0,84–0,84 | 0,60–0,60 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Items/Economy/oPP` | 1 | 42x42 | ok | 0,59–0,59 | 0,79–0,79 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Items/Geology/Instruments` | 10 | 42x42 | ok | 0,86–0,91 | 0,61–1,14 | 2/12 | 0,60 | 0/10 |
| `Assets/Textures/Items/Geology/Packs` | 1 | 42x42 | ok | 0,88–0,88 | 0,75–0,75 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Progression/freeUp` | 1 | 42x42 | ok | 0,68–0,68 | 0,86–0,86 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Progression/mineBooster` | 1 | 42x42 | ok | 0,86–0,86 | 0,75–0,75 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Progression/up` | 1 | 42x42 | ok | 0,86–0,86 | 0,62–0,62 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Items/Progression/upgradeBooster` | 1 | 42x42 | ok | 0,84–0,84 | 0,82–0,82 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Recovery/battery` | 1 | 42x42 | ok | 0,66–0,66 | 0,43–0,43 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Recovery/nano` | 1 | 42x42 | ok | 0,70–0,70 | 0,55–0,55 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Recovery/rem` | 1 | 42x42 | ok | 0,82–0,82 | 0,87–0,87 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Items/Travel/gate` | 1 | 42x42 | ok | 0,52–0,52 | 0,77–0,77 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Travel/portableTeleporter` | 1 | 42x42 | ok | 0,75–0,75 | 0,55–0,55 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Travel/resp` | 1 | 42x42 | ok | 0,88–0,88 | 1,32–1,32 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Travel/teleport` | 1 | 42x42 | ok | 0,86–0,86 | 0,94–0,94 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Items/Travel/trans` | 1 | 42x42 | ok | 0,66–0,66 | 0,63–0,63 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Backgrounds/Volcano` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Backgrounds/WithHeavyTraces` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Backgrounds/WithLightTraces` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Biological/Black` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Biological/Blue` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Biological/Cyan` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Biological/Red` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Biological/Violet` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Biological/White` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Boulders/BlackBoulder` | 3 | 32x32 | ok | 0,82–0,94 | 0,00–0,00 | 2/12 | 0,02 | 0/3 |
| `Assets/Textures/Server/Cells/Boulders/Boulder` | 3 | 32x32 | ok | 0,88–0,94 | 0,00–0,00 | 1/12 | 0,00 | 0/3 |
| `Assets/Textures/Server/Cells/Boulders/DeepMagmaBoulder` | 1 | 32x32 | ok | 0,88–0,88 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Boulders/MetalBoulder` | 3 | 32x32 | ok | 0,82–0,88 | 0,00–0,00 | 3/12 | 0,01 | 0/3 |
| `Assets/Textures/Server/Cells/Buildings/Corner` | 1 | 448x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Buildings/Door` | 1 | 448x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Buildings/Road` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Buildings/Wall` | 1 | 512x32 | ok | 0,94–0,94 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/GreenBlock` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/MilitaryBlock` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/MilitaryBlockFrame` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/QuadBlock` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/RedBlock` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/Support` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Construction/YellowBlock` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Crystals/Blue` | 2 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,04 | 0/2 |
| `Assets/Textures/Server/Cells/Crystals/Cyan` | 2 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 2/12 | 0,07 | 0/2 |
| `Assets/Textures/Server/Cells/Crystals/Green` | 2 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 2/12 | 0,04 | 0/2 |
| `Assets/Textures/Server/Cells/Crystals/Red` | 2 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 2/12 | 0,03 | 1/2 |
| `Assets/Textures/Server/Cells/Crystals/Violet` | 2 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,08 | 0/2 |
| `Assets/Textures/Server/Cells/Crystals/White` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Blue` | 1 | 32x160 | ok | 0,99–0,99 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Gray` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Passive` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Purple` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Yellow` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Liquids/Lava` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Roads/Golden` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Roads/Polymer` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Roads/Standard` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/Acid` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/Black` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/Deep` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Rock/DeepObsidian` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/DeepRainbow` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/DeepStriped` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/DeepTurquoise` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/GRock` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Rock/Golden` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Rock/Heavy` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Rock/LivingBlack` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Rock/Red` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Rock/Standard` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Sand/Black` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/Blue` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/DarkBlack` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/DarkBlue` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/DarkRusty` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/DarkWhite` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/DarkYellow` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/DeepLazurite` | 1 | 32x160 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/MilitaryBlock` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/Sand/Rusty` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Sand/White` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Sand/Yellow` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Cells/State/Empty` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/State/Unloaded` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/State/WorldEdge` | 1 | 320x320 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Cells/Structures/Storage` | 1 | 32x128 | ok | 0,85–0,85 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Clan/1` | 1 | 13x13 | ok | 0,53–0,53 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Crystals/blue` | 1 | 24x24 | ok | 0,92–0,92 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Crystals/cyan` | 1 | 24x24 | ok | 0,88–0,88 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Crystals/green` | 1 | 24x24 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Crystals/red` | 1 | 24x24 | ok | 0,92–0,92 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Crystals/violet` | 1 | 24x24 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Crystals/white` | 1 | 24x24 | ok | 0,69–0,69 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Pack/Clans/0` | 1 | 114x82 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Craft/0` | 1 | 50x47 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Gun/0` | 1 | 94x94 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Gun/1` | 1 | 94x94 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Gun/2` | 1 | 94x94 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Gun/3` | 1 | 94x94 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Gun/4` | 1 | 94x94 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Jobs/0` | 1 | 114x50 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Market/0` | 1 | 114x114 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Observatory/0` | 1 | 50x50 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Resp/0` | 1 | 50x87 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Resp/1` | 1 | 50x87 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Science/0` | 1 | 242x114 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/0` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/1` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/2` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/3` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/4` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/5` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/6` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Storage/7` | 1 | 50x18 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Teleport/0` | 1 | 50x50 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Teleport/1` | 1 | 50x50 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Pack/Up/0` | 1 | 50x79 | **1** | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Skin/Capsule` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/DarkCapsule` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Skin/Fatty` | 1 | 32x32 | ok | 0,85–0,85 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/GreenSkin` | 1 | 32x32 | ok | 0,74–0,74 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/Medic` | 1 | 32x32 | ok | 0,52–0,52 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/Mosquito` | 1 | 32x32 | ok | 0,45–0,45 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/OrangeSkin` | 1 | 32x32 | ok | 0,76–0,76 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/PurpleSkin` | 1 | 32x32 | ok | 0,85–0,85 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/RedSkin` | 1 | 32x32 | ok | 0,77–0,77 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/Standard` | 1 | 32x32 | ok | 0,74–0,74 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Skin/Widow` | 1 | 32x32 | ok | 0,70–0,70 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Tail/Blue` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Tail/Default` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Tail/Gold` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Tail/Green` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Tail/Red` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Tail/Violet` | 1 | 32x32 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/Visual/bz` | 1 | 32x960 | ok | 0,97–0,97 | 0,50–0,50 | 1/12 | 0,00 | 1/1 |
| `Assets/Textures/Server/Visual/death` | 1 | 64x2496 | ok | 0,79–0,79 | 0,05–0,05 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Server/World/Terrain/Decals` | 2 | 512x32 | ok | 0,33–0,71 | 1,10–1,19 | 2/12 | 0,35 | 0/2 |
| `Assets/Textures/Surface/basalt` | 1 | 1254x1254 | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Surface/moon` | 1 | 1287x1222 | ok | 0,87–0,87 | 0,02–0,02 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Surface/ridge-a` | 1 | 2172x724 | ok | 0,97–0,97 | 0,05–0,05 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Surface/ridge-b` | 1 | 2172x724 | ok | 1,00–1,00 | 0,06–0,06 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/Surface/ridge-c` | 1 | 2172x724 | ok | 0,97–0,97 | 0,05–0,05 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/UI/Controls/Slider` | 1 | 24x24 | ok | 0,88–0,88 | 0,00–0,00 | 1/12 | 0,00 | 0/1 |
| `Assets/Textures/UI/Controls/Toggle/States` | 4 | 259x253, 259x260, 260x260 … (+1) | ok | 1,00–1,00 | 0,00–0,00 | 1/12 | 0,06 | 1/4 |
| `Assets/Textures/World/Surface/Materials` | 2 | 480x329, 480x47 | ok | 1,00–1,00 | 0,00–0,03 | 1/12 | 0,04 | 0/2 |

Колонки «метрика» и «без ссылок» отвечают числом: жирное число — столько ассетов просит объяснения, `ok` — объяснять нечего.

«Без ссылок» — не приговор. Programmator и Cells грузятся вычисленным путём, поэтому литеральной ссылки у них нет по устройству. Выход за метрику — тоже не всегда дефект: числа нужны арт-библии, а не приговору.

## Разбивка по оттенку

Сектор — 30°. Считаются только непрозрачные пиксели.

| семейство | 0–30° | 30–60° | 60–90° | 90–120° | 120–150° | 150–180° | 180–210° | 210–240° | 240–270° | 270–300° | 300–330° | 330–360° |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `Assets/Resources/Effects/Crystals/Digits` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Buildings/Services` | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons` | 0 | 2 | 1 | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building` | 0 | 4 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Effects/Audio` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Selection` | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer` | 0 | 6 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Items/Activation` | 0 | 1 | 0 | 2 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Mining/Digging` | 0 | 2 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation` | 0 | 7 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation` | 0 | 5 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Actions/Player/Recovery` | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological` | 0 | 1 | 0 | 2 | 1 | 2 | 1 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials` | 0 | 0 | 2 | 6 | 5 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Mining` | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Occupancy` | 0 | 0 | 0 | 0 | 1 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Physics` | 0 | 0 | 0 | 0 | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Logic/Boolean` | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Capacity` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Health` | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Automation` | 3 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation` | 8 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Return` | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Debugging` | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Events` | 0 | 0 | 0 | 0 | 0 | 0 | 2 | 2 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution` | 5 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/State` | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/ControlFlow/Subroutines` | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Grid/Empty` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Constants` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/State` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 4 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Variables` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 4 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Assignment` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 2 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 6 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 6 | 0 |
| `Assets/Resources/Programmator/Icons/Memory/Rounding` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 3 | 0 |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions` | 0 | 0 | 0 | 0 | 0 | 8 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Relative` | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Directions` | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Relative` | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved` | 2 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 1 | 0 | 2 | 0 |
| `Assets/Resources/Rendering/Calibration/Textures` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/UI/HUD/Player/Skills` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Resources/UI/HUD/Player/Skills/Icons` | 3 | 1 | 1 | 4 | 3 | 7 | 0 | 1 | 1 | 0 | 2 | 0 |
| `Assets/Textures/Items/Buildings/auto` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/bombShop` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/botSpot` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/clans` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Items/Buildings/compressor` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/constructionBot` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/craft` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Items/Buildings/disassembler` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/generator` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Items/Buildings/jobs` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/market` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/poly` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/scienceCentre` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Items/Buildings/storage` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Combat/c190` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Combat/charge` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Combat/gun` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Combat/plasmBomb` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Combat/protonBomb` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Items/Combat/razBomb` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Detection/aliveRadar` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Detection/robotRadar` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Items/Detection/scanner` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Detection/vulkanRadar` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Items/Economy/cred` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Economy/currency` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Economy/eMI` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Economy/fED` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Economy/oPP` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Items/Geology/Instruments` | 1 | 9 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Geology/Packs` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Progression/freeUp` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Items/Progression/mineBooster` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Progression/up` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Progression/upgradeBooster` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Recovery/battery` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Recovery/nano` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Recovery/rem` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Travel/gate` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Travel/portableTeleporter` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Travel/resp` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Items/Travel/teleport` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Items/Travel/trans` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Cells/Backgrounds/Volcano` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Backgrounds/WithHeavyTraces` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Backgrounds/WithLightTraces` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Biological/Black` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 |
| `Assets/Textures/Server/Cells/Biological/Blue` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Biological/Cyan` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Biological/Red` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Biological/Violet` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Cells/Biological/White` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Boulders/BlackBoulder` | 2 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Boulders/Boulder` | 0 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Boulders/DeepMagmaBoulder` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Server/Cells/Boulders/MetalBoulder` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 0 | 1 |
| `Assets/Textures/Server/Cells/Buildings/Corner` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Buildings/Door` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Buildings/Road` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Cells/Buildings/Wall` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/GreenBlock` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/MilitaryBlock` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/MilitaryBlockFrame` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/QuadBlock` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/RedBlock` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/Support` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Construction/YellowBlock` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Crystals/Blue` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Crystals/Cyan` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Crystals/Green` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Crystals/Red` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Server/Cells/Crystals/Violet` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 |
| `Assets/Textures/Server/Cells/Crystals/White` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Blue` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Gray` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Passive` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Purple` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Cells/Liquids/Acid/Yellow` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Liquids/Lava` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Roads/Golden` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Roads/Polymer` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Roads/Standard` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Acid` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Black` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Deep` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/DeepObsidian` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/DeepRainbow` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/DeepStriped` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/DeepTurquoise` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/GRock` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Golden` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Heavy` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Server/Cells/Rock/LivingBlack` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Red` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Rock/Standard` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/Black` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/Blue` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/DarkBlack` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/DarkBlue` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/DarkRusty` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/DarkWhite` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/DarkYellow` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/DeepLazurite` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/MilitaryBlock` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/Rusty` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/White` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Sand/Yellow` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/State/Empty` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/State/Unloaded` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/State/WorldEdge` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Cells/Structures/Storage` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Clan/1` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Crystals/blue` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Crystals/cyan` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Crystals/green` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Crystals/red` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Server/Crystals/violet` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 |
| `Assets/Textures/Server/Crystals/white` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Clans/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Craft/0` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Server/Pack/Gun/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Gun/1` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Gun/2` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Gun/3` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Gun/4` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Jobs/0` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Market/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Observatory/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Resp/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Resp/1` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Science/0` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/1` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/2` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/3` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/4` | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/5` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/6` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Storage/7` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Teleport/0` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Pack/Teleport/1` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Pack/Up/0` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/Capsule` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/DarkCapsule` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/Fatty` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/GreenSkin` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/Medic` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/Mosquito` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 |
| `Assets/Textures/Server/Skin/OrangeSkin` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/PurpleSkin` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/RedSkin` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/Standard` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Skin/Widow` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Tail/Blue` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Tail/Default` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Tail/Gold` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Tail/Green` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Tail/Red` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Tail/Violet` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Server/Visual/bz` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/Visual/death` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Server/World/Terrain/Decals` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Surface/basalt` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Surface/moon` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Surface/ridge-a` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Surface/ridge-b` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Surface/ridge-c` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/UI/Controls/Slider` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/UI/Controls/Toggle/States` | 0 | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/World/Surface/Materials` | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

## Все файлы

Занятость — площадь непрозрачного bbox к площади холста. Мягкость — насколько рамка bbox полупрозрачна. Поля — отступы bbox от краёв. Ссылка: `guid`, `имя` или `нет`.

| файл | размер | метрика | занятость | мягкость | поля (л,в,п,н) | оттенок | насыщ. | светлота | ссылка |
| --- | --- | --- | ---: | ---: | --- | ---: | ---: | ---: | --- |
| `Assets/Resources/Effects/Crystals/Digits/CrystalDigits.png` | 192x12 | — | 0,99 | 0,00 | 0, 0, 2, 0 | 105° | 0,20 | 0,31 | имя |
| `Assets/Resources/Programmator/Icons/Actions/Buildings/Services/171.png` | 21x14 | — | 1,00 | 0,03 | 0, 0, 0, 0 | 107° | 0,67 | 0,83 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Buildings/Services/172.png` | 21x17 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 20° | 0,63 | 0,80 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons/147.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 56° | 0,57 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons/162.png` | 20x15 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 68° | 0,78 | 0,88 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons/163.png` | 20x15 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 96° | 0,77 | 0,88 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons/164.png` | 20x15 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 58° | 0,78 | 0,88 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Combat/Weapons/169.png` | 20x17 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 166° | 0,88 | 0,89 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building/142.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building/165.png` | 13x13 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 102° | 0,74 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building/17.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building/19.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Construction/Building/21.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Effects/Audio/23.png` | 13x13 | — | 0,72 | 0,00 | 1, 1, 1, 1 | 24° | 1,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Selection/175.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,62 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Selection/176.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,62 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Selection/177.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,62 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Selection/178.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,62 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/89.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 57° | 0,46 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/90.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,46 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/91.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 52° | 0,40 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/92.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 61° | 0,53 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/93.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 43° | 0,55 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/94.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 54° | 0,57 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/95.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 65° | 0,54 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Inventory/Transfer/96.png` | 16x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 42° | 0,54 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Items/Activation/167.png` | 20x17 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 104° | 0,24 | 0,57 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Items/Activation/168.png` | 20x17 | — | 1,00 | 0,08 | 0, 0, 0, 0 | 250° | 0,88 | 0,88 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Items/Activation/170.png` | 20x17 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 104° | 0,51 | 0,59 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Items/Activation/18.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Mining/Digging/141.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Mining/Digging/145.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 65° | 0,63 | 0,99 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Mining/Digging/8.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/10.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/11.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/12.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/144.png` | 16x13 | — | 0,74 | 0,00 | 1, 1, 1, 1 | 232° | 0,50 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/15.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/16.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/22.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Rotation/9.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation/14.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation/4.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation/5.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation/6.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Movement/Translation/7.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Player/Recovery/143.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Player/Recovery/173.png` | 22x13 | — | 1,00 | 0,03 | 0, 0, 0, 0 | 55° | 0,59 | 0,72 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Player/Recovery/174.png` | 21x13 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 60° | 0,59 | 0,72 | нет |
| `Assets/Resources/Programmator/Icons/Actions/Player/Recovery/20.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/47.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 152° | 0,95 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/66.png` | 15x15 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 169° | 0,75 | 0,69 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/67.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 115° | 0,45 | 0,71 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/68.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 49° | 0,87 | 0,74 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/69.png` | 15x15 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 346° | 0,78 | 0,70 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/70.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 117° | 0,88 | 0,66 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/71.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 208° | 0,82 | 0,76 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Biological/72.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 135° | 0,90 | 0,83 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/46.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 151° | 0,95 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/52.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 98° | 0,53 | 0,54 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/53.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 118° | 0,40 | 0,44 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/54.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 153° | 0,95 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/56.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 141° | 0,50 | 0,66 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/57.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 137° | 0,43 | 0,84 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/58.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 137° | 0,68 | 0,83 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/59.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 79° | 1,00 | 0,93 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/60.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 104° | 1,00 | 0,88 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/63.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 97° | 0,78 | 0,69 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/64.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 102° | 0,39 | 0,54 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/65.png` | 15x15 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 73° | 0,77 | 0,66 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/74.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 90° | 0,63 | 0,85 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/76.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 132° | 0,51 | 0,72 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Materials/77.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 121° | 1,00 | 0,93 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Mining/50.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 155° | 0,94 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Mining/51.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 154° | 0,94 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Occupancy/146.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 181° | 0,44 | 0,83 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Occupancy/43.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 157° | 0,93 | 0,92 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Occupancy/44.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 148° | 0,96 | 0,90 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Physics/45.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 151° | 0,95 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Physics/48.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 150° | 0,96 | 0,90 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Cells/Physics/49.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 151° | 0,95 | 0,91 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Logic/Boolean/38.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 137° | 1,00 | 0,87 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Logic/Boolean/39.png` | 19x19 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 137° | 1,00 | 0,87 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Capacity/78.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 282° | 0,17 | 0,99 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Capacity/79.png` | 15x15 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 200° | 0,03 | 0,97 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Health/148.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,55 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Conditions/Player/Health/149.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,55 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Automation/158.png` | 15x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,52 | 0,86 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Automation/159.png` | 15x13 | — | 0,92 | 0,02 | 0, 0, 0, 1 | 0° | 0,00 | 0,56 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Automation/179.png` | 14x18 | — | 1,00 | 0,54 | 0, 0, 0, 0 | 137° | 0,57 | 0,68 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Automation/180.png` | 16x18 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,36 | 0,76 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/139.png` | 27x28 | — | 0,96 | 0,00 | 0, 1, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/140.png` | 27x28 | — | 0,96 | 0,00 | 0, 1, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/150.png` | 27x28 | — | 0,89 | 0,00 | 0, 3, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/151.png` | 27x28 | — | 0,89 | 0,00 | 0, 3, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/152.png` | 27x28 | — | 0,96 | 0,00 | 0, 1, 0, 0 | 0° | 0,21 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/153.png` | 27x28 | — | 0,96 | 0,00 | 0, 1, 0, 0 | 0° | 0,21 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/154.png` | 28x28 | — | 0,96 | 0,00 | 0, 1, 0, 0 | 0° | 0,23 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Navigation/155.png` | 27x28 | — | 0,96 | 0,00 | 0, 1, 0, 0 | 0° | 0,23 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Return/41.png` | 27x28 | — | 0,75 | 0,00 | 0, 3, 0, 4 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Branches/Return/42.png` | 27x28 | — | 0,75 | 0,00 | 0, 3, 0, 4 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Debugging/181.png` | 23x15 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 124° | 0,79 | 0,54 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Debugging/182.png` | 23x15 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 124° | 0,83 | 0,52 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Events/166.png` | 22x29 | — | 0,80 | 0,00 | 1, 1, 2, 1 | 208° | 0,42 | 0,99 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Events/81.png` | 11x11 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 210° | 0,70 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Events/82.png` | 11x11 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 220° | 0,67 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Events/83.png` | 11x11 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 183° | 0,82 | 0,95 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution/1.png` | 28x13 | — | 0,82 | 0,00 | 0, 1, 1, 1 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution/13.png` | 13x13 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 58° | 0,58 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution/2.png` | 11x11 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,54 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution/24.png` | 30x10 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution/3.png` | 11x11 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,54 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Execution/40.png` | 30x10 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/State/137.png` | 31x10 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 0° | 0,19 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/State/138.png` | 16x10 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 0° | 0,29 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Subroutines/25.png` | 31x10 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Subroutines/26.png` | 31x10 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 239° | 0,16 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Subroutines/27.png` | 17x10 | — | 0,82 | 0,00 | 1, 0, 2, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/ControlFlow/Subroutines/28.png` | 16x10 | — | 1,00 | 0,04 | 0, 0, 0, 0 | 239° | 0,25 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Grid/Empty/0.png` | 27x7 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Constants/100.png` | 29x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 259° | 0,72 | 0,99 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Constants/101.png` | 29x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 264° | 0,72 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Constants/102.png` | 29x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 259° | 0,72 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Constants/103.png` | 29x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 265° | 0,71 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/State/104.png` | 29x26 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 316° | 0,72 | 0,98 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/State/105.png` | 29x26 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 322° | 0,73 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/State/106.png` | 29x26 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 305° | 0,72 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/State/107.png` | 29x26 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 316° | 0,71 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Variables/108.png` | 26x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 314° | 1,00 | 0,99 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Variables/109.png` | 26x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 316° | 1,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Variables/110.png` | 26x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 309° | 1,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Arithmetic/Variables/111.png` | 26x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 314° | 1,00 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Assignment/97.png` | 26x23 | — | 0,88 | 0,00 | 0, 1, 1, 1 | 312° | 0,60 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Assignment/98.png` | 25x23 | — | 0,92 | 0,00 | 1, 0, 0, 1 | 312° | 0,61 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Assignment/99.png` | 29x28 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 262° | 0,70 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants/119.png` | 29x30 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 265° | 0,64 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants/120.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 265° | 0,64 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants/121.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 265° | 0,62 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants/122.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 265° | 0,62 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants/123.png` | 30x30 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 265° | 0,62 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/Constants/124.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 265° | 0,60 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State/112.png` | 27x22 | — | 0,91 | 0,00 | 0, 0, 0, 2 | 312° | 0,73 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State/113.png` | 27x22 | — | 0,91 | 0,00 | 0, 0, 0, 2 | 312° | 0,73 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State/114.png` | 27x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 312° | 0,68 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State/115.png` | 27x22 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 312° | 0,68 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State/116.png` | 28x22 | — | 0,91 | 0,00 | 0, 0, 0, 2 | 312° | 0,70 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Comparison/State/117.png` | 28x23 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 312° | 0,65 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Rounding/125.png` | 25x19 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 312° | 0,43 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Rounding/126.png` | 25x19 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 312° | 0,49 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Memory/Rounding/127.png` | 25x19 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 312° | 0,49 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/29.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/30.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/31.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/32.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/33.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/35.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/36.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Directions/37.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Relative/135.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Relative/156.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Relative/157.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Cells/Relative/34.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Directions/131.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Directions/132.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Directions/133.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Directions/134.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Relative/136.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Relative/86.png` | 30x30 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Relative/87.png` | 30x30 | — | 1,00 | 0,02 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Observer/Offsets/Relative/88.png` | 30x30 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 172° | 0,88 | 0,96 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/118.png` | 23x28 | — | 0,00 | 0,00 | 0, 0, 0, 0 | — | 0,00 | 0,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/128.png` | 29x28 | — | 1,00 | 0,05 | 0, 0, 0, 0 | 265° | 0,66 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/129.png` | 29x28 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 307° | 0,87 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/130.png` | 29x28 | — | 0,96 | 0,06 | 0, 1, 0, 0 | 312° | 0,69 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/55.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 137° | 1,00 | 0,87 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/61.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,55 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/62.png` | 15x15 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,55 | 1,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/73.png` | 14x15 | — | 0,04 | 0,06 | 0, 5, 13, 2 | 135° | 0,98 | 0,87 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/75.png` | 15x15 | — | 0,00 | 0,00 | 0, 0, 0, 0 | — | 0,00 | 0,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/80.png` | 15x15 | — | 0,00 | 0,00 | 0, 0, 0, 0 | — | 0,00 | 0,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/84.png` | 30x30 | — | 0,00 | 0,00 | 0, 0, 0, 0 | — | 0,00 | 0,00 | нет |
| `Assets/Resources/Programmator/Icons/Protocol/Reserved/85.png` | 31x30 | — | 0,00 | 0,06 | 0, 17, 30, 9 | 135° | 0,98 | 0,87 | нет |
| `Assets/Resources/Rendering/Calibration/Textures/CalibrationMark.png` | 512x512 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,37 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/BuildGreen.png` | 73x73 | ok | 1,00 | 1,04 | 0, 0, 0, 0 | 161° | 0,29 | 0,66 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/BuildRed.png` | 73x74 | violation (73x73) | 1,00 | 0,96 | 0, 0, 0, 0 | 165° | 0,24 | 0,65 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/BuildRoad.png` | 72x73 | violation (73x73) | 1,00 | 1,06 | 0, 0, 0, 0 | 161° | 0,26 | 0,61 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/BuildSupport.png` | 72x73 | violation (73x73) | 1,00 | 1,08 | 0, 0, 0, 0 | 166° | 0,20 | 0,66 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/BuildWar.png` | 73x73 | ok | 1,00 | 0,87 | 0, 0, 0, 0 | 125° | 0,27 | 0,68 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/BuildYellow.png` | 74x73 | violation (73x73) | 1,00 | 1,04 | 0, 0, 0, 0 | 158° | 0,29 | 0,66 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Cooling.png` | 74x74 | violation (73x73) | 1,00 | 0,99 | 0, 0, 0, 0 | 220° | 0,69 | 0,86 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Digging.png` | 74x74 | violation (73x73) | 1,00 | 0,95 | 0, 0, 0, 0 | 53° | 0,85 | 0,88 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Extraction.png` | 73x73 | ok | 1,00 | 0,81 | 0, 0, 0, 0 | 23° | 0,75 | 0,73 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/GunProtection.png` | 74x74 | violation (73x73) | 1,00 | 0,97 | 0, 0, 0, 0 | 309° | 0,87 | 0,90 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Health.png` | 74x73 | violation (73x73) | 1,00 | 1,02 | 0, 0, 0, 0 | 317° | 0,81 | 0,85 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Induction.png` | 74x73 | violation (73x73) | 1,00 | 0,59 | 0, 0, 0, 0 | 250° | 0,57 | 0,89 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/MineGeneral.png` | 74x74 | violation (73x73) | 1,00 | 1,05 | 0, 0, 0, 0 | 23° | 0,75 | 0,73 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Movement.png` | 73x73 | ok | 1,00 | 1,03 | 0, 0, 0, 0 | 158° | 0,85 | 0,88 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Packing.png` | 73x73 | ok | 1,00 | 1,03 | 0, 0, 0, 0 | 101° | 0,80 | 0,84 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/PackingBlue.png` | 74x73 | violation (73x73) | 1,00 | 1,03 | 0, 0, 0, 0 | 129° | 0,86 | 0,78 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/PackingCyan.png` | 74x74 | violation (73x73) | 1,00 | 0,93 | 0, 0, 0, 0 | 121° | 0,83 | 0,77 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/PackingGreen.png` | 73x73 | ok | 1,00 | 1,00 | 0, 0, 0, 0 | 110° | 0,87 | 0,80 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/PackingRed.png` | 73x73 | ok | 1,00 | 1,16 | 0, 0, 0, 0 | 75° | 0,83 | 0,84 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/PackingViolet.png` | 74x74 | violation (73x73) | 1,00 | 1,05 | 0, 0, 0, 0 | 92° | 0,83 | 0,76 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/PackingWhite.png` | 73x72 | violation (73x73) | 1,00 | 0,65 | 0, 0, 0, 0 | 106° | 0,60 | 0,77 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/Repair.png` | 73x74 | violation (73x73) | 0,99 | 0,97 | 0, 0, 0, 1 | 6° | 0,57 | 0,86 | имя |
| `Assets/Resources/UI/HUD/Player/Skills/Icons/RoadMovement.png` | 74x74 | violation (73x73) | 1,00 | 0,90 | 0, 0, 0, 0 | 156° | 0,82 | 0,87 | нет |
| `Assets/Resources/UI/HUD/Player/Skills/up_word.png` | 11x6 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 1,00 | нет |
| `Assets/Textures/Items/Buildings/auto/auto.png` | 42x42 | ok | 0,82 | 0,88 | 1, 3, 1, 3 | 36° | 0,38 | 0,48 | имя |
| `Assets/Textures/Items/Buildings/bombShop/bombShop.png` | 42x42 | ok | 0,82 | 0,53 | 3, 1, 3, 1 | 49° | 0,55 | 0,68 | имя |
| `Assets/Textures/Items/Buildings/botSpot/botSpot.png` | 42x42 | ok | 0,84 | 0,72 | 2, 1, 3, 1 | 60° | 0,00 | 0,24 | имя |
| `Assets/Textures/Items/Buildings/clans/clans.png` | 42x42 | ok | 0,79 | 0,63 | 1, 3, 1, 4 | 360° | 0,14 | 0,49 | имя |
| `Assets/Textures/Items/Buildings/compressor/compressor.png` | 42x42 | ok | 0,79 | 0,94 | 3, 1, 4, 1 | 50° | 0,30 | 0,39 | имя |
| `Assets/Textures/Items/Buildings/constructionBot/constructionBot.png` | 42x42 | ok | 0,86 | 0,71 | 1, 2, 1, 2 | 189° | 0,46 | 0,31 | имя |
| `Assets/Textures/Items/Buildings/craft/craft.png` | 42x42 | ok | 0,86 | 0,83 | 2, 1, 2, 1 | 356° | 0,47 | 0,76 | имя |
| `Assets/Textures/Items/Buildings/disassembler/disassembler.png` | 42x42 | ok | 0,68 | 0,98 | 6, 1, 6, 1 | 91° | 0,72 | 0,50 | имя |
| `Assets/Textures/Items/Buildings/generator/generator.png` | 42x42 | ok | 0,86 | 0,64 | 2, 1, 2, 1 | 356° | 0,49 | 0,59 | имя |
| `Assets/Textures/Items/Buildings/jobs/jobs.png` | 42x42 | ok | 0,91 | 0,88 | 1, 1, 1, 1 | 175° | 0,64 | 0,71 | имя |
| `Assets/Textures/Items/Buildings/market/market.png` | 42x42 | ok | 0,82 | 0,43 | 1, 3, 1, 3 | 43° | 0,60 | 0,66 | имя |
| `Assets/Textures/Items/Buildings/poly/poly.png` | 42x42 | ok | 0,82 | 0,42 | 3, 1, 3, 1 | 120° | 0,79 | 0,48 | имя |
| `Assets/Textures/Items/Buildings/scienceCentre/scienceCentre.png` | 42x42 | ok | 0,77 | 1,01 | 1, 4, 1, 4 | 248° | 0,20 | 0,45 | имя |
| `Assets/Textures/Items/Buildings/storage/storage.png` | 42x42 | ok | 0,77 | 0,42 | 1, 4, 1, 4 | 61° | 0,18 | 0,36 | имя |
| `Assets/Textures/Items/Combat/c190/c190.png` | 42x42 | ok | 0,88 | 0,71 | 1, 1, 2, 1 | 174° | 0,45 | 0,43 | имя |
| `Assets/Textures/Items/Combat/charge/charge.png` | 42x42 | ok | 0,88 | 0,36 | 1, 1, 1, 2 | 235° | 0,66 | 0,58 | имя |
| `Assets/Textures/Items/Combat/gun/gun.png` | 42x42 | ok | 0,82 | 0,94 | 3, 1, 3, 1 | 29° | 0,53 | 0,29 | нет |
| `Assets/Textures/Items/Combat/plasmBomb/plasmBomb.png` | 42x42 | ok | 0,86 | 0,71 | 2, 1, 2, 1 | 34° | 0,70 | 0,68 | имя |
| `Assets/Textures/Items/Combat/protonBomb/protonBomb.png` | 42x42 | ok | 0,88 | 0,38 | 1, 1, 2, 1 | 270° | 0,65 | 0,67 | имя |
| `Assets/Textures/Items/Combat/razBomb/razBomb.png` | 42x42 | ok | 0,88 | 0,59 | 1, 1, 2, 1 | 162° | 0,75 | 0,62 | имя |
| `Assets/Textures/Items/Detection/aliveRadar/aliveRadar.png` | 42x42 | ok | 0,91 | 0,78 | 1, 1, 1, 1 | 182° | 0,92 | 0,33 | имя |
| `Assets/Textures/Items/Detection/robotRadar/robotRadar.png` | 42x42 | ok | 0,91 | 0,82 | 1, 1, 1, 1 | 271° | 0,89 | 0,38 | имя |
| `Assets/Textures/Items/Detection/scanner/scanner.png` | 42x42 | ok | 0,41 | 0,74 | 12, 1, 12, 1 | 106° | 0,26 | 0,23 | имя |
| `Assets/Textures/Items/Detection/vulkanRadar/vulkanRadar.png` | 42x42 | ok | 0,91 | 0,78 | 1, 1, 1, 1 | 359° | 0,86 | 0,37 | имя |
| `Assets/Textures/Items/Economy/cred/cred.png` | 42x42 | ok | 0,68 | 0,73 | 6, 1, 6, 1 | 45° | 0,71 | 0,47 | имя |
| `Assets/Textures/Items/Economy/currency/currency.png` | 42x42 | ok | 0,54 | 0,62 | 9, 1, 9, 1 | 80° | 0,69 | 0,67 | имя |
| `Assets/Textures/Items/Economy/eMI/eMI.png` | 42x42 | ok | 0,63 | 0,98 | 7, 1, 7, 1 | 209° | 0,03 | 0,26 | нет |
| `Assets/Textures/Items/Economy/fED/fED.png` | 42x42 | ok | 0,84 | 0,60 | 2, 1, 3, 1 | 43° | 0,53 | 0,78 | нет |
| `Assets/Textures/Items/Economy/oPP/oPP.png` | 42x42 | ok | 0,59 | 0,79 | 8, 1, 8, 1 | 272° | 0,43 | 0,53 | нет |
| `Assets/Textures/Items/Geology/Instruments/geoBlack.png` | 42x42 | ok | 0,86 | 0,75 | 1, 2, 1, 2 | 55° | 0,10 | 0,40 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoBlackRock.png` | 42x42 | ok | 0,88 | 0,87 | 1, 1, 1, 2 | 31° | 0,66 | 0,50 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoBlue.png` | 42x42 | ok | 0,88 | 1,14 | 1, 1, 1, 2 | 53° | 0,15 | 0,44 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoCyan.png` | 42x42 | ok | 0,88 | 1,10 | 1, 1, 1, 2 | 59° | 0,16 | 0,44 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoHypno.png` | 42x42 | ok | 0,88 | 0,73 | 1, 1, 1, 2 | 53° | 0,15 | 0,43 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoRainbow.png` | 42x42 | ok | 0,88 | 1,07 | 1, 1, 1, 2 | 31° | 0,69 | 0,52 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoRed.png` | 42x42 | ok | 0,86 | 1,10 | 1, 2, 1, 2 | 50° | 0,17 | 0,45 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoRedRock.png` | 42x42 | ok | 0,91 | 0,62 | 1, 1, 1, 1 | 29° | 0,69 | 0,50 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoViolet.png` | 42x42 | ok | 0,88 | 1,04 | 1, 1, 1, 2 | 50° | 0,15 | 0,44 | имя |
| `Assets/Textures/Items/Geology/Instruments/geoWhite.png` | 42x42 | ok | 0,88 | 0,61 | 1, 1, 1, 2 | 54° | 0,09 | 0,43 | имя |
| `Assets/Textures/Items/Geology/Packs/geopack.png` | 42x42 | ok | 0,88 | 0,75 | 1, 1, 1, 2 | 54° | 0,13 | 0,43 | имя |
| `Assets/Textures/Items/Progression/freeUp/freeUp.png` | 42x42 | ok | 0,68 | 0,86 | 6, 1, 6, 1 | 286° | 0,42 | 0,42 | имя |
| `Assets/Textures/Items/Progression/mineBooster/mineBooster.png` | 42x42 | ok | 0,86 | 0,75 | 2, 1, 2, 1 | 20° | 0,69 | 0,40 | имя |
| `Assets/Textures/Items/Progression/up/up.png` | 42x42 | ok | 0,86 | 0,62 | 2, 1, 2, 1 | 112° | 0,53 | 0,66 | нет |
| `Assets/Textures/Items/Progression/upgradeBooster/upgradeBooster.png` | 42x42 | ok | 0,84 | 0,82 | 1, 2, 1, 3 | 106° | 0,63 | 0,47 | имя |
| `Assets/Textures/Items/Recovery/battery/battery.png` | 42x42 | ok | 0,66 | 0,43 | 6, 1, 7, 1 | 228° | 0,66 | 0,68 | имя |
| `Assets/Textures/Items/Recovery/nano/nano.png` | 42x42 | ok | 0,70 | 0,55 | 5, 1, 6, 1 | 1° | 0,50 | 0,29 | имя |
| `Assets/Textures/Items/Recovery/rem/rem.png` | 42x42 | ok | 0,82 | 0,87 | 1, 3, 1, 3 | 2° | 0,60 | 0,45 | нет |
| `Assets/Textures/Items/Travel/gate/gate.png` | 42x42 | ok | 0,52 | 0,77 | 9, 1, 10, 1 | 184° | 0,25 | 0,26 | имя |
| `Assets/Textures/Items/Travel/portableTeleporter/portableTeleporter.png` | 42x42 | ok | 0,75 | 0,55 | 1, 4, 1, 5 | 121° | 0,57 | 0,41 | имя |
| `Assets/Textures/Items/Travel/resp/resp.png` | 42x42 | ok | 0,88 | 1,32 | 1, 1, 2, 1 | 217° | 0,45 | 0,77 | имя |
| `Assets/Textures/Items/Travel/teleport/teleport.png` | 42x42 | ok | 0,86 | 0,94 | 2, 1, 2, 1 | 288° | 0,48 | 0,68 | имя |
| `Assets/Textures/Items/Travel/trans/trans.png` | 42x42 | ok | 0,66 | 0,63 | 6, 1, 7, 1 | 298° | 0,54 | 0,38 | имя |
| `Assets/Textures/Server/Cells/Backgrounds/Volcano/VolcanoBackground.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 24° | 0,66 | 0,19 | имя |
| `Assets/Textures/Server/Cells/Backgrounds/WithHeavyTraces/BackgroundWithHeavyTraces.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,13 | имя |
| `Assets/Textures/Server/Cells/Backgrounds/WithLightTraces/BackgroundWithLightTraces.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 24° | 0,40 | 0,16 | имя |
| `Assets/Textures/Server/Cells/Biological/Black/AliveBlack.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 307° | 0,41 | 0,24 | имя |
| `Assets/Textures/Server/Cells/Biological/Blue/AliveBlue.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 224° | 0,83 | 0,58 | имя |
| `Assets/Textures/Server/Cells/Biological/Cyan/AliveCyan.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 205° | 0,78 | 0,61 | имя |
| `Assets/Textures/Server/Cells/Biological/Red/AliveRed.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 2° | 0,70 | 0,60 | имя |
| `Assets/Textures/Server/Cells/Biological/Violet/AliveViol.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 275° | 0,52 | 0,60 | имя |
| `Assets/Textures/Server/Cells/Biological/White/AliveWhite.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 238° | 0,53 | 0,67 | имя |
| `Assets/Textures/Server/Cells/Boulders/BlackBoulder/BlackBoulder1.png` | 32x32 | ok | 0,82 | 0,00 | 1, 2, 1, 2 | 12° | 0,37 | 0,19 | имя |
| `Assets/Textures/Server/Cells/Boulders/BlackBoulder/BlackBoulder2.png` | 32x32 | ok | 0,91 | 0,00 | 1, 1, 1, 0 | 28° | 0,35 | 0,19 | имя |
| `Assets/Textures/Server/Cells/Boulders/BlackBoulder/BlackBoulder3.png` | 32x32 | ok | 0,94 | 0,00 | 0, 1, 1, 0 | 47° | 0,35 | 0,18 | имя |
| `Assets/Textures/Server/Cells/Boulders/Boulder/Boulder1.png` | 32x32 | ok | 0,91 | 0,00 | 1, 1, 1, 0 | 72° | 0,03 | 0,23 | имя |
| `Assets/Textures/Server/Cells/Boulders/Boulder/Boulder2.png` | 32x32 | ok | 0,88 | 0,00 | 1, 1, 1, 1 | 66° | 0,03 | 0,13 | имя |
| `Assets/Textures/Server/Cells/Boulders/Boulder/Boulder3.png` | 32x32 | ok | 0,94 | 0,00 | 0, 1, 1, 0 | 75° | 0,02 | 0,30 | имя |
| `Assets/Textures/Server/Cells/Boulders/DeepMagmaBoulder/DeepMagmaBoulder.png` | 32x32 | ok | 0,88 | 0,00 | 1, 1, 1, 1 | 358° | 0,54 | 0,35 | имя |
| `Assets/Textures/Server/Cells/Boulders/MetalBoulder/MetalBoulder1.png` | 32x32 | ok | 0,82 | 0,00 | 2, 1, 2, 1 | 253° | 0,39 | 0,23 | имя |
| `Assets/Textures/Server/Cells/Boulders/MetalBoulder/MetalBoulder2.png` | 32x32 | ok | 0,85 | 0,00 | 1, 2, 1, 1 | 193° | 0,39 | 0,23 | имя |
| `Assets/Textures/Server/Cells/Boulders/MetalBoulder/MetalBoulder3.png` | 32x32 | ok | 0,88 | 0,00 | 0, 2, 1, 1 | 341° | 0,40 | 0,21 | имя |
| `Assets/Textures/Server/Cells/Buildings/Corner/BuildingCorner.png` | 448x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 222° | 0,80 | 0,83 | имя |
| `Assets/Textures/Server/Cells/Buildings/Door/BuildingDoor.png` | 448x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 173° | 0,78 | 0,82 | имя |
| `Assets/Textures/Server/Cells/Buildings/Road/BuildingRoad.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 294° | 0,06 | 0,11 | имя |
| `Assets/Textures/Server/Cells/Buildings/Wall/BuildingWall.png` | 512x32 | ok | 0,94 | 0,00 | 32, 0, 0, 0 | 149° | 0,48 | 0,68 | имя |
| `Assets/Textures/Server/Cells/Construction/GreenBlock/GreenBlock.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 83° | 0,67 | 0,29 | имя |
| `Assets/Textures/Server/Cells/Construction/MilitaryBlock/MilitaryBlock.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 186° | 0,48 | 0,40 | имя |
| `Assets/Textures/Server/Cells/Construction/MilitaryBlockFrame/MilitaryBlockFrame.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 201° | 0,43 | 0,34 | имя |
| `Assets/Textures/Server/Cells/Construction/QuadBlock/QuadBlock.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,46 | имя |
| `Assets/Textures/Server/Cells/Construction/RedBlock/RedBlock.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 7° | 0,91 | 0,30 | имя |
| `Assets/Textures/Server/Cells/Construction/Support/Support.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 46° | 0,10 | 0,44 | имя |
| `Assets/Textures/Server/Cells/Construction/YellowBlock/YellowBlock.png` | 32x32 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 34° | 0,94 | 0,39 | имя |
| `Assets/Textures/Server/Cells/Crystals/Blue/Blue.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 235° | 0,80 | 0,47 | имя |
| `Assets/Textures/Server/Cells/Crystals/Blue/XBlue.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 224° | 0,84 | 0,56 | имя |
| `Assets/Textures/Server/Cells/Crystals/Cyan/Cyan.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 211° | 0,87 | 0,50 | имя |
| `Assets/Textures/Server/Cells/Crystals/Cyan/XCyan.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 207° | 0,81 | 0,56 | имя |
| `Assets/Textures/Server/Cells/Crystals/Green/Green.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 233° | 0,81 | 0,40 | имя |
| `Assets/Textures/Server/Cells/Crystals/Green/XGreen.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 176° | 0,85 | 0,41 | имя |
| `Assets/Textures/Server/Cells/Crystals/Red/Red.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 360° | 0,75 | 0,46 | нет |
| `Assets/Textures/Server/Cells/Crystals/Red/XRed.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 1° | 0,72 | 0,54 | имя |
| `Assets/Textures/Server/Cells/Crystals/Violet/Violet.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 278° | 0,60 | 0,47 | имя |
| `Assets/Textures/Server/Cells/Crystals/Violet/XViolet.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 276° | 0,53 | 0,60 | имя |
| `Assets/Textures/Server/Cells/Crystals/White/White.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 237° | 0,57 | 0,65 | имя |
| `Assets/Textures/Server/Cells/Liquids/Acid/Blue/BlueAcid.png` | 32x160 | ok | 0,99 | 0,00 | 0, 1, 0, 0 | 194° | 0,66 | 0,39 | нет |
| `Assets/Textures/Server/Cells/Liquids/Acid/Gray/GrayAcid.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 357° | 0,16 | 0,63 | имя |
| `Assets/Textures/Server/Cells/Liquids/Acid/Passive/PassiveAcid.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 41° | 0,74 | 0,56 | нет |
| `Assets/Textures/Server/Cells/Liquids/Acid/Purple/PurpleAcid.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 284° | 0,77 | 0,48 | имя |
| `Assets/Textures/Server/Cells/Liquids/Acid/Yellow/YellowAcid.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 41° | 0,74 | 0,56 | нет |
| `Assets/Textures/Server/Cells/Liquids/Lava/Lava.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 7° | 0,96 | 0,71 | имя |
| `Assets/Textures/Server/Cells/Roads/Golden/GoldenRoad.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 36° | 0,76 | 0,15 | имя |
| `Assets/Textures/Server/Cells/Roads/Polymer/PolymerRoad.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 159° | 0,52 | 0,14 | имя |
| `Assets/Textures/Server/Cells/Roads/Standard/Road.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 294° | 0,06 | 0,11 | имя |
| `Assets/Textures/Server/Cells/Rock/Acid/AcidRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 56° | 0,83 | 0,32 | имя |
| `Assets/Textures/Server/Cells/Rock/Black/BlackRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 192° | 0,19 | 0,13 | имя |
| `Assets/Textures/Server/Cells/Rock/Deep/DeepRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 83° | 0,68 | 0,35 | нет |
| `Assets/Textures/Server/Cells/Rock/DeepObsidian/DeepObsidian.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 255° | 0,44 | 0,11 | имя |
| `Assets/Textures/Server/Cells/Rock/DeepRainbow/DeepRainbowRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 218° | 0,54 | 0,63 | имя |
| `Assets/Textures/Server/Cells/Rock/DeepStriped/DeepStripedRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 190° | 0,43 | 0,41 | имя |
| `Assets/Textures/Server/Cells/Rock/DeepTurquoise/DeepTurquoiseRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 213° | 0,46 | 0,51 | имя |
| `Assets/Textures/Server/Cells/Rock/GRock/GRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 13° | 0,58 | 0,18 | нет |
| `Assets/Textures/Server/Cells/Rock/Golden/GoldenRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 28° | 0,79 | 0,49 | нет |
| `Assets/Textures/Server/Cells/Rock/Heavy/HeavyRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 346° | 0,62 | 0,39 | нет |
| `Assets/Textures/Server/Cells/Rock/LivingBlack/LivingBlackRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 192° | 0,19 | 0,13 | нет |
| `Assets/Textures/Server/Cells/Rock/Red/RedRock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 5° | 0,74 | 0,26 | имя |
| `Assets/Textures/Server/Cells/Rock/Standard/Rock.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 240° | 0,79 | 0,39 | имя |
| `Assets/Textures/Server/Cells/Sand/Black/BlackSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 32° | 0,13 | 0,47 | нет |
| `Assets/Textures/Server/Cells/Sand/Blue/BlueSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 209° | 0,44 | 0,81 | нет |
| `Assets/Textures/Server/Cells/Sand/DarkBlack/DarkBlackSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 30° | 0,22 | 0,26 | нет |
| `Assets/Textures/Server/Cells/Sand/DarkBlue/DarkBlueSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 206° | 0,52 | 0,63 | нет |
| `Assets/Textures/Server/Cells/Sand/DarkRusty/DarkRustySand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 22° | 0,78 | 0,51 | нет |
| `Assets/Textures/Server/Cells/Sand/DarkWhite/DarkWhiteSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 36° | 0,34 | 0,66 | нет |
| `Assets/Textures/Server/Cells/Sand/DarkYellow/DarkYellowSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 35° | 0,63 | 0,65 | нет |
| `Assets/Textures/Server/Cells/Sand/DeepLazurite/DeepLazuriteSand.png` | 32x160 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 203° | 0,74 | 0,33 | нет |
| `Assets/Textures/Server/Cells/Sand/MilitaryBlock/MilitaryBlockSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 206° | 0,68 | 0,79 | нет |
| `Assets/Textures/Server/Cells/Sand/Rusty/RustySand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 25° | 0,63 | 0,71 | имя |
| `Assets/Textures/Server/Cells/Sand/White/WhiteSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 40° | 0,20 | 0,81 | имя |
| `Assets/Textures/Server/Cells/Sand/Yellow/YellowSand.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 38° | 0,56 | 0,93 | нет |
| `Assets/Textures/Server/Cells/State/Empty/Empty.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 24° | 0,66 | 0,19 | имя |
| `Assets/Textures/Server/Cells/State/Unloaded/Unloaded.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 237° | 0,55 | 0,64 | имя |
| `Assets/Textures/Server/Cells/State/WorldEdge/WorldEdge.png` | 320x320 | ok | 1,00 | 0,00 | 0, 0, 0, 0 | 5° | 0,74 | 0,26 | имя |
| `Assets/Textures/Server/Cells/Structures/Storage/Box.png` | 32x128 | ok | 0,85 | 0,00 | 2, 1, 2, 2 | 292° | 0,32 | 0,58 | нет |
| `Assets/Textures/Server/Clan/1/1.png` | 13x13 | — | 0,53 | 0,00 | 2, 1, 2, 2 | 0° | 0,00 | 0,70 | нет |
| `Assets/Textures/Server/Crystals/blue/blue.png` | 24x24 | — | 0,92 | 0,00 | 0, 0, 0, 2 | 233° | 0,58 | 0,42 | имя |
| `Assets/Textures/Server/Crystals/cyan/cyan.png` | 24x24 | — | 0,88 | 0,00 | 0, 0, 1, 2 | 174° | 0,37 | 0,31 | имя |
| `Assets/Textures/Server/Crystals/green/green.png` | 24x24 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 110° | 0,61 | 0,38 | имя |
| `Assets/Textures/Server/Crystals/red/red.png` | 24x24 | — | 0,92 | 0,00 | 0, 2, 0, 0 | 360° | 0,44 | 0,45 | нет |
| `Assets/Textures/Server/Crystals/violet/violet.png` | 24x24 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 327° | 0,41 | 0,40 | имя |
| `Assets/Textures/Server/Crystals/white/white.png` | 24x24 | — | 0,69 | 0,00 | 2, 2, 2, 2 | 139° | 0,04 | 0,31 | имя |
| `Assets/Textures/Server/Pack/Clans/0/0.png` | 114x82 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 7° | 0,06 | 0,42 | нет |
| `Assets/Textures/Server/Pack/Craft/0/0.png` | 50x47 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 352° | 0,26 | 0,43 | нет |
| `Assets/Textures/Server/Pack/Gun/0/0.png` | 94x94 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 24° | 0,39 | 0,26 | нет |
| `Assets/Textures/Server/Pack/Gun/1/1.png` | 94x94 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 26° | 0,42 | 0,30 | нет |
| `Assets/Textures/Server/Pack/Gun/2/2.png` | 94x94 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 29° | 0,43 | 0,30 | нет |
| `Assets/Textures/Server/Pack/Gun/3/3.png` | 94x94 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 27° | 0,43 | 0,31 | нет |
| `Assets/Textures/Server/Pack/Gun/4/4.png` | 94x94 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 22° | 0,44 | 0,30 | нет |
| `Assets/Textures/Server/Pack/Jobs/0/0.png` | 114x50 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 202° | 0,35 | 0,54 | нет |
| `Assets/Textures/Server/Pack/Market/0/0.png` | 114x114 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 16° | 0,19 | 0,54 | нет |
| `Assets/Textures/Server/Pack/Observatory/0/0.png` | 50x50 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 3° | 0,31 | 0,81 | нет |
| `Assets/Textures/Server/Pack/Resp/0/0.png` | 50x87 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,38 | нет |
| `Assets/Textures/Server/Pack/Resp/1/1.png` | 50x87 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 247° | 0,29 | 0,61 | нет |
| `Assets/Textures/Server/Pack/Science/0/0.png` | 242x114 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 169° | 0,10 | 0,51 | нет |
| `Assets/Textures/Server/Pack/Storage/0/0.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 3° | 0,49 | 0,74 | нет |
| `Assets/Textures/Server/Pack/Storage/1/1.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 86° | 0,42 | 0,70 | нет |
| `Assets/Textures/Server/Pack/Storage/2/2.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 227° | 0,53 | 0,75 | нет |
| `Assets/Textures/Server/Pack/Storage/3/3.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 43° | 0,48 | 0,77 | нет |
| `Assets/Textures/Server/Pack/Storage/4/4.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 181° | 0,45 | 0,70 | нет |
| `Assets/Textures/Server/Pack/Storage/5/5.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 264° | 0,52 | 0,75 | нет |
| `Assets/Textures/Server/Pack/Storage/6/6.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,66 | нет |
| `Assets/Textures/Server/Pack/Storage/7/7.png` | 50x18 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,20 | нет |
| `Assets/Textures/Server/Pack/Teleport/0/0.png` | 50x50 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,00 | 0,50 | нет |
| `Assets/Textures/Server/Pack/Teleport/1/1.png` | 50x50 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 300° | 0,33 | 0,83 | нет |
| `Assets/Textures/Server/Pack/Up/0/0.png` | 50x79 | violation (не кратно 32) | 1,00 | 0,00 | 0, 0, 0, 0 | 116° | 0,23 | 0,41 | нет |
| `Assets/Textures/Server/Skin/Capsule/Capsule.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 253° | 0,34 | 0,27 | имя |
| `Assets/Textures/Server/Skin/DarkCapsule/DarkCapsule.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 257° | 0,31 | 0,25 | нет |
| `Assets/Textures/Server/Skin/Fatty/Fatty.png` | 32x32 | — | 0,85 | 0,00 | 1, 1, 1, 2 | 250° | 0,20 | 0,38 | имя |
| `Assets/Textures/Server/Skin/GreenSkin/greenSkin.png` | 32x32 | — | 0,74 | 0,00 | 3, 1, 3, 2 | 112° | 0,25 | 0,31 | имя |
| `Assets/Textures/Server/Skin/Medic/Medic.png` | 32x32 | — | 0,52 | 0,00 | 4, 5, 4, 5 | 106° | 0,12 | 0,48 | имя |
| `Assets/Textures/Server/Skin/Mosquito/Mosquito.png` | 32x32 | — | 0,45 | 0,00 | 5, 5, 6, 5 | 326° | 0,17 | 0,42 | имя |
| `Assets/Textures/Server/Skin/OrangeSkin/orangeSkin.png` | 32x32 | — | 0,76 | 0,00 | 3, 1, 3, 1 | 18° | 0,28 | 0,36 | имя |
| `Assets/Textures/Server/Skin/PurpleSkin/purpleSkin.png` | 32x32 | — | 0,85 | 0,00 | 1, 1, 1, 2 | 243° | 0,09 | 0,36 | имя |
| `Assets/Textures/Server/Skin/RedSkin/redSkin.png` | 32x32 | — | 0,77 | 0,00 | 2, 2, 2, 2 | 9° | 0,34 | 0,41 | имя |
| `Assets/Textures/Server/Skin/Standard/Standard.png` | 32x32 | — | 0,74 | 0,00 | 3, 1, 3, 2 | 62° | 0,27 | 0,35 | имя |
| `Assets/Textures/Server/Skin/Widow/Widow.png` | 32x32 | — | 0,70 | 0,00 | 4, 1, 4, 1 | 176° | 0,08 | 0,42 | имя |
| `Assets/Textures/Server/Tail/Blue/Blue.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 219° | 0,59 | 0,32 | имя |
| `Assets/Textures/Server/Tail/Default/default.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 66° | 0,23 | 0,14 | имя |
| `Assets/Textures/Server/Tail/Gold/Gold.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 43° | 0,65 | 0,32 | имя |
| `Assets/Textures/Server/Tail/Green/Green.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 133° | 0,54 | 0,28 | имя |
| `Assets/Textures/Server/Tail/Red/Red.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 0° | 0,64 | 0,32 | нет |
| `Assets/Textures/Server/Tail/Violet/Violet.png` | 32x32 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 273° | 0,49 | 0,32 | имя |
| `Assets/Textures/Server/Visual/bz/bz.png` | 32x960 | — | 0,97 | 0,50 | 0, 22, 0, 8 | 45° | 0,45 | 0,99 | нет |
| `Assets/Textures/Server/Visual/death/death.png` | 64x2496 | — | 0,79 | 0,05 | 10, 6, 3, 21 | 22° | 0,36 | 0,69 | имя |
| `Assets/Textures/Server/World/Terrain/Decals/terrain-decals-rock.png` | 512x32 | — | 0,71 | 1,19 | 14, 4, 14, 4 | 332° | 0,37 | 0,86 | имя |
| `Assets/Textures/Server/World/Terrain/Decals/terrain-decals.png` | 512x32 | — | 0,33 | 1,10 | 4, 11, 14, 10 | 26° | 0,02 | 0,77 | имя |
| `Assets/Textures/Surface/basalt/basalt.png` | 1254x1254 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 56° | 0,08 | 0,21 | guid |
| `Assets/Textures/Surface/moon/moon.png` | 1287x1222 | — | 0,87 | 0,02 | 0, 21, 143, 0 | 54° | 0,17 | 0,31 | guid |
| `Assets/Textures/Surface/ridge-a/ridge-a.png` | 2172x724 | — | 0,97 | 0,05 | 0, 19, 0, 0 | 146° | 0,09 | 0,27 | guid |
| `Assets/Textures/Surface/ridge-b/ridge-b.png` | 2172x724 | — | 1,00 | 0,06 | 0, 0, 0, 0 | 166° | 0,08 | 0,26 | guid |
| `Assets/Textures/Surface/ridge-c/ridge-c.png` | 2172x724 | — | 0,97 | 0,05 | 0, 20, 0, 0 | 174° | 0,07 | 0,29 | guid |
| `Assets/Textures/UI/Controls/Slider/knob.png` | 24x24 | — | 0,88 | 0,00 | 0, 0, 1, 2 | 174° | 0,37 | 0,31 | имя |
| `Assets/Textures/UI/Controls/Toggle/States/checked.png` | 259x253 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 206° | 0,39 | 0,80 | имя |
| `Assets/Textures/UI/Controls/Toggle/States/deselected.png` | 259x260 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 207° | 0,46 | 0,38 | нет |
| `Assets/Textures/UI/Controls/Toggle/States/selected.png` | 260x260 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 206° | 0,39 | 0,81 | имя |
| `Assets/Textures/UI/Controls/Toggle/States/unchecked.png` | 256x255 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 206° | 0,44 | 0,38 | имя |
| `Assets/Textures/World/Surface/Materials/perspective.png` | 480x329 | — | 1,00 | 0,03 | 0, 0, 0, 0 | 55° | 0,62 | 0,41 | имя |
| `Assets/Textures/World/Surface/Materials/transit.png` | 480x47 | — | 1,00 | 0,00 | 0, 0, 0, 0 | 46° | 0,59 | 0,32 | имя |
