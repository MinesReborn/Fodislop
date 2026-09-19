#nullable enable

using UnityEngine;

namespace Kern.World;

public static class PixelGrid
{
    public static float QuantizeRenderScale(float desiredScale, float minimumScale, float maximumScale)
    {
        if (desiredScale <= 0f)
        {
            return maximumScale;
        }

        float best = maximumScale;
        float bestDistance = float.MaxValue;
        for (int divisor = 1; divisor <= 8; divisor++)
        {
            float candidate = 1f / divisor;
            if (candidate < minimumScale || candidate > maximumScale)
            {
                continue;
            }

            float distance = Mathf.Abs(candidate - desiredScale);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    public static int RenderHeight(int screenHeight, float renderScale)
    {
        if (screenHeight <= 0 || renderScale <= 0f)
        {
            return screenHeight;
        }

        return Mathf.Max(1, Mathf.RoundToInt(screenHeight * renderScale));
    }

    public const int MinimumPixelsPerTexel = 1;

    /// <inheritdoc cref="MinimumPixelsPerTexel"/>
    public const int MaximumPixelsPerTexel = 64;

    public static float OrthographicSizeFor(int pixelsPerTexel, int screenHeight)
    {
        if (pixelsPerTexel <= 0 || screenHeight <= 0)
        {
            return 0f;
        }

        return screenHeight / (pixelsPerTexel * 2f * RenderingConstants.PIXELS_PER_UNIT);
    }

    public static float QuantizeOrthographicSize(
        float desiredSize,
        int screenHeight,
        float minimumSize,
        float maximumSize)
    {
        if (screenHeight <= 0 || minimumSize <= 0f || maximumSize < minimumSize)
        {
            return desiredSize;
        }

        float best = 0f;
        float bestDistance = float.MaxValue;

        for (int pixelsPerTexel = MinimumPixelsPerTexel;
            pixelsPerTexel <= MaximumPixelsPerTexel;
            pixelsPerTexel++)
        {
            float candidate = OrthographicSizeFor(pixelsPerTexel, screenHeight);
            if (candidate < minimumSize || candidate > maximumSize)
            {
                continue;
            }

            float distance = Mathf.Abs(candidate - desiredSize);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best > 0f
            ? best
            : (desiredSize < minimumSize ? minimumSize : maximumSize);
    }

    public static float PixelsPerTexel(float orthographicSize, int screenHeight)
    {
        if (orthographicSize <= 0f || screenHeight <= 0)
        {
            return 0f;
        }

        return screenHeight / (orthographicSize * 2f * RenderingConstants.PIXELS_PER_UNIT);
    }

    public static float SnapUnit(float orthographicSize, int screenHeight)
    {
        float pixelsPerTexel = PixelsPerTexel(orthographicSize, screenHeight);
        if (pixelsPerTexel <= 0f)
        {
            return 0f;
        }

        return 1f / (RenderingConstants.PIXELS_PER_UNIT * pixelsPerTexel);
    }

    public static Vector2 Snap(Vector2 position, float snapUnit)
    {
        if (snapUnit <= 0f)
        {
            return position;
        }

        return new Vector2(
            Mathf.Round(position.x / snapUnit) * snapUnit,
            Mathf.Round(position.y / snapUnit) * snapUnit);
    }
}
