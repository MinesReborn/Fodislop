#nullable enable

using Kern.Core;
using Kern.World;
using UnityEngine;

namespace Kern.Game;

public sealed class RobotMovement
{
    private const float MinimumSmoothTime = 0.05f;
    private const float MaximumSmoothTime = 0.15f;

    private Vector3 _targetPosition;
    private Vector3 _serverPosition;
    private Vector3 _smoothPosition;
    private Vector3 _currentVelocity;
    private float _targetAngle;
    private float _smoothAngle;
    private float _currentAngularVelocity;
    private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;
    private float _rotationSpeed = ProjectRuntimeContracts.Movement.RobotRotationSpeed;
    private float _tremor;
    private bool _hasReceivedInitialPosition;

    public Vector3 TargetPosition
    {
        get => _targetPosition;
        set => _targetPosition = value;
    }

    public Vector3 ServerPosition => _serverPosition;
    public Vector3 SmoothPosition => _smoothPosition;
    public float TargetAngle
    {
        get => _targetAngle;
        set => _targetAngle = value;
    }
    public float MoveSpeed
    {
        get => _moveSpeed;
        set => _moveSpeed = value;
    }

    public float RotationSpeed
    {
        get => _rotationSpeed;
        set => _rotationSpeed = value;
    }

    public bool HasReceivedInitialPosition => _hasReceivedInitialPosition;

    public void SnapTo(Vector3 position, float angle)
    {
        _targetPosition = position;
        _serverPosition = position;
        _smoothPosition = position;
        _targetAngle = angle;
        _smoothAngle = angle;
        _currentVelocity = Vector3.zero;
        _currentAngularVelocity = 0f;
    }

    public bool ApplyServerPosition(ushort x, ushort y, int worldHeight, bool isLocalPlayer, out bool isInitial)
    {
        _serverPosition = CoordinateUtils.ServerToUnityPos(x, y, worldHeight);

        if (!_hasReceivedInitialPosition)
        {
            _hasReceivedInitialPosition = true;
            _smoothPosition = _serverPosition;
            _targetPosition = _serverPosition;
            _currentVelocity = Vector3.zero;
            isInitial = true;
            return true;
        }

        isInitial = false;
        if (!isLocalPlayer || Vector3.Distance(_targetPosition, _serverPosition) > 2.0f)
        {
            _targetPosition = _serverPosition;
            return true;
        }

        return false;
    }

    public bool IsSettled(bool tentaclesSettled)
    {
        return (_smoothPosition - _targetPosition).sqrMagnitude <= 1e-8f &&
               _currentVelocity.sqrMagnitude <= 1e-8f &&
               Mathf.Abs(Mathf.DeltaAngle(_smoothAngle, _targetAngle)) <= 0.001f &&
               Mathf.Abs(_currentAngularVelocity) <= 0.001f &&
               _tremor <= 0.01f &&
               tentaclesSettled;
    }

    public void TeleportToTarget()
    {
        _smoothPosition = _targetPosition;
        _smoothAngle = _targetAngle;
        _currentVelocity = Vector3.zero;
        _currentAngularVelocity = 0f;
    }

    public (Vector3 position, float angle, float movementFactor, bool snapped) Step(float deltaTime)
    {
        float renderDistance = (_smoothPosition - _targetPosition).magnitude;
        float speedRatio = Mathf.Clamp01(_moveSpeed / ProjectRuntimeContracts.Movement.ReferenceMoveSpeed);
        float targetSmoothTime = Mathf.Lerp(MinimumSmoothTime, MaximumSmoothTime, speedRatio);
        float distanceRatio = Mathf.Clamp01(renderDistance / 2f);
        float smoothTime = Mathf.Lerp(MinimumSmoothTime, targetSmoothTime, distanceRatio);

        bool snapped = false;
        if (renderDistance > 28f)
        {
            _smoothPosition = _targetPosition;
            _smoothAngle = _targetAngle;
            _currentVelocity = Vector3.zero;
            _currentAngularVelocity = 0f;
            snapped = true;
        }
        else
        {
            float maxVisualSpeed = Mathf.Max(_moveSpeed * 1.25f, 5f);
            _smoothPosition = Vector3.SmoothDamp(_smoothPosition, _targetPosition, ref _currentVelocity, smoothTime, maxVisualSpeed, deltaTime);
        }

        Vector3 finalPosition = _smoothPosition;
        if (_tremor > 0.01f)
        {
            _tremor *= Mathf.Pow(0.8f, deltaTime / 0.016f);
            finalPosition.x += _tremor * (Random.value - 0.5f);
            finalPosition.y += _tremor * (Random.value - 0.5f);
        }

        _smoothAngle = Mathf.SmoothDampAngle(_smoothAngle, _targetAngle, ref _currentAngularVelocity, smoothTime, _rotationSpeed, deltaTime);

        float movementFactor = Mathf.Clamp01(_currentVelocity.magnitude / 5f);
        return (finalPosition, _smoothAngle, movementFactor, snapped);
    }
}
