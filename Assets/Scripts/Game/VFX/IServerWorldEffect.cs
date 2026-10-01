#nullable enable

namespace Kern.Game;

/// <summary>
/// Активный серверный мировой эффект (аудио-пакет или чистый VFX-пакет).
/// Живёт в списке ServerAudioEventManager и обновляется покадрово до завершения.
/// </summary>
public interface IServerWorldEffect
{
    bool IsDisposed { get; }

    void Update();

    void Dispose();
}
