#nullable enable

using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Game.Entities.Robots;

internal sealed class RobotFrameUpdater
{
    private readonly Transform _transform;
    private readonly RobotMovement _movement;
    private readonly RobotVisuals _visuals;
    private readonly RobotNameplate _nameplate;
    private readonly RobotCuller _culler = new();
    private readonly RobotLighting _lighting;
    private readonly IWorldPresentationState _presentationState;
    private readonly IGameplayCamera _gameplayCamera;
    private readonly LightingEngine _lightingEngine;
    private readonly IRuntimeDebugSettings _debugSettings;

    public RobotFrameUpdater(
        Transform transform,
        RobotMovement movement,
        RobotVisuals visuals,
        RobotNameplate nameplate,
        RobotLighting lighting,
        IWorldPresentationState presentationState,
        IGameplayCamera gameplayCamera,
        LightingEngine lightingEngine,
        IRuntimeDebugSettings debugSettings)
    {
        _transform = transform;
        _movement = movement;
        _visuals = visuals;
        _nameplate = nameplate;
        _lighting = lighting;
        _presentationState = presentationState;
        _gameplayCamera = gameplayCamera;
        _lightingEngine = lightingEngine;
        _debugSettings = debugSettings;
    }

    public void Update(
        bool isLocalPlayer,
        bool isMetadataLoaded,
        bool hasInitialServerPosition,
        bool localPlayerHasServerPosition,
        float deltaTime)
    {
        if (Application.isPlaying &&
            (isLocalPlayer ? !localPlayerHasServerPosition : !isMetadataLoaded || !hasInitialServerPosition))
        {
            return;
        }

        _lighting.InitializeSettings(_lightingEngine);

        if (_presentationState.IsTeleportPreparing)
        {
            return;
        }

        if (!isLocalPlayer && _culler.CheckAndApply(
            _transform,
            _gameplayCamera.Camera,
            _visuals,
            _nameplate,
            _lighting,
            _movement,
            _lightingEngine))
        {
            return;
        }

        if (_movement.IsSettled(_visuals.TentaclesSettled))
        {
            // A moving frame can leave the rendered transform at the tremor offset.
            _transform.position = _movement.SmoothPosition;
            _visuals.UpdateMotion(
                _transform.position,
                0f,
                deltaTime,
                true,
                _debugSettings.TailMotionDebugMode);
            _nameplate.UpdatePosition(
                _transform.position,
                _visuals.SkinSprite,
                _transform,
                _visuals.ClanTransform);
            _lighting.Update(_movement.SmoothPosition, _lightingEngine);
            return;
        }

        (Vector3 finalPosition, float rotationAngle, float movementFactor) = _movement.Step(deltaTime);
        _transform.position = finalPosition;
        _transform.rotation = Quaternion.Euler(0f, 0f, rotationAngle);
        _visuals.UpdateMotion(
            finalPosition,
            movementFactor,
            deltaTime,
            false,
            _debugSettings.TailMotionDebugMode);
        _nameplate.UpdatePosition(finalPosition, _visuals.SkinSprite, _transform, _visuals.ClanTransform);
        _lighting.Update(_movement.SmoothPosition, _lightingEngine);
    }

    public void SnapVisualToTarget(float deltaTime)
    {
        _movement.TeleportToTarget();
        _transform.position = _movement.SmoothPosition;
        _visuals.SnapTentacles(_movement.SmoothPosition);
        _visuals.UpdateMotion(
            _transform.position,
            0f,
            deltaTime,
            true,
            _debugSettings.TailMotionDebugMode);
        _nameplate.UpdatePosition(
            _transform.position,
            _visuals.SkinSprite,
            _transform,
            _visuals.ClanTransform);
        _lighting.Update(_movement.SmoothPosition, _lightingEngine);
    }
}
