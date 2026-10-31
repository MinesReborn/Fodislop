#nullable enable

using System.Collections.Generic;

namespace Kern.Core.Interfaces;

public interface IAudioBankManifestState
{
    bool TryGetBankFilenames(out IReadOnlyList<string> filenames);

    void SetBankFilenames(IReadOnlyList<string> filenames);
}
