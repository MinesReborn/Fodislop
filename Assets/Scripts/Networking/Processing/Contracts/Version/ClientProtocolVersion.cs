#nullable enable

namespace Kern.Core;

public static class ClientProtocolVersion
{
    // Must stay aligned with the server's accepted ClientHello protocol
    // version. Version 7 removes the reserved action packet.
    // A development-only old-client path may still send 0.
    // Эту же версию сервер сверяет с MinimumClientVersion: устаревшему
    // клиенту приходит OutdatedClientPacket со ссылкой на скачивание.
    // Metadata-only handshake (docs/operations/protocol-trust-audit.md
    // §1.4); trust belongs to AuthToken only.
    public const int ClientVersion = 7;
}
