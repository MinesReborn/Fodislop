#nullable enable

using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Core.Interfaces;

public interface IAudioBankManifestState
{
    /// <summary>Растёт при каждой смене набора банков: по нему звук узнаёт, что банки пора сверить.</summary>
    int Revision { get; }

    bool TryGetBanks(out IReadOnlyList<AssetsDefinitionPacket> banks);

    void SetBanks(IReadOnlyList<AssetsDefinitionPacket> banks);
}
