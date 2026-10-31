#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core.Interfaces;

namespace Kern.Infrastructure.Audio;

public sealed class AudioBankManifestState : IAudioBankManifestState
{
    private IReadOnlyList<string>? _filenames;

    public bool TryGetBankFilenames(out IReadOnlyList<string> filenames)
    {
        filenames = _filenames ?? Array.Empty<string>();
        return _filenames != null;
    }

    public void SetBankFilenames(IReadOnlyList<string> filenames)
    {
        if (filenames == null)
        {
            throw new ArgumentNullException(nameof(filenames));
        }

        _filenames = filenames
            .Distinct(StringComparer.Ordinal)
            .OrderBy(filename => filename, StringComparer.Ordinal)
            .ToArray();
    }
}
