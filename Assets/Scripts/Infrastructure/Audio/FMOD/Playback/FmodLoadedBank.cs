#nullable enable

namespace Kern.Infrastructure.Audio;

internal readonly struct FmodLoadedBank
{
    public FmodLoadedBank(string eTag, FMOD.Studio.Bank bank)
    {
        ETag = eTag;
        Bank = bank;
    }

    public string ETag { get; }

    public FMOD.Studio.Bank Bank { get; }
}
