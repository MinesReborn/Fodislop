#nullable enable

using System;
using Kern.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class MapTextureController
{
    private int _lastPanelWidth = -1;
    private int _lastPanelHeight = -1;

    public int TexWidth { get; private set; }

    public int TexHeight { get; private set; }

    public Texture2D? MapTexture { get; private set; }

    public bool CheckPanelResize(VisualElement? mapViewport)
    {
        if (mapViewport == null)
        {
            return false;
        }

        Rect panelRect = mapViewport.worldBound;
        int curW = panelRect.width > 0f ? Mathf.RoundToInt(panelRect.width) : 0;
        int curH = panelRect.height > 0f ? Mathf.RoundToInt(panelRect.height) : 0;
        return curW > 0 && curH > 0 && (curW != _lastPanelWidth || curH != _lastPanelHeight);
    }

    public void InitTexture(VisualElement mapViewport, Image? mapImage)
    {
        Rect panelRect = mapViewport.worldBound;

        MapViewportBounds.CalculateTextureDimensions(
            panelRect.width,
            panelRect.height,
            out int texW,
            out int texH);

        TexWidth = texW;
        TexHeight = texH;

        _lastPanelWidth = panelRect.width > 0f ? Mathf.RoundToInt(panelRect.width) : 1920;
        _lastPanelHeight = panelRect.height > 0f ? Mathf.RoundToInt(panelRect.height) : 1080;

        DestroyTexture();

        MapTexture = RuntimeTextureFactory.CreateRGBA32NoMip(
            TexWidth,
            TexHeight,
            "WorldMapTexture",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);

        // new Texture2D не инициализирует пиксели, и до первого Render панель
        // показала бы неинициализированную память. Заливаем цветом незагруженной
        // клетки: карта без данных обязана быть чёрной, а не мусором.
        var unloaded = new Color32[TexWidth * TexHeight];
        Array.Fill(unloaded, new Color32(0, 0, 0, 255));
        MapTexture.SetPixelData(unloaded, 0);
        MapTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        // Текстура переписывается на каждом кадре рендера, поэтому динамический
        // атлас UI Toolkit обязан её исключить.
        DynamicAtlasConfigurator.RegisterRuntimeRedrawn(MapTexture);

        if (mapImage != null)
        {
            mapImage.image = MapTexture;
        }
    }

    public void DestroyTexture()
    {
        if (MapTexture != null)
        {
            UnityEngine.Object.Destroy(MapTexture);
            MapTexture = null;
        }
    }
}
