#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Player
{
    /// <summary>
    /// Укладывает камеру на пиксельную сетку экрана согласно выбранному
    /// режиму выборки.
    /// </summary>
    /// <remarks>
    /// Выделено из <see cref="CameraFollow"/>: слежение за игроком и
    /// согласование мира с сеткой экрана — разные работы. Первая про то,
    /// куда камера едет, вторая про то, во что округляется результат, и
    /// вторая зависит от разрешения окна, масштаба рендера и настройки
    /// игрока, до которых слежению дела нет.
    /// </remarks>
    internal sealed class CameraPixelGridAligner
    {
        private readonly IClientConfigManager? _clientConfig;

        public CameraPixelGridAligner(IClientConfigManager? clientConfig)
        {
            _clientConfig = clientConfig;
        }

        /// <summary>
        /// Выбранный режим.
        /// </summary>
        /// <remarks>
        /// До загрузки конфига берётся сглаживание: это значение по
        /// умолчанию, и оно же наименее заметно, если кадр-другой отработал
        /// до применения настроек.
        /// </remarks>
        public PixelSamplingMode Mode =>
            _clientConfig?.Config?.Display?.PixelSampling ?? PixelSamplingMode.SmoothFiltered;

        /// <summary>
        /// Размер камеры для запрошенного зума.
        /// </summary>
        /// <remarks>
        /// Округление к целому числу пикселей на тексель убирает муар
        /// начисто, но делает зум ступенчатым: при высоте 1080 и пределах
        /// от 5 до 30 ступеней всего пять. Поэтому оно включается только в
        /// режиме <see cref="PixelSamplingMode.PixelPerfect"/>; в остальных
        /// размер идёт как есть, а муар снимает шейдер.
        /// </remarks>
        public float ResolveOrthographicSize(float desiredSize, float minimumZoom, float maximumZoom)
        {
            return Mode == PixelSamplingMode.PixelPerfect
                ? PixelGrid.QuantizeOrthographicSize(
                    desiredSize,
                    EffectiveRenderHeight(),
                    minimumZoom,
                    maximumZoom)
                : desiredSize;
        }

        /// <summary>
        /// Ставит позицию камеры в узел пиксельной сетки.
        /// </summary>
        /// <remarks>
        /// Привязка законна при любом зуме: она округляет к целому
        /// экранному пикселю, а не к целому текселю. Сглаживание слежения
        /// выдаёт произвольную дробную позицию, и без округления вся
        /// картинка ползёт относительно сетки экрана долями пикселя —
        /// шейдерное сглаживание границ этого не снимает, оно про статичный
        /// рисунок, а не про дрожание при движении.
        ///
        /// В исходном режиме привязки нет: он существует затем, чтобы
        /// показать картинку вообще без лечения.
        /// </remarks>
        public Vector3 SnapPosition(Vector3 position, float orthographicSize)
        {
            if (Mode == PixelSamplingMode.Raw)
            {
                return position;
            }

            float snapUnit = PixelGrid.SnapUnit(orthographicSize, EffectiveRenderHeight());
            if (snapUnit <= 0f)
            {
                return position;
            }

            Vector2 snapped = PixelGrid.Snap(new Vector2(position.x, position.y), snapUnit);
            return new Vector3(snapped.x, snapped.y, position.z);
        }

        /// <summary>
        /// Высота буфера, в котором рисуется кадр.
        /// </summary>
        /// <remarks>
        /// Не высота окна: при масштабе рендера меньше единицы сетка
        /// текселей живёт в буфере, и выравнивать надо по нему.
        /// </remarks>
        private static int EffectiveRenderHeight()
        {
            float renderScale = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
                ? urp.renderScale
                : 1f;
            return PixelGrid.RenderHeight(Screen.height, renderScale);
        }
    }
}
