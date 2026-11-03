#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using NUnit.Framework;
using UnityEngine;

using Kern.Game.Entities.Robots;
using Kern.Game.Visual.Entities.Tails;
using Kern.Game.Visual.Entities.Tentacles;

namespace Kern.Tests.Game;

/// <summary>
///     Pins the tail to the previous client: Mines Original / Assets/Scripts/RobotScript.cs
///     (TailUpdate, UpdateTailVertices and the index table built in Start).
/// </summary>
/// <remarks>
///     The oracle is a transcription of the legacy code inside this file, never a call into
///     production code, and it is driven with the same per-strand random stream as the
///     chain under test. Each strand owns its own stream because the legacy loop order
///     (sector outer, strand inner) differs from the production one (strand outer, sector
///     inner); a single shared stream would hand the two sides different values.
/// </remarks>
public sealed class TailChainLegacyParityTests
{
    private const float DeltaTime = 1f / 60f;
    private const int Sectors = TentacleGeometry.SectorCount;

    /// <summary>Matches the four LineRenderer strands in the legacy robot prefab.</summary>
    private const int Strands = 4;

    private const float ParityTolerance = 1e-4f;

    [Test]
    public void Step_MatchesLegacyTailUpdate_AtSixtyHertz()
    {
        const int Ticks = 240;
        const float Speed = 15f;

        var legacy = new LegacyTail(Vector2.zero, seed: 1234);
        var production = CreateStrands(seed: 1234);

        for (int tick = 0; tick < Ticks; tick++)
        {
            Vector2 root = new(tick * (Speed * DeltaTime), 0f);

            legacy.Step(root);
            StepAll(production, new Vector3(root.x, root.y, 0f), DeltaTime);

            for (int strand = 0; strand < Strands; strand++)
            {
                AssertNodesMatch(legacy, production[strand], strand, $"tick {tick}");
            }
        }
    }

    /// <summary>
    ///     Legacy sector <c>s</c> is its smoothed array entry <c>s</c>; the chain exposes the
    ///     robot itself at index 0, so entry <c>s</c> sits at index <c>s + 1</c>.
    /// </summary>
    private static void AssertNodesMatch(LegacyTail legacy, TailChain chain, int strand, string context)
    {
        Vector2[] expected = legacy.SmoothedStrand(strand);
        for (int sector = 0; sector < Sectors; sector++)
        {
            Vector3 actual = chain[sector + 1];
            Assert.That(actual.x, Is.EqualTo(expected[sector].x).Within(ParityTolerance),
                $"{context}: strand {strand} sector {sector} x");
            Assert.That(actual.y, Is.EqualTo(expected[sector].y).Within(ParityTolerance),
                $"{context}: strand {strand} sector {sector} y");
        }
    }

    [Test]
    public void SharedStretch_CollapsesEveryStrandOntoOneInertia_WhenPastTheFarClamp()
    {
        // Scope: this covers the chain's response to a shared stretch. That the owner reads the
        // distance off strand zero and hands the same value to every strand lives in
        // RobotVisuals.UpdateTentacles and is not covered here — driving it would need the
        // renderer, since Tentacle self-registers on construction.
        // The anti-stretch clamps overwrite the per-strand inertia term, so a stretch past
        // the far threshold must make every strand integrate identically. They can only
        // do that if every strand actually received the same shared distance. Wobble is
        // disabled here so the comparison isolates the inertia term.
        const float Stretch = 25f;

        var strands = new TailChain[Strands];
        for (int strand = 0; strand < Strands; strand++)
        {
            strands[strand] = new TailChain(strand, Vector3.zero, () => 0.5f);
        }

        foreach (TailChain chain in strands)
        {
            chain.Snap(Vector3.zero);
            chain.Step(Vector3.zero, movementFactor: 1f, DeltaTime, Stretch);
        }

        TailChain reference = strands[0];
        for (int strand = 1; strand < Strands; strand++)
        {
            for (int sector = 1; sector <= Sectors; sector++)
            {
                Assert.That(strands[strand][sector].x,
                    Is.EqualTo(reference[sector].x).Within(ParityTolerance),
                    $"strand {strand} sector {sector} x did not follow the shared stretch");
                Assert.That(strands[strand][sector].y,
                    Is.EqualTo(reference[sector].y).Within(ParityTolerance),
                    $"strand {strand} sector {sector} y did not follow the shared stretch");
            }
        }
    }

