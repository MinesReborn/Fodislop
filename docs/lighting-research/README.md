# Lighting research

Локальная подборка первоисточников для развития GPU lighting pipeline Kern. PDF сохранены рядом с этим индексом; ссылки ведут на страницы статей в arXiv.

## Читать в первую очередь

1. **Holographic Radiance Cascades for 2D Global Illumination** — Rouli Freeman, Alexander Sannikov, Adrian Margel (2025), [arXiv:2505.02041](https://arxiv.org/abs/2505.02041). Главный практический источник для текущего 2D-пайплайна: многоуровневые probes, radiance intervals, merge и требования к разрешению каскадов. Локально: `2505.02041-holographic-radiance-cascades-2d-gi.pdf`.
2. **Radiance Cascades: A Novel High-Resolution Formal Solution for Multidimensional Non-LTE Radiative Transfer** — Christopher M. J. Osborne, Alexander Sannikov (2024), [arXiv:2408.14425](https://arxiv.org/abs/2408.14425). Теоретическое основание метода: penumbra hypothesis, пространственно-угловой масштаб и переиспользование radiance samples. Это статья по radiative transfer, а не готовый игровой renderer. Локально: `2408.14425-radiance-cascades-foundations.pdf`.
3. **Split Radiance Cascades: Real-Time Global Illumination via Sparse Radiance Probes** — Rouli Freeman, Alexander Sannikov (2026), [arXiv:2607.20384](https://arxiv.org/abs/2607.20384). Развитие RC: sparse world-space probes, ray splitting и temporal accumulation. Для текущего 2D compute-прохода полезна как проверка архитектуры, а не как код для прямого переноса. Локально: `2607.20384-split-radiance-cascades.pdf`.

## Соседние production-техники

4. **Scaling Probe-Based Real-Time Dynamic Global Illumination for Production** — Zander Majercik, Adam Marrs, Josef Spjut, Morgan McGuire (2020), [arXiv:2009.10796](https://arxiv.org/abs/2009.10796). Практика production DDGI: visibility, self-shadow bias, probe state machine, multiresolution cascaded volumes и сокращение бесполезной работы. Локально: `2009.10796-scaling-probe-based-ddgi.pdf`.
5. **Occlusion-Point Reuse for Ray-Traced Ambient Occlusion and Shadow** — Haojie Jin et al. (2026), [arXiv:2607.23122](https://arxiv.org/abs/2607.23122). Отдельный материал по AO/visibility и переиспользованию occlusion samples. Не является рецептом для occupancy-based contact AO Kern, но полезен для строгого разделения AO, visibility и direct shadow. Локально: `2607.23122-occlusion-point-reuse-ao.pdf`.

## Привязка к Kern

- Radiance Cascades относятся к `SolveCascade`, `ResolveDirect` и объединению интервалов.
- DDGI полезен для политики invalidation, pruning и масштабирования стоимости, но не заменяет текущий 2D RC на 3D probe volume.
- AO остаётся отдельным полноразрешённым полем из occupancy и не должен подменять direct radiance, transmission или тени.
- Любую оптимизацию сначала проверять GPU-профайлером по отдельным kernels и dispatch dimensions; ограничение FPS не считается оптимизацией.
