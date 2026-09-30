#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Player;
internal sealed class CameraPixelGridAligner
{
    private readonly IClientConfigManager? _clientConfig;

    public CameraPixelGridAligner(IClientConfigManager? clientConfig)
    {
        _clientConfig = clientConfig;
    }

    public PixelSamplingMode Mode =>
        _clientConfig?.Config?.Display?.PixelSampling ?? PixelSamplingMode.SmoothFiltered;

    public bool QuantizesZoom => PixelSamplingRules.QuantizesZoom(Mode);

    // Соседний целый уровень пикселей на тексель. levels > 0 приближает
    // (больше пикселей на тексель — меньше кадр), levels < 0 отдаляет.
    // Щелчок колёсика в PixelPerfect ведёт ровно на соседний уровень, а не в
    // «ближайший к сдвинутому размеру»: иначе мелкая прокрутка то не
    // срабатывала, то перескакивала через уровень.
    public float StepQuantizedSize(float size, int levels, float minimumZoom, float maximumZoom)
    {
        int height = EffectiveRenderHeight();
        int current = Mathf.RoundToInt(PixelGrid.PixelsPerTexel(size, height));
        int next = Mathf.Clamp(
            current + levels,
            PixelGrid.MinimumPixelsPerTexel,
            PixelGrid.MaximumPixelsPerTexel);
        return PixelGrid.QuantizeOrthographicSize(
            PixelGrid.OrthographicSizeFor(next, height),
            height,
            minimumZoom,
            maximumZoom);
    }

    public float ResolveOrthographicSize(float desiredSize, float minimumZoom, float maximumZoom)
    {
        return PixelSamplingRules.QuantizesZoom(Mode)
            ? PixelGrid.QuantizeOrthographicSize(
                desiredSize,
                EffectiveRenderHeight(),
                minimumZoom,
                maximumZoom)
            : desiredSize;
    }

    public Vector3 SnapPosition(Vector3 position, float orthographicSize)
    {
        if (!PixelSamplingRules.SnapsCameraPosition(Mode))
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

    private static int EffectiveRenderHeight()
    {
        float renderScale = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
            ? urp.renderScale
            : 1f;
        return PixelGrid.RenderHeight(Screen.height, renderScale);
    }
}