    [Test]
    public void TipDistanceTo_MeasuresTheStrandsOwnTip()
    {
        var chain = new TailChain(0, Vector3.zero, () => 0.5f);
        var root = new Vector3(7f, 0f, 0f);

        Assert.That(chain.TipDistanceTo(root), Is.EqualTo(7f).Within(1e-5f));

        chain.Step(root, movementFactor: 1f, DeltaTime, stretch: 5f);

        // The tip is the last chain node, which the indexer exposes after the robot point.
        Assert.That(chain.TipDistanceTo(root),
            Is.EqualTo(Vector3.Distance(chain[Sectors], root)).Within(1e-5f));
    }

    [Test]
    public void Wobble_ContinuesWhileTheRootIsStationary_AtZeroMovementFactor()
    {
        // A cheap non-repeating stand-in for UnityEngine.Random: consecutive draws must
        // differ, otherwise a frozen chain would pass this test.
        float value = 0f;
        float Next() => value = (value + 0.6180339887f) % 1f;

        var chain = new TailChain(0, Vector3.zero, () => Next());
        var start = chain[Sectors];

        for (int tick = 0; tick < 30; tick++)
        {
            chain.Step(Vector3.zero, movementFactor: 0f, DeltaTime, stretch: 0.5f);
        }

        Assert.That((chain[Sectors] - start).sqrMagnitude, Is.GreaterThan(1e-6f),
            "the legacy tail keeps shivering at rest; a frozen chain is a regression");
        Assert.That(chain.IsSettled, Is.False,
            "an always-wobbling chain can never satisfy the settle test");
    }

    [Test]
    public void Step_IsFrameRateIndependent_WithWobbleDisabled()
    {
        var single = new TailChain(0, Vector3.zero, () => 0.5f);
        var halved = new TailChain(0, Vector3.zero, () => 0.5f);

        single.Step(new Vector3(1f, 0f, 0f), movementFactor: 1f, DeltaTime, stretch: 1f);
        halved.Step(new Vector3(1f, 0f, 0f), movementFactor: 1f, DeltaTime / 2f, stretch: 1f);
        halved.Step(new Vector3(1f, 0f, 0f), movementFactor: 1f, DeltaTime / 2f, stretch: 1f);

        for (int node = 0; node < Sectors; node++)
        {
            Assert.That(halved[node].x, Is.EqualTo(single[node].x).Within(1e-5f),
                $"node {node} x: inertia^(60*dt) must compose to inertia^1 over two half steps");
            Assert.That(halved[node].y, Is.EqualTo(single[node].y).Within(1e-5f),
                $"node {node} y");
        }
    }

    [TestCase(2)]
    [TestCase(6)]
    [TestCase(12)]
    public void Step_LongFrameMatchesShortSteps_WithMovingRootAndWobble(int ticks)
    {
        var stalledRandom = new System.Random(718);
        var regularRandom = new System.Random(718);
        var stalled = new TailChain(0, Vector3.zero, () => (float)stalledRandom.NextDouble());
        var regular = new TailChain(0, Vector3.zero, () => (float)regularRandom.NextDouble());
        Vector3 destination = new(3f, 1f, 0f);
        float durationSeconds = DeltaTime * ticks;
        stalled.Step(destination, 1f, durationSeconds, 1f);
        for (int tick = 0; tick < ticks; tick++)
        {
            regular.Step(destination * ((tick + 1f) / ticks), 1f, DeltaTime, 1f);
        }

        for (int point = 0; point < TailChain.PointCount; point++)
        {
            Assert.That(Vector3.Distance(stalled[point], regular[point]), Is.LessThan(1e-4f),
                $"point {point}: a long frame must preserve the short-step chain and noise sequence");
        }
    }

