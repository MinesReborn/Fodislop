using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IAssetLoader
    {
        UniTask<string> GetAssetPathAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 5);
        UniTask<Texture2D> GetTextureAsync(string filename, CancellationToken cancellationToken = default);
    }
}
