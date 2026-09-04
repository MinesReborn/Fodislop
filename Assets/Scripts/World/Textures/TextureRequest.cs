#nullable enable

using Cysharp.Threading.Tasks;
using MinesServer.Data;

namespace Fodinae.World.Textures;

/// <summary>
/// Asynchronous request handle for loading a cell texture.
/// </summary>
public sealed class TextureRequest
{
    private readonly UniTaskCompletionSource<bool> _taskSource = new();

    public TextureRequest(CellType cellType)
    {
        CellType = cellType;
    }

    public CellType CellType { get; }

    public UniTask<bool> Task => _taskSource.Task;

    public void SetResult(bool success)
    {
        _taskSource.TrySetResult(success);
    }
}
