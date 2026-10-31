#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

using Kern.Game.Rendering;
using Kern.Game.Visual.Entities.Tentacles;

namespace Kern.Game.Entities.Robots;

public sealed class RobotVisuals
{
    private const float BodySwayDegrees = 2f;
    private const float BodySwayCyclesPerCell = 0.75f;
    private const float BodySwayResponse = 12f;
    private const float FacingEpsilonSquared = 1e-6f;

    private readonly Transform _transform;
    private readonly bool _isLocalPlayer;
    private WorldEntityBatchRenderer _entityBatchRenderer = null!;
    private WorldEntityBatchRenderer.SpriteHandle? _bodyBatchHandle;
    private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;
    private Transform? _clanTransform;
    private Transform? _bodyTransform;
    private Vector3 _lastBodyPosition;
    private bool _hasBodyPosition;
    private float _bodySwayPhase;
    private float _bodySwayAngle;
    private Sprite? _skinSprite;
    private Sprite? _clanSprite;
    private Tentacle[]? _tentacles;
    private bool _tentaclesSettled;
    private Vector3 _lastTentacleRootPosition;
    private Vector3 _forcedTailOffset;
    private TailMotionDebugMode _tailMotionDebugMode;

    public Sprite? SkinSprite => _skinSprite;
    public Transform? ClanTransform => _clanTransform;
    public bool TentaclesSettled => _tentaclesSettled;

    /// <summary>Число прядей хвоста: вдвое больше легаси-четырёх; добавленные короче и едут внутри отрезка пряди ноль.</summary>
    public const int StrandCount = 8;

    public RobotVisuals(Transform transform, bool isLocalPlayer)
    {
        _transform = transform;
        _isLocalPlayer = isLocalPlayer;
    }

    internal static float ForcedTailSpeed(TailMotionDebugMode mode) => mode switch
    {
        TailMotionDebugMode.Minimal => MovementDefaults.RobotMoveSpeed * 0.5f,
        TailMotionDebugMode.Medium => MovementDefaults.RobotMoveSpeed,
        TailMotionDebugMode.Maximum => MovementDefaults.RobotMoveSpeed * 4f,
        _ => 0f,
    };

    internal static Vector3 AdvanceForcedOffset(
        Vector3 offset,
        Transform facingSource,
        float speed,
        float deltaTime)
    {
        if (speed <= 0f)
        {
            return offset;
        }

        Vector3 facing = facingSource.up;
        facing.z = 0f;
        if (facing.sqrMagnitude <= FacingEpsilonSquared)
        {
            return offset;
        }

        return offset + (facing.normalized * (speed * deltaTime));
    }

    public void Initialize(WorldEntityBatchRenderer entityBatchRenderer, Transform? clanTransform, ISceneObjectFactory sceneObjects)
    {
        _entityBatchRenderer = entityBatchRenderer;
        if (_bodyTransform == null)
        {
            _bodyTransform = sceneObjects.Create("RobotSkinMotion", RuntimeOwner.Robots).transform;
            _bodyTransform.SetParent(_transform, worldPositionStays: false);
            _bodyTransform.localPosition = Vector3.zero;
            _bodyTransform.localRotation = Quaternion.identity;
            _bodyTransform.localScale = Vector3.one;
        }

        _clanTransform = clanTransform;
        EnsureBatchHandles();
    }

    public void EnsureBatchHandles()
    {
        if (_entityBatchRenderer == null)
        {
            return;
        }

        _bodyBatchHandle ??= _entityBatchRenderer.RegisterSprite(_bodyTransform!, 0);
        if (_clanTransform != null)
        {
            _clanBatchHandle ??= _entityBatchRenderer.RegisterSprite(_clanTransform, 100);
        }
    }

    public void SetBodyVisible(bool visible)
    {
        EnsureBatchHandles();
        _bodyBatchHandle?.SetEnabled(visible);
        if (!visible)
        {
            ResetBodySway();
        }
    }
    public void SetColor(Color color)
    {
        _bodyBatchHandle?.SetColor(color);
    }

    public void SetClanSprite(Sprite? sprite)
    {
        EnsureBatchHandles();
        if (_clanSprite != null)
        {
            Object.Destroy(_clanSprite);
        }

        _clanSprite = sprite;
        if (_clanBatchHandle != null)
        {
            _entityBatchRenderer.SetSprite(_clanBatchHandle, sprite);
            _clanBatchHandle.SetEnabled(sprite != null);
        }
    }

    public void SetSkinSprite(Sprite? sprite)
    {
        EnsureBatchHandles();
        if (_skinSprite != null)
        {
            Object.Destroy(_skinSprite);
        }

        _skinSprite = sprite;
        if (_bodyBatchHandle != null)
        {
            _entityBatchRenderer.SetSprite(_bodyBatchHandle, sprite);
            if (!_isLocalPlayer)
            {
                _bodyBatchHandle.SetEnabled(sprite != null);
            }
        }
    }

    public void CreateTentacles(Texture2D tailTexture, Vector3 position)
    {
        ClearTentacles();
        if (_entityBatchRenderer == null)
        {
            return;
        }

        _tentacles = new Tentacle[StrandCount];
        _tentaclesSettled = false;
        for (int i = 0; i < StrandCount; i++)
        {
            _tentacles[i] = new Tentacle(
                _entityBatchRenderer,
                tailTexture,
                position,
                i);
        }
    }

    public void ClearTentacles()
    {
        if (_tentacles != null)
        {
            foreach (var tentacle in _tentacles)
            {
                tentacle?.Destroy();
            }

            _tentacles = null;
        }

        _forcedTailOffset = Vector3.zero;
        _tentaclesSettled = true;
    }