    [Test]
    public void Step_HighFrameRatePreservesNoiseSequenceAndChain()
    {
        var regularRandom = new System.Random(941);
        var fastRandom = new System.Random(941);
        var regular = new TailChain(0, Vector3.zero, () => (float)regularRandom.NextDouble());
        var fast = new TailChain(0, Vector3.zero, () => (float)fastRandom.NextDouble());
        for (int tick = 0; tick < 120; tick++)
        {
            Vector3 midpoint = new((tick + 0.5f) * 0.25f, 0f, 0f);
            Vector3 root = new((tick + 1f) * 0.25f, 0f, 0f);
            regular.Step(root, 1f, DeltaTime, 1f);
            fast.Step(midpoint, 1f, DeltaTime / 2f, 1f);
            fast.Step(root, 1f, DeltaTime / 2f, 1f);
            for (int point = 0; point < TailChain.PointCount; point++)
            {
                Assert.That(Vector3.Distance(fast[point], regular[point]), Is.LessThan(1e-4f));
            }
        }
    }

    [Test]
    public void TentacleGeometry_WritesTwoSharedVerticesPerPointAtAConstantWidth()
    {
        var points = new Vector3[TentacleGeometry.PointCount];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector3(i * 0.25f, 0f, 0f);
        }

        Rect atlasRect = new(0.25f, 0.5f, 0.125f, 0.0625f);
        var verts = new Vector3[TentacleGeometry.VerticesPerTentacle];
        var uvs = new Vector2[TentacleGeometry.VerticesPerTentacle];

        TentacleGeometry.WriteVertices(points, atlasRect, verts, uvs, vertBase: 0);

        Assert.That(verts.Length, Is.EqualTo(10), "two vertices per line point");
        Assert.That(uvs.Length, Is.EqualTo(10));

        float width = 2f / TentacleGeometry.HalfWidthDivisor;

