#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Infrastructure.Audio;

public sealed class AudioBankManifestState : IAudioBankManifestState
{
    private IReadOnlyList<AssetsDefinitionPacket>? _banks;

    public int Revision { get; private set; }

    public bool TryGetBanks(out IReadOnlyList<AssetsDefinitionPacket> banks)
    {
        banks = _banks ?? Array.Empty<AssetsDefinitionPacket>();
        return _banks != null;
    }

    public void SetBanks(IReadOnlyList<AssetsDefinitionPacket> banks)
    {
        if (banks == null)
        {
            throw new ArgumentNullException(nameof(banks));
        }

        AssetsDefinitionPacket[] ordered = banks
            .GroupBy(bank => bank.Filename, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(bank => bank.Filename, StringComparer.Ordinal)
            .ToArray();
        if (_banks != null && _banks.SequenceEqual(ordered))
        {
            return;
        }

        _banks = ordered;
        Revision++;
    }
}
