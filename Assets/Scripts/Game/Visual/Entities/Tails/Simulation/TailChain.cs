#nullable enable

using System;
using UnityEngine;

using Kern.Game.Rendering;

namespace Kern.Game.Visual.Entities.Tails;

/// <summary>
///     Follow-the-leader chain for one strand of a robot's tail, ported from the previous
///     client: Mines Original / Assets/Scripts/RobotScript.cs (TailUpdate).
/// </summary>
/// <remarks>
///     <para>
///         No history buffer and no target distance between nodes: each node is pulled toward its
///         predecessor with a fixed inertia, and the chain length is an emergent property of that
///         inertia against the root's speed.
///     </para>
///     <para>
    ///         Simulation uses elapsed-time accumulation at 60 Hz, a wobble that continues
    ///         while standing, and batched shared-vertex ribbon
///         geometry. The source prefab uses four line renderers; the same four
///         strands are kept here. Consequences and measurements are in
///         docs/architecture/TAIL_RENDERING.md.
///     </para>
/// </remarks>
public sealed class TailChain
{
    /// <summary>
    ///     One point is the robot itself plus four chain nodes, which is exactly the strip
    ///     topology the shared batch renderer allocates. Taken from the renderer so the two
    ///     can never drift apart.
    /// </summary>
    public const int PointCount = WorldEntityBatchRenderer.POINT_COUNT;

    private const int NodeCount = PointCount - 1;

    /// <summary>
    ///     The old step was exponential smoothing applied once per 1/60 s, so the continuous
    ///     equivalent for an individual node is alpha = 1 - inertia^(60 * deltaTime).
    ///     Coupled nodes still require bounded steps when a frame stalls.
    /// </summary>
    private const float ReferenceStepsPerSecond = 60f;
    private const float MaximumStepSeconds = 1f / ReferenceStepsPerSecond;

    /// <summary>Weight of the old node position; the remainder is the pull toward the target.</summary>
    private const float BaseInertia = 0.35f;

    private const float InertiaFalloff = 800f;
    private const float InertiaDenominator = 2000f;

    /// <summary>
    ///     Per-strand inertia falls off as the strand index rises, so strand 0 trails the furthest
    ///     behind. Sign matters: inverting this term reverses which strand is long.
    /// </summary>
    private const float StrandInertiaBase = 16f;
    private const float StrandInertiaStep = 2f;

    /// <summary>
    ///     Anti-stretch: the further the tip is from the robot, the harder the chain is yanked
    ///     back. Two separate thresholds, not an else-if chain — the old code applied them in
    ///     order, so past the far threshold the nearer value was already overwritten.
    /// </summary>
    private const float StretchDistance = 10f;
    private const float StretchInertia = 0.2f;
    private const float StretchDistanceFar = 20f;
    private const float StretchInertiaFar = 0.1f;

    /// <summary>
    ///     Wobble amplitude per node, as in the legacy client. Raising or lowering this does not
    ///     change the outline: the wobble is white noise and the render layer low-passes it, so the
    ///     drawn centre line turns only about 1.7 degrees per node at any amplitude. It controls
    ///     shimmer, not curvature.
    /// </summary>
    internal const float RootWobbleAmplitude = 2.5f;
    internal const float SegmentWobbleAmplitude = 3.5f;

    private const float SegmentWobbleBias = 0.15f;

    private const float SettleEpsilonSquared = 1e-8f;
    private const float MovementEpsilon = 1e-4f;

    private readonly float _strandInertia;
    private readonly Func<float> _unitRandom;
    private readonly Vector3[] _chain = new Vector3[NodeCount];
    private readonly Vector3[] _smoothed = new Vector3[NodeCount];
    private Vector3 _rootPosition;
    private double _remainingSeconds;
    private bool _settled;

    public TailChain(int strandIndex, Vector3 startPosition, Func<float>? unitRandom = null)
    {
        _strandInertia = StrandInertiaBase + (StrandInertiaStep * strandIndex);
        _unitRandom = unitRandom ?? DefaultUnitRandom;
        _rootPosition = startPosition;
        for (int i = 0; i < NodeCount; i++)
        {
            _chain[i] = startPosition;
            _smoothed[i] = startPosition;
        }
    }