    public void SetTentaclesActive(bool active)
    {
        if (_tentacles == null)
        {
            return;
        }

        foreach (Tentacle? tentacle in _tentacles)
        {
            tentacle?.SetActive(active);
        }
    }

    public void SnapTentacles(Vector3 position)
    {
        ResetBodySway();
        _forcedTailOffset = Vector3.zero;
        if (_tentacles != null)
        {
            _tentaclesSettled = false;

            foreach (Tentacle? tentacle in _tentacles)
            {
                tentacle?.Snap(position);
            }
        }
    }

    public bool AreTentaclesSettled()
    {
        if (_tentacles == null)
        {
            return true;
        }

        foreach (Tentacle? tentacle in _tentacles)
        {
            if (tentacle != null && !tentacle.IsSettled)
            {
                return false;
            }
        }

        return true;
    }

    public void UpdateTentacles(Vector3 rootPosition, float movementFactor, float deltaTime)
    {
        if (_tentacles == null || _tentacles.Length == 0)
        {
            return;
        }

        float sharedStretch = _tentacles[0].TipDistanceTo(rootPosition);
        foreach (var tentacle in _tentacles)
        {
            tentacle.Update(
                rootPosition,
                movementFactor,
                deltaTime,
                sharedStretch,
                _forcedTailOffset);
        }
    }

    public void UpdateMotion(
        Vector3 position,
        float movementFactor,
        float deltaTime,
        bool bodySettled,
        TailMotionDebugMode tailMotionDebugMode)
    {
        UpdateBodySway(position, movementFactor, deltaTime);
        if (_tentacles == null)
        {
            _tentaclesSettled = true;
            return;
        }

        bool returningToNormal =
            tailMotionDebugMode == TailMotionDebugMode.Normal &&
            _tailMotionDebugMode != TailMotionDebugMode.Normal;
        if (returningToNormal)
        {
            SnapTentacles(position);
        }

        _tailMotionDebugMode = tailMotionDebugMode;
        float forcedSpeed = ForcedTailSpeed(tailMotionDebugMode);
        if (forcedSpeed > 0f)
        {
            _forcedTailOffset = AdvanceForcedOffset(
                _forcedTailOffset,
                _transform,
                forcedSpeed,
                deltaTime);
        }

        if (bodySettled && _tentaclesSettled && forcedSpeed <= 0f)
        {
            return;
        }

        float effectiveMovementFactor = forcedSpeed > 0f
            ? Mathf.Max(movementFactor, 1f)
            : movementFactor;

        if (bodySettled)
        {
            UpdateTentacles(position, effectiveMovementFactor, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            return;
        }

        bool tentacleStateChanged =
            !_tentaclesSettled ||
            (position - _lastTentacleRootPosition).sqrMagnitude > 1e-8f ||
            movementFactor > 0.0001f ||
            forcedSpeed > 0f;

        if (tentacleStateChanged)
        {
            UpdateTentacles(position, effectiveMovementFactor, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            _lastTentacleRootPosition = position;
        }
    }

    private void UpdateBodySway(Vector3 position, float movementFactor, float deltaTime)
    {
        float distance = _hasBodyPosition ? Vector2.Distance(position, _lastBodyPosition) : 0f;
        _lastBodyPosition = position;
        _hasBodyPosition = true;
        _bodySwayPhase = Mathf.Repeat(
            _bodySwayPhase + distance * BodySwayCyclesPerCell * Mathf.PI * 2f,
            Mathf.PI * 2f);
        float targetAngle = Mathf.Sin(_bodySwayPhase) * BodySwayDegrees * Mathf.Clamp01(movementFactor);
        _bodySwayAngle = Mathf.Lerp(_bodySwayAngle, targetAngle, 1f - Mathf.Exp(-BodySwayResponse * deltaTime));
        if (_bodyTransform != null)
        {
            _bodyTransform.localRotation = Quaternion.Euler(0f, 0f, _bodySwayAngle);
        }
    }

    private void ResetBodySway()
    {
        _hasBodyPosition = false;
        _bodySwayPhase = 0f;
        _bodySwayAngle = 0f;
        if (_bodyTransform != null)
        {
            _bodyTransform.localRotation = Quaternion.identity;
        }
    }

    public Transform EnsureClanIcon(ISceneObjectFactory sceneObjects, uint botId)
    {
        if (_clanTransform == null)
        {
            Transform? existingClan = _transform.Find("ClanIcon");
            GameObject clanGo = existingClan != null
                ? existingClan.gameObject
                : (sceneObjects != null
                    ? sceneObjects.Create("ClanIcon", RuntimeOwner.Robots)
                    : throw new System.InvalidOperationException(
                        $"[Robot] ISceneObjectFactory was not injected before creating ClanIcon for bot {botId}."));
            clanGo.transform.SetParent(_transform, worldPositionStays: false);
            _clanTransform = clanGo.transform;
            _clanTransform.localScale = Vector3.one * 0.8f;
        }

        return _clanTransform;
    }

    public void Destroy()
    {
        _entityBatchRenderer?.UnregisterSprite(_bodyBatchHandle);
        _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);
        if (_bodyTransform != null)
        {
            Object.Destroy(_bodyTransform.gameObject);
            _bodyTransform = null;
        }

        _bodyBatchHandle = null;
        _clanBatchHandle = null;

        if (_skinSprite != null)
        {
            Object.Destroy(_skinSprite);
            _skinSprite = null;
        }

        if (_clanSprite != null)
        {
            Object.Destroy(_clanSprite);
            _clanSprite = null;
        }

        ClearTentacles();
    }
}
