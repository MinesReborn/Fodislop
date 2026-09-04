#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Updates beacon reticles, ping pulses and station badge positioning over the planet scenery.
/// </summary>
public sealed class MenuSceneryMarkers
{
    public static void Animate(
        float time,
        VisualElement? beacon,
        VisualElement? beaconPing,
        VisualElement? stationBadge,
        VisualElement? sidebar,
        VisualElement? targetReticle,
        Image? planetBodyImage,
        MenuSceneryController? scenery)
    {
        UpdateStationMarker(beacon, beaconPing, stationBadge, sidebar, planetBodyImage, scenery);
        UpdateLandingSectorMarker(targetReticle, planetBodyImage, scenery);

        if (targetReticle != null)
        {
            targetReticle.style.scale = new Scale(Vector3.one);
        }
    }

    private static void UpdateStationMarker(
        VisualElement? beacon,
        VisualElement? beaconPing,
        VisualElement? stationBadge,
        VisualElement? sidebar,
        Image? planetBodyImage,
        MenuSceneryController? scenery)
    {
        if (beacon == null)
        {
            return;
        }

        IPanel? hostPanel = beacon.panel;

        if (hostPanel == null ||
            !TryGetPlanetFrame(planetBodyImage, scenery, out Rect rect, out Rect image) ||
            scenery == null ||
            !scenery.TryGetStationViewportPosition(out Vector2 viewport))
        {
            UIState.Hide(beacon);
            return;
        }

        Rect panel = hostPanel.visualTree.worldBound;
        float panelX = image.x + (viewport.x * image.width);
        float panelY = image.y + ((1f - viewport.y) * image.height);

        const float footerSafe = 56f;

        if (stationBadge != null)
        {
            const float edgeGap = 24f;
            const float markerGap = 28f;
            const float headerSafe = 84f;

            float badgeWidth = stationBadge.resolvedStyle.width;
            float badgeHeight = stationBadge.resolvedStyle.height;
            bool hasBadgeLayout = float.IsFinite(badgeWidth) && float.IsFinite(badgeHeight) &&
                badgeWidth > 0f && badgeHeight > 0f;
            if (hasBadgeLayout)
            {
                float safeRight = panel.width - edgeGap;
                if (sidebar != null)
                {
                    Rect rail = sidebar.worldBound;
                    if (rail.width > 0f && panelY + badgeHeight > rail.yMin && panelY < rail.yMax)
                    {
                        safeRight = Mathf.Min(safeRight, rail.xMin - edgeGap);
                    }
                }

                float preferred = panelX + markerGap + badgeWidth <= safeRight
                    ? panelX + markerGap
                    : panelX - markerGap - badgeWidth;

                float left = Mathf.Clamp(preferred, edgeGap, Mathf.Max(edgeGap, safeRight - badgeWidth));
                float top = Mathf.Clamp(
                    panelY - (badgeHeight * 0.5f),
                    headerSafe,
                    Mathf.Max(headerSafe, panel.height - footerSafe - badgeHeight));

                stationBadge.style.left = left - panelX;
                stationBadge.style.top = top - panelY;
                stationBadge.style.right = StyleKeyword.Auto;
                stationBadge.style.bottom = StyleKeyword.Auto;
            }
        }

        UIState.Show(beacon);
        UIState.Hide(beaconPing);

        float x = rect.x + (viewport.x * rect.width);
        float y = rect.y + ((1f - viewport.y) * rect.height);

        Vector2 markerHalfSize = ResolveHalfSize(beacon);
        beacon.style.left = x - markerHalfSize.x;
        beacon.style.top = y - markerHalfSize.y;
    }

    private static void UpdateLandingSectorMarker(
        VisualElement? targetReticle,
        Image? planetBodyImage,
        MenuSceneryController? scenery)
    {
        if (targetReticle == null)
        {
            return;
        }

        if (!TryGetPlanetFrame(planetBodyImage, scenery, out Rect rect, out _) ||
            scenery == null ||
            !scenery.TryGetPlanetSurfaceViewportPosition(MenuSceneryPresenter.LandingSiteDirection, out Vector2 viewport))
        {
            UIState.Hide(targetReticle);
            return;
        }

        float x = rect.x + (viewport.x * rect.width);
        float y = rect.y + ((1f - viewport.y) * rect.height);

        Vector2 markerHalfSize = ResolveHalfSize(targetReticle);
        targetReticle.style.left = x - markerHalfSize.x;
        targetReticle.style.top = y - markerHalfSize.y;
        UIState.Show(targetReticle);
    }

    private static Vector2 ResolveHalfSize(VisualElement element)
    {
        float width = element.resolvedStyle.width;
        float height = element.resolvedStyle.height;
        return new Vector2(
            float.IsFinite(width) && width > 0f ? width * 0.5f : 0f,
            float.IsFinite(height) && height > 0f ? height * 0.5f : 0f);
    }

    public static bool TryGetPlanetFrame(
        Image? planetBodyImage,
        MenuSceneryController? scenery,
        out Rect localFrame,
        out Rect worldFrame)
    {
        localFrame = default;
        worldFrame = default;

        if (planetBodyImage == null || scenery == null || scenery.OutputTexture == null)
        {
            return false;
        }

        if (!ReferenceEquals(planetBodyImage.image, scenery.OutputTexture))
        {
            return false;
        }

        Rect rect = planetBodyImage.layout;
        if (rect.width <= 1f || rect.height <= 1f ||
            float.IsNaN(rect.width) || float.IsNaN(rect.height))
        {
            return false;
        }

        localFrame = rect;
        worldFrame = planetBodyImage.worldBound;
        return true;
    }
}