    private static float DefaultUnitRandom() => UnityEngine.Random.value;

    public bool IsSettled => _settled;

    /// <summary>Rendered strip point; index 0 is the robot itself, as in the old client.</summary>
    public Vector3 this[int index] => index == 0 ? _rootPosition : _smoothed[index - 1];

    /// <summary>
    ///     Distance from this strand's tip to <paramref name="root" />. The owner reads it off
    ///     strand zero and passes the same value to every strand, as the legacy client did.
    /// </summary>
    public float TipDistanceTo(Vector3 root) => Vector3.Distance(_smoothed[NodeCount - 1], root);

    public void Snap(Vector3 position)
    {
        _remainingSeconds = 0;
        _rootPosition = position;
        for (int i = 0; i < NodeCount; i++)
        {
            _chain[i] = position;
            _smoothed[i] = position;
        }

        _settled = true;
    }

    /// <param name="stretch">Anti-stretch distance shared by every strand.</param>
    public void Step(Vector3 rootPosition, float movementFactor, float deltaTime, float stretch)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 previousRoot = _rootPosition;
        double frameSeconds = deltaTime;
        double elapsedSeconds = 0;
        // Consume complete reference ticks, including those missed during a
        // stall. Keep the remainder so high FPS does not resample random noise
        // more often or change the coupled chain's integration order.
        while (_remainingSeconds + frameSeconds >= MaximumStepSeconds)
        {
            double untilTick = MaximumStepSeconds - _remainingSeconds;
            elapsedSeconds += untilTick;
            frameSeconds -= untilTick;
            _remainingSeconds = 0;
            Vector3 stepRoot = Vector3.Lerp(previousRoot, rootPosition, (float)(elapsedSeconds / deltaTime));
            IntegrateStep(stepRoot, movementFactor, MaximumStepSeconds, stretch);
        }

        _remainingSeconds += frameSeconds;
        _rootPosition = rootPosition;
    }

    private void IntegrateStep(Vector3 rootPosition, float movementFactor, float deltaTime, float stretch)
    {
        _rootPosition = rootPosition;

        float inertia = BaseInertia +
            (InertiaFalloff / (InertiaDenominator + (_strandInertia * stretch * stretch)));
        if (stretch > StretchDistance)
        {
            inertia = StretchInertia;
        }

        if (stretch > StretchDistanceFar)
        {
            inertia = StretchInertiaFar;
        }

        float steps = ReferenceStepsPerSecond * deltaTime;
        float nodeAlpha = 1f - Mathf.Pow(inertia, steps);

        // The root used a cubed inertia: extra lag right at the base so the tail hinges
        // instead of pivoting rigidly around the robot.
        float rootInertia = inertia * inertia * inertia;
        float rootAlpha = 1f - Mathf.Pow(rootInertia, steps);

        // Wobble is the only source of life in the old model — no sine, no phase, no idle
        // animation. The legacy client left the amplitude untouched by movement, so a
        // standing tail keeps shivering; only the elapsed time scales it here.
        float wobbleScale = steps;
        float pull = 1f - inertia;

        _chain[0] += (rootAlpha * (rootPosition - _chain[0]));
        _chain[0] += Wobble(RootWobbleAmplitude * pull * wobbleScale);

        for (int i = 1; i < NodeCount; i++)
        {
            _chain[i] += Wobble((pull - SegmentWobbleBias) * SegmentWobbleAmplitude * wobbleScale);
            _chain[i] += nodeAlpha * (_chain[i - 1] - _chain[i]);
        }

        for (int i = 0; i < NodeCount; i++)
        {
            _smoothed[i] += nodeAlpha * (_chain[i] - _smoothed[i]);
        }

        _settled = movementFactor <= MovementEpsilon;
        if (_settled)
        {
            for (int i = 0; i < NodeCount; i++)
            {
                if ((_chain[i] - _smoothed[i]).sqrMagnitude > SettleEpsilonSquared)
                {
                    _settled = false;
                    break;
                }
            }
        }
    }

    private Vector3 Wobble(float amplitude)
    {
        if (amplitude == 0f)
        {
            return Vector3.zero;
        }

        return new Vector3(
            amplitude * (_unitRandom() - 0.5f),
            amplitude * (_unitRandom() - 0.5f),
            0f);
    }
}
