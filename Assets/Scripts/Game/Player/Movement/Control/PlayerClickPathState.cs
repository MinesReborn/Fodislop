#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game.Player.Pathfinding;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Player.Movement;

internal sealed class PlayerClickPathState
{
    private ClickPathRenderer? _renderer;

    public List<Vector2Int>? Path { get; private set; }
    public int Index { get; set; }
    public ClickPathRenderer? Renderer => _renderer;
    public event Action<IReadOnlyList<Vector2Int>?>? PathChanged;

    public void BindRenderer(ClickPathRenderer? renderer) => _renderer = renderer;

    public bool TryStart(
        Vector2Int target,
        bool movementLocked,
        bool hasServerPosition,
        IWorldDataStorage? storage,
        IMapDataProvider? mapDataProvider,
        Vector2Int position,
        Action cancelPath,
        Action<Vector2Int> rotate,
        ISceneObjectFactory sceneObjects,
        Transform owner)
    {
        if (!ClickPathRequestHandler.TryFindPath(
                target,
                movementLocked,
                hasServerPosition,
                storage,
                mapDataProvider,
                position,
                cancelPath,
                rotate,
                out List<Vector2Int>? path))
        {
            return false;
        }

        Path = path;
        Index = 0;
        PathChanged?.Invoke(Path);
        _renderer?.EnsureView(sceneObjects);
        _renderer?.Show(Path, 0, mapDataProvider, owner.position.z);
        return true;
    }

    public void Cancel()
    {
        if (Path == null)
        {
            return;
        }

        Path = null;
        Index = 0;
        PathChanged?.Invoke(null);
        _renderer?.Hide();
    }
}
