#nullable enable

using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Kern.Core.Interfaces;
public interface IRuntimeAssetPaths
{
    UniTask EnsureReadyAsync();
    IReadOnlyList<string> GetTextureFilenames();
    byte[]? GetTextureBytes(string relativePath);
    UniTask<IReadOnlyList<string>> GetAudioBankFilenamesAsync();
    UniTask<byte[]?> GetAudioBankBytesAsync(string relativePath);
}
