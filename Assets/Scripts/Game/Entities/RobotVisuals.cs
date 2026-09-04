#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Manages procedural visuals, sprite batching, skin/tail texture loading and tentacles for a Robot entity.
/// </summary>
public sealed class RobotVisuals
{
    private readonly Transform _transform;
    private readonly bool _isLocalPlayer;
    private WorldEntityBatchRenderer _entityBatchRenderer = null!;
    private WorldEntityBatchRenderer.SpriteHandle? _bodyBatchHandle;
    private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;
    private Transform? _clanTransform;
    private Sprite? _skinSprite;
    private Sprite? _clanSprite;
    private RobotAura? _aura;
    private Tentacle[]? _tentacles;
    private bool _tentaclesSettled;
    private Vector3 _lastTentacleRootPosition;
    private float _lastTentacleRotation;

    public Sprite? SkinSprite => _skinSprite;
    public Transform? ClanTransform => _clanTransform;
    public bool TentaclesSettled => _tentaclesSettled;

    public RobotVisuals(Transform transform, bool isLocalPlayer)
    {
        _transform = transform;
        _isLocalPlayer = isLocalPlayer;
    }

    public void Initialize(WorldEntityBatchRenderer entityBatchRenderer, Transform? clanTransform)
    {
        _entityBatchRenderer = entityBatchRenderer;
        _clanTransform = clanTransform;
        EnsureBatchHandles();
    }

    public void EnsureBatchHandles()
    {
        if (_entityBatchRenderer == null)
        {
            return;
        }

        _bodyBatchHandle ??= _entityBatchRenderer.RegisterSprite(_transform, 0);
        if (_clanTransform != null)
        {
            _clanBatchHandle ??= _entityBatchRenderer.RegisterSprite(_clanTransform, 100);
        }
    }

    public void SetBodyVisible(bool visible)
    {
        EnsureBatchHandles();
        _bodyBatchHandle?.SetEnabled(visible);
    }

    public void SetClanVisible(bool visible)
    {
        EnsureBatchHandles();
        _clanBatchHandle?.SetEnabled(visible);
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

        _tentacles = new Tentacle[4];
        _tentaclesSettled = false;
        float[] offsets = { -45f, -15f, 15f, 45f };
        for (int i = 0; i < 4; i++)
        {
            _tentacles[i] = new Tentacle(
                _entityBatchRenderer,
                tailTexture,
                position,
                offsets[i],
                i,
                4);
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

    public void UpdateTentacles(Vector3 rootPosition, float rotationAngle, float movementFactor, float deltaTime)
    {
        if (_tentacles == null)
        {
            return;
        }

        foreach (var tentacle in _tentacles)
        {
            tentacle?.Update(rootPosition, rotationAngle, movementFactor, deltaTime);
        }
    }

    public void UpdateMotion(Vector3 position, float rotationAngle, float movementFactor, float deltaTime, bool bodySettled)
    {
        if (_tentacles == null)
        {
            _tentaclesSettled = true;
            return;
        }

        if (bodySettled && _tentaclesSettled)
        {
            return;
        }

        if (bodySettled)
        {
            UpdateTentacles(position, rotationAngle, 0f, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            return;
        }

        bool tentacleStateChanged =
            !_tentaclesSettled ||
            (position - _lastTentacleRootPosition).sqrMagnitude > 1e-8f ||
            Mathf.Abs(Mathf.DeltaAngle(_lastTentacleRotation, rotationAngle)) > 0.001f ||
            movementFactor > 0.0001f;

        if (tentacleStateChanged)
        {
            UpdateTentacles(position, rotationAngle, movementFactor, deltaTime);
            _tentaclesSettled = AreTentaclesSettled();
            _lastTentacleRootPosition = position;
            _lastTentacleRotation = rotationAngle;
        }
    }

    /// <summary>
    /// Показывает или прячет кольцо вращающихся стрелок вокруг робота.
    /// </summary>
    /// <remarks>
    /// Кольцо создаётся при первом показе, а не вместе с роботом: у
    /// большинства роботов оно не загорится ни разу, а это шесть объектов
    /// сцены и столько же записей в батче на каждого.
    /// </remarks>
    public void SetAuraVisible(bool visible, ISceneObjectFactory? sceneObjects)
    {
        if (!visible && _aura == null)
        {
            return;
        }

        _aura ??= new RobotAura(_transform);
        _aura.SetVisible(visible, _entityBatchRenderer, sceneObjects);
    }

    public void TickAura(float deltaTime) => _aura?.Tick(deltaTime);

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
        _aura?.Destroy();
        _aura = null;
        _entityBatchRenderer?.UnregisterSprite(_bodyBatchHandle);
        _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);
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
