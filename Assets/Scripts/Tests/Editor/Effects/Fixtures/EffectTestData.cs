#nullable enable

using Kern.Effects;
using UnityEngine;

namespace Kern.Tests.Effects;

internal static class EffectTestData
{
    public static EffectDefinition Spark() => new()
    {
        Id = "spark",
        DurationSeconds = 0.5f,
        Loop = false,
        Emitters =
        [
            new EffectEmitterDefinition
            {
                Id = "core",
                SpriteSheet = "Effects/Sprites/Spark",
                SpriteSheetColumns = 4,
                SpriteSheetRows = 1,
                FrameCount = 4,
                FramesPerSecond = 24f,
                SortingOrder = 0,
                RatePerSecond = 0f,
                BurstCount = 6,
                LifetimeSeconds = new Vector2(0.2f, 0.3f),
                SpeedUnitsPerSecond = new Vector2(1f, 2f),
                DirectionDegrees = new Vector2(0f, 360f),
                GravityUnitsPerSecondSquared = -2f,
                DragPerSecond = 1f,
                StartSizeRange = new Vector2(0.5f, 0.9f),
                EndSizeRange = new Vector2(0.1f, 0.3f),
                StartColor = Color.white,
                EndColor = new Color(1f, 1f, 1f, 0f),
                Shape = EffectEmitterShape.Point,
            },
        ],
    };

    public static EffectDefinition SheetSpark() => new()
    {
        Id = "sheet-spark",
        DurationSeconds = 0.5f,
        Loop = false,
        Emitters =
        [
            new EffectEmitterDefinition
            {
                Id = "core",
                SpriteSheet = "Visual/Sprites/Textures/CrystallSparkle/CrystallSparkleImage",
                SpriteSheetColumns = 6,
                SpriteSheetRows = 1,
                FrameCount = 6,
                FramesPerSecond = 12f,
                SortingOrder = 0,
                BurstCount = 6,
                LifetimeSeconds = new Vector2(0.2f, 0.3f),
                SpeedUnitsPerSecond = new Vector2(1f, 2f),
                DirectionDegrees = new Vector2(0f, 360f),
                GravityUnitsPerSecondSquared = -2f,
                DragPerSecond = 1f,
                StartSizeRange = new Vector2(0.5f, 0.9f),
                EndSizeRange = new Vector2(0.1f, 0.3f),
                StartColor = Color.white,
                EndColor = new Color(1f, 1f, 1f, 0f),
                Shape = EffectEmitterShape.Point,
            },
        ],
    };

    public static EffectDefinition SheetSpark(int sortingOrder) => new()
    {
        Id = $"sheet-spark-{sortingOrder}",
        DurationSeconds = 0.5f,
        Loop = false,
        Emitters =
        [
            new EffectEmitterDefinition
            {
                Id = "core",
                SpriteSheet = "Visual/Sprites/Textures/CrystallSparkle/CrystallSparkleImage",
                SpriteSheetColumns = 6,
                SpriteSheetRows = 1,
                FrameCount = 6,
                FramesPerSecond = 12f,
                SortingOrder = sortingOrder,
                BurstCount = 6,
                LifetimeSeconds = new Vector2(0.2f, 0.3f),
                SpeedUnitsPerSecond = new Vector2(1f, 2f),
                DirectionDegrees = new Vector2(0f, 360f),
                StartSizeRange = new Vector2(0.5f, 0.9f),
                EndSizeRange = new Vector2(0.1f, 0.3f),
                Shape = EffectEmitterShape.Point,
            },
        ],
    };

    public static EffectDefinition LoopingSmoke() => new()
    {
        Id = "smoke-loop",
        DurationSeconds = 0.1f,
        Loop = true,
        Emitters =
        [
            new EffectEmitterDefinition
            {
                Id = "puff",
                SpriteSheet = "Effects/Sprites/Smoke",
                SpriteSheetColumns = 2,
                SpriteSheetRows = 2,
                FrameCount = 4,
                FramesPerSecond = 8f,
                RatePerSecond = 12f,
                BurstCount = 1,
                LifetimeSeconds = new Vector2(0.3f, 0.4f),
                SpeedUnitsPerSecond = new Vector2(0.2f, 0.5f),
                DirectionDegrees = new Vector2(70f, 110f),
                StartSizeRange = new Vector2(0.6f, 1f),
                EndSizeRange = new Vector2(1f, 1.4f),
                StartColor = new Color(0.6f, 0.6f, 0.6f, 0.7f),
                EndColor = new Color(0.6f, 0.6f, 0.6f, 0f),
                Shape = EffectEmitterShape.Circle,
                ShapeExtents = new Vector2(0.2f, 0f),
            },
        ],
    };
}
