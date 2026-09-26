# Terrain CPU benchmark

Измеряет чистые CPU-стадии террейна без Unity Editor, сцены, рендера и GPU.
Код масок, искажения, заливки и `TerrainQuadBuilder.FillQuad` подключён из
production-файлов проекта. Входные клетки синтетические и детерминированы.
Для `FillQuad` atlas и metadata — лёгкие тестовые адаптеры; замер включает
выбор слоя и тайла, геометрию, UV, упаковку lighting/decal данных и запись
четырёх вершин, но не загрузку текстур и GPU upload.

## Сравнить вычислительные стадии

```sh
dotnet run -c Release --project tools/Kern.TerrainBench -- \
  --filter "masks,mask-composition,distortion,flood-fill,quad-fill" \
  --sizes 128x96,192x128,256x160 \
  --seconds 0.5 \
  --out Logs/terrain-bench
```

`quad-fill` отдельно показывает органический передний план, фон и классический
передний план. `masks` сверяет скользящий обход с прямым обходом по клеткам;
`mask-composition` сравнивает раздельные и объединённые рельефные маски на тех
же данных. `flood-fill` отделяет стоимость генератора тестовой пещеры от чтения
предвычисленных клеток и замера самого алгоритма.

`residency` сравнивает production probe и поиск промежуточного окна с
повторным чтением статусов чанков с вариантом, где один статус читается за
план. Фикстура эмулирует `ReadChunk` и LRU, поэтому показывает число
устранённых обращений и относительную CPU-стоимость, а не абсолютную цену
Unity storage.

Для смешанного сценария с прокруткой окна и копанием запустить:

```sh
dotnet run -c Release --project tools/Kern.TerrainBench -- \
  --filter session --sizes 192x128 --out Logs/terrain-bench
```

Для пробы резидентности:

```sh
dotnet run -c Release --project tools/Kern.TerrainBench -- \
  --filter residency --sizes 192x128 --seconds 1 --out Logs/terrain-bench
```

Вывод содержит p50/p95/max wall-time, p50/p95 суммарного CPU-времени процесса и
аллокации, плюс CPU-время самой медленной wall-time выборки. Большой разрыв
между ними показывает время, не ушедшее на исполнение потоков процесса;
параллельная операция может иметь CPU-время больше wall-time. Для
`FillQuad` также выводятся наносекунды на клетку. `session` раскладывает один
шаг по стадиям. JSON и Markdown сохраняются в указанную папку для сравнения
следующих прогонов.

Чтобы проверить CPU-таймер, запустить `--filter timing-diagnostic`: сон должен
показать wall-time без сопоставимого CPU-времени, а CPU spin — близкие значения.

Это сравнение CPU-математики на синтетических данных. Оно не подтверждает
стоимость Unity jobs, реальных world/texture services, GPU passes или поведения
шейдера в production.