        for (int point = 0; point < TentacleGeometry.PointCount; point++)
        {
            int v = point * TentacleGeometry.VerticesPerPoint;
            Vector3 centre = points[point];

            // A straight line has no turn, so the mitre factor is one and the offset is the
            // plain perpendicular at every point.
            Vector3 offset = new Vector3(0f, 1f, 0f) / TentacleGeometry.HalfWidthDivisor;
            Assert.That(verts[v], Is.EqualTo(centre + offset).Within(ParityTolerance), $"point {point} plus");
            Assert.That(verts[v + 1], Is.EqualTo(centre - offset).Within(ParityTolerance), $"point {point} minus");

            Assert.That(Vector3.Distance(verts[v], verts[v + 1]),
                Is.EqualTo(width).Within(ParityTolerance), $"point {point} width");

            // U runs the tail once; V crosses the width, so the texture's vertical gradient is
            // what shades the ribbon.
            float expectedU = atlasRect.xMin +
                (atlasRect.width * ((float)point / (TentacleGeometry.PointCount - 1)));
            Assert.That(uvs[v], Is.EqualTo(new Vector2(expectedU, atlasRect.yMax)), $"point {point} upper vertex");
            Assert.That(uvs[v + 1], Is.EqualTo(new Vector2(expectedU, atlasRect.yMin)), $"point {point} lower vertex");
        }
    }

    [Test]
    public void TentacleGeometry_MitresTheJoint_SoABendDoesNotPinchTheRibbon()
    {
        // One shared offset per point means the offset bisects the turn. Without the mitre
        // correction the ribbon would be 1 / cos(half turn) too thin at every bend, which is
        // exactly where the legacy version showed its notches.
        const float turn = 60f;
        float half = turn * 0.5f * Mathf.Deg2Rad;
        float expected = 1f / Mathf.Cos(half);

        var points = new Vector3[TentacleGeometry.PointCount];
        points[0] = Vector3.zero;
        points[1] = Vector3.right;
        points[2] = points[1] + Quaternion.Euler(0f, 0f, turn) * Vector3.right;
        for (int i = 3; i < points.Length; i++)
        {
            points[i] = points[i - 1] + Vector3.right;
        }

        var verts = new Vector3[TentacleGeometry.VerticesPerTentacle];
        var uvs = new Vector2[TentacleGeometry.VerticesPerTentacle];

        TentacleGeometry.WriteVertices(points, new Rect(0f, 0f, 1f, 1f), verts, uvs, vertBase: 0);

        int joint = 1 * TentacleGeometry.VerticesPerPoint;
        float width = Vector3.Distance(verts[joint], verts[joint + 1]);

        Assert.That(width, Is.EqualTo((2f / TentacleGeometry.HalfWidthDivisor) * expected).Within(1e-3f),
            "the joint offset must be mitred so the ribbon keeps its width through the bend");
    }

    [Test]
    public void TentacleGeometry_WidthStaysAQuarterOfTheBody()
    {
        // Batching draws a 32px skin at 100 pixels per unit with no prefab scale, so the body
        // is 0.32 world units. The legacy client scaled its body by 3.125 to one unit, which
        // made its 2/16 ribbon an eighth of the body; copying the width verbatim here made it
        // 39% of the body instead. The width is held to a quarter — twice the legacy
        // proportion.
        const float BodySize = 32f / 100f;

        float width = 2f / TentacleGeometry.HalfWidthDivisor;

        Assert.That(width / BodySize, Is.EqualTo(0.25f).Within(1e-4f),
            "the tail must stay a quarter of the body width");
    }

    [Test]
    public void TentacleGeometry_SubtractsTheSimulationOffset_SoTheStripStaysOnTheRobot()
    {
        // The tail motion debug modes run the chain in a frame that travels behind the robot.
        // Sector zero's "from" point is the chain's root, so without the subtraction the whole
        // strip would sit away from the body by the whole accumulated offset.
        var robotRoot = new Vector3(12f, -3f, 0f);
        Vector3 originOffset = new(-40f, 7f, 0f);

        var points = new Vector3[TailChain.PointCount];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = robotRoot + originOffset + new Vector3(i * 0.25f, 0f, 0f);
        }

        var verts = new Vector3[TentacleGeometry.VerticesPerTentacle];
        var uvs = new Vector2[TentacleGeometry.VerticesPerTentacle];

        TentacleGeometry.WriteVertices(
            points,
            new Rect(0f, 0f, 1f, 1f),
            verts,
            uvs,
            vertBase: 0,
            originOffset);

        // Point zero sits on the robot, exactly on it rather than a half-width off.
        Vector3 offset = new Vector3(0f, 1f, 0f) / TentacleGeometry.HalfWidthDivisor;

        Assert.That(verts[0], Is.EqualTo(robotRoot + offset).Within(ParityTolerance));
        Assert.That(verts[1], Is.EqualTo(robotRoot - offset).Within(ParityTolerance));

        // And the rest of the strip trails behind the robot, not behind the offset frame.
        for (int point = 1; point < TentacleGeometry.PointCount; point++)
        {
            Vector3 expectedCentre = robotRoot + new Vector3(point * 0.25f, 0f, 0f);
            int v = point * TentacleGeometry.VerticesPerPoint;
            Assert.That(verts[v], Is.EqualTo(expectedCentre + offset).Within(ParityTolerance),
                $"point {point} must sit in robot space, not simulation space");
            Assert.That(verts[v + 1], Is.EqualTo(expectedCentre - offset).Within(ParityTolerance),
                $"point {point} must sit in robot space, not simulation space");
        }
    }

