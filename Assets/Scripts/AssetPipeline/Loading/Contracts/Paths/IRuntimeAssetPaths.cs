#nullable enable

using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Kern.Core.Interfaces;
public interface IRuntimeAssetPaths
{
    UniTask EnsureReadyAsync();

    /// <summary>Textures shipped as client-owned content, such as UI, item icons and the surface scene.</summary>
    IReadOnlyList<string> GetEmbeddedTextureFilenames();

    /// <summary>Local world textures used by DummyConnection; online worlds use their server asset manifest.</summary>
    IReadOnlyList<string> GetDummyWorldTextureFilenames();

    /// <summary>Looks up bytes in either local package group.</summary>
    byte[]? GetTextureBytes(string relativePath);
    UniTask<IReadOnlyList<string>> GetAudioBankFilenamesAsync();
    UniTask<byte[]?> GetAudioBankBytesAsync(string relativePath);
}
