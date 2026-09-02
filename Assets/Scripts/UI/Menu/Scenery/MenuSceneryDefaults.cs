#nullable enable

using UnityEngine;

namespace Fodinae.UI;

public static class MenuSceneryDefaults
{
    public const float OrbitRadius = 4f;
    public const int RenderTextureResizeThresholdPixels = 24;
    public const int MinimumRenderTextureSide = 64;

    public static readonly Vector3 OrbitPlaneEulerAngles = new(70f, 0f, -22f);
}