[Test]
    public void ForcedTailMotion_AdvancesTheOffsetAtEveryFacingAngle()
    {
        // Calls the production accumulator, so switching its facing source back to
        // Transform.forward — which is (0, 0, 1) at a zero angle and has no component in the
        // play plane — fails here instead of silently leaving the offset at zero.
        var go = new GameObject("tail-facing");
        try
        {
            const float speed = 7.5f;
            const float deltaTime = 1f / 60f;

            foreach (float degrees in new[] { 0f, 45f, 90f, 180f, 270f, 359f })
            {
                go.transform.rotation = Quaternion.Euler(0f, 0f, degrees);

                Vector3 advanced = RobotVisuals.AdvanceForcedOffset(
                    Vector3.zero, go.transform, speed, deltaTime);

                Assert.That(advanced.sqrMagnitude, Is.GreaterThan(0f),
                    $"angle {degrees}: the forced offset must actually advance");
                Assert.That(advanced.z, Is.EqualTo(0f).Within(1e-5f),
                    $"angle {degrees}: the offset must stay in the play plane");
                Assert.That(Vector3.Dot(advanced.normalized, go.transform.up),
                    Is.GreaterThan(0.99f),
                    $"angle {degrees}: the offset must travel along the facing");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ForcedTailMotion_TravelsAlongTheFacing_SoTheTailTrailsBehind()
    {
        // The chain lags behind its root, so a root travelling against the facing would settle
        // the tail in front of the robot. This asserts on the production accumulator's sign.
        var go = new GameObject("tail-direction");
        try
        {
            go.transform.rotation = Quaternion.Euler(0f, 0f, 37f);
            Vector3 facing = go.transform.up;

            Vector3 advanced = RobotVisuals.AdvanceForcedOffset(
                Vector3.zero, go.transform, speed: 7.5f, deltaTime: 1f / 60f);

            Assert.That(Vector3.Dot(advanced, facing), Is.GreaterThan(0f),
                "the simulated root must travel along the facing so the chain is left behind it");
            Assert.That(Vector3.Dot(-facing, facing), Is.LessThan(0f),
                "the tail therefore ends up on the opposite side, which is behind the robot");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ForcedTailMotion_DoesNotAdvanceAtNormalModeOrAtRest()
    {
        var go = new GameObject("tail-idle");
        try
        {
            Assert.That(RobotVisuals.ForcedTailSpeed(TailMotionDebugMode.Normal), Is.EqualTo(0f));

            Vector3 start = new(3f, -4f, 0f);
            Vector3 unchanged = RobotVisuals.AdvanceForcedOffset(
                start, go.transform, RobotVisuals.ForcedTailSpeed(TailMotionDebugMode.Normal), 1f / 60f);

            Assert.That(unchanged, Is.EqualTo(start), "normal mode must leave the frame alone");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ForcedTailMotion_MinimalSpeedIsHalfOfRobotMoveSpeed()
    {
        Assert.That(RobotVisuals.ForcedTailSpeed(TailMotionDebugMode.Minimal),
            Is.EqualTo(MovementDefaults.RobotMoveSpeed * 0.5f).Within(1e-4f));
        Assert.That(RobotVisuals.ForcedTailSpeed(TailMotionDebugMode.Minimal),
            Is.EqualTo(7.5f).Within(1e-4f));
        Assert.That(RobotVisuals.ForcedTailSpeed(TailMotionDebugMode.Medium),
            Is.EqualTo(MovementDefaults.RobotMoveSpeed).Within(1e-4f));
        Assert.That(RobotVisuals.ForcedTailSpeed(TailMotionDebugMode.Maximum),
            Is.EqualTo(MovementDefaults.RobotMoveSpeed * 4f).Within(1e-4f));
    }

    [Test]
    public void StrandOrder_StrandZeroIsLongest_AndEachExtraIsShorter()
    {
        // Higher strand index means a larger per-strand inertia term, which makes a node retain
        // more of its lagging position, so the strand settles shorter. The extras beyond the
        // legacy four therefore ride inside strand zero's span rather than past it.
        var strands = CreateStrands(seed: 77);
        foreach (TailChain chain in strands)
        {
            chain.Snap(Vector3.zero);
        }

        for (int tick = 0; tick < 300; tick++)
        {
            StepAll(strands, new Vector3(tick * 0.25f, 0f, 0f), DeltaTime);
        }

        float previous = float.MaxValue;
        for (int strand = 0; strand < Strands; strand++)
        {
            float tip = Vector3.Distance(strands[strand][Sectors], new Vector3(300 * 0.25f, 0f, 0f));
            Assert.That(tip, Is.LessThan(previous),
                $"strand {strand} must be shorter than the one before it");
            previous = tip;
        }
    }

    [Test]
    public void TentacleGeometry_ClosesEveryRibbonQuad()
    {
        var tris = new int[TentacleGeometry.IndicesPerTentacle];

        TentacleGeometry.WriteIndices(tris, indexBase: 0, vertexBase: 0);

        Assert.That(tris.Length, Is.EqualTo(24), "four quads form the five-point ribbon");

        for (int segment = 0; segment < TentacleGeometry.SectorCount; segment++)
        {
            int v = segment * TentacleGeometry.VerticesPerPoint;
            int t = segment * 6;
            Assert.That(tris[t], Is.EqualTo(v), $"segment {segment} a");
            Assert.That(tris[t + 1], Is.EqualTo(v + 1), $"segment {segment} b");
            Assert.That(tris[t + 2], Is.EqualTo(v + 2), $"segment {segment} c");
            Assert.That(tris[t + 3], Is.EqualTo(v + 2), $"segment {segment} c again");
            Assert.That(tris[t + 4], Is.EqualTo(v + 1), $"segment {segment} b again");
            Assert.That(tris[t + 5], Is.EqualTo(v + 3), $"segment {segment} d");

            Assert.That(CountTrianglesCoveringQuad(tris, segment, 0), Is.EqualTo(2),
                $"segment {segment} must be a closed quad, not a lone triangle");
        }
    }

    [Test]
    public void TentacleGeometry_ReferencesEveryVertexOfTheStrip()
    {
        // The legacy index table left the far corner of sectors one through three referenced by
        // no index, which is what drew the tail as detached shards.
        var tris = new int[TentacleGeometry.IndicesPerTentacle];

        TentacleGeometry.WriteIndices(tris, indexBase: 0, vertexBase: 0);

        var referenced = new bool[TentacleGeometry.VerticesPerTentacle];
        foreach (int index in tris)
        {
            referenced[index] = true;
        }

        for (int vertex = 0; vertex < referenced.Length; vertex++)
        {
            Assert.That(referenced[vertex], Is.True, $"vertex {vertex} is never drawn");
        }
    }

    private static int CountTrianglesCoveringQuad(
        int[] tris,
        int segment,
        int indexOffset,
        int vertexOffset = 0)
    {
        int baseVertex = vertexOffset + (segment * TentacleGeometry.VerticesPerPoint);
        int covering = 0;
        for (int t = indexOffset; t + 2 < tris.Length; t += 3)
        {
            bool usesOnlyThisQuad = true;
            for (int k = 0; k < 3; k++)
            {
                int vertex = tris[t + k] - baseVertex;
                if (vertex < 0 || vertex >= (TentacleGeometry.VerticesPerPoint * 2))
                {
                    usesOnlyThisQuad = false;
                    break;
                }
            }

            if (usesOnlyThisQuad)
            {
                covering++;
            }
        }

        return covering;
    }

    private static TailChain[] CreateStrands(int seed)
    {
        var strands = new TailChain[Strands];
        for (int strand = 0; strand < Strands; strand++)
        {
            strands[strand] = new TailChain(strand, Vector3.zero, StrandStream(seed, strand));
        }

        return strands;
    }

    private static void StepAll(TailChain[] strands, Vector3 root, float deltaTime)
    {
        // Mirrors RobotVisuals.UpdateTentacles: the stretch is read once, from the tip of
        // strand zero, and handed to every strand.
        float stretch = strands[0].TipDistanceTo(root);
        foreach (TailChain strand in strands)
        {
            strand.Step(root, movementFactor: 1f, deltaTime, stretch);
        }
    }

    private static Func<float> StrandStream(int seed, int strand)
    {
        var random = new System.Random(seed + (strand * 7919));
        return () => (float)random.NextDouble();
    }

    /// <summary>
    ///     Transcription of the legacy tail update. Sector loop outside, strand loop inside,
    ///     single stretch measured from strand zero's smoothed tip, per-strand inertia term.
    ///     The index layout is not transcribed: the legacy one left three of every four sector
    ///     quads half-drawn, which is covered separately by the geometry tests below.
    /// </summary>
    private sealed class LegacyTail
    {
        private const float BaseInertia = 0.35f;
        private const float InertiaFalloff = 800f;
        private const float InertiaDenominator = 2000f;
        private const float StrandInertiaBase = 16f;
        private const float StrandInertiaStep = 2f;
        private const float StretchDistance = 10f;
        private const float StretchInertia = 0.2f;
        private const float StretchDistanceFar = 20f;
        private const float StretchInertiaFar = 0.1f;
        private const float SegmentWobbleBias = 0.15f;

        // The legacy amplitudes, scaled by the same factor the chain uses. The wobble scale is
        // the one deliberate divergence from the legacy client, so the oracle follows the
        // production constants instead of repeating the numbers; the scale itself is pinned by
        // WobbleAmplitude_IsThreeTenthsOfTheLegacyClient.
        private const float RootWobbleAmplitude = TailChain.RootWobbleAmplitude;
        private const float SegmentWobbleAmplitude = TailChain.SegmentWobbleAmplitude;

        private readonly Vector2[][] _chain;
        private readonly Vector2[][] _smoothed;
        private readonly System.Random[] _random;

        public LegacyTail(Vector2 start, int seed)
        {
            _chain = new Vector2[Strands][];
            _smoothed = new Vector2[Strands][];
            _random = new System.Random[Strands];

            for (int strand = 0; strand < Strands; strand++)
            {
                _chain[strand] = new Vector2[Sectors];
                _smoothed[strand] = new Vector2[Sectors];
                for (int sector = 0; sector < Sectors; sector++)
                {
                    _chain[strand][sector] = start;
                    _smoothed[strand][sector] = start;
                }

                _random[strand] = new System.Random(seed + (strand * 7919));
            }
        }

        public Vector2[] SmoothedStrand(int strand) => _smoothed[strand];

        public void Step(Vector2 root)
        {
            float stretch = Vector2.Distance(_smoothed[0][Sectors - 1], root);

            for (int sector = 0; sector < Sectors; sector++)
            {
                for (int strand = 0; strand < Strands; strand++)
                {
                    float strandInertia = StrandInertiaBase + (StrandInertiaStep * strand);
                    float inertia = BaseInertia +
                        (InertiaFalloff / (InertiaDenominator + (strandInertia * stretch * stretch)));
                    if (stretch > StretchDistance)
                    {
                        inertia = StretchInertia;
                    }

                    if (stretch > StretchDistanceFar)
                    {
                        inertia = StretchInertiaFar;
                    }

                    float pull = 1f - inertia;

                    if (sector == 0)
                    {
                        float cubed = inertia * inertia * inertia;
                        _chain[strand][sector] = (cubed * _chain[strand][sector]) + ((1f - cubed) * root);
                        _chain[strand][sector] += pull * RootWobbleAmplitude * WobbleVector(strand);
                    }
                    else
                    {
                        _chain[strand][sector] +=
                            (pull - SegmentWobbleBias) * SegmentWobbleAmplitude * WobbleVector(strand);
                        _chain[strand][sector] = (inertia * _chain[strand][sector]) +
                            (pull * _chain[strand][sector - 1]);
                    }

                    _smoothed[strand][sector] = (inertia * _smoothed[strand][sector]) +
                        (pull * _chain[strand][sector]);
                }
            }
        }

        private Vector2 WobbleVector(int strand) =>
            new((float)_random[strand].NextDouble() - 0.5f, (float)_random[strand].NextDouble() - 0.5f);
    }
}
