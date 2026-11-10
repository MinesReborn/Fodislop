#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Entities.Robots;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Player.Cameras;

internal sealed class CameraFollowEffects
{
    private const float HurtShakeStepSeconds = 1f / 30f;
    private const float JumpReportCells = 2f;
    private readonly Camera _camera;
    private readonly ILocalPlayerState _localPlayer;
    private readonly IWorldPresentationState _presentationState;
    private int _hurtShakeFrames;
    private float _hurtShakeClock;

    public CameraFollowEffects(
        Camera camera,
        ILocalPlayerState localPlayer,
        IWorldPresentationState presentationState)
    {
        _camera = camera;
        _localPlayer = localPlayer;
        _presentationState = presentationState;
    }

    public void TriggerHurtShake() => _hurtShakeFrames = 16;

    public void ClearHurtShake()
    {
        _hurtShakeFrames = 0;
        _hurtShakeClock = 0f;
    }

    public void UpdateHurtShake()
        {
            _hurtShakeClock += Time.deltaTime;
            if (_hurtShakeClock < HurtShakeStepSeconds)
            {
                return;
            }

            _hurtShakeClock -= HurtShakeStepSeconds;
            if (_hurtShakeFrames < 1)
            {
                return;
            }

            Vector3 position = _camera.transform.position;
            float randomX = UnityEngine.Random.Range(0f, 1f);
            float randomY = UnityEngine.Random.Range(0f, 1f);
            position.x += (randomX * 5f - 2f) * 0.0625f;
            position.y += (randomY * 5f - 2f) * 0.0625f;
            _camera.transform.position = position;
            _hurtShakeFrames--;
        }

        public void ReportCameraJump(
            Vector3 before,
            Vector3 velocityBefore,
            Vector3 velocityAfter,
            bool usedViewTransition,
            Robot? targetRobot,
            float followSmoothTime)
        {
            Vector3 after = _camera.transform.position;
            float jump = Vector2.Distance(before, after);
            if (jump <= JumpReportCells * WorldGridDefaults.CellSize)
            {
                return;
        }

            ILocalPlayer? player = _localPlayer?.Current;
            string robotState = targetRobot != null
                ? $"anchor={targetRobot.CameraAnchor}, target={targetRobot.TargetPosition}"
                : "robot=none";
            Debug.LogWarning(
                $"[CameraFollow] Camera jumped {jump:F2} from {before} to {after}; {robotState}; " +
                $"server cell={(player != null ? player.Position.ToString() : "none")}, " +
                $"hasServerPosition={player?.HasServerPosition}, " +
                $"presentation={_presentationState.Current.Phase}/{_presentationState.Current.TransitionId}, " +
                $"viewTransition={usedViewTransition}, " +
                $"dt={Time.deltaTime:F6}s, smoothTime={followSmoothTime:F6}s, " +
                $"velocityBefore={velocityBefore}, velocityAfter={velocityAfter}, frame={Time.frameCount}.");
    }
}
