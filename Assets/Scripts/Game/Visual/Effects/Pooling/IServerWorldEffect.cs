#nullable enable

namespace Kern.Game.Visual.Effects.Pooling;

/// <summary>
/// Активный серверный мировой эффект (аудио-пакет или чистый VisualEffect-пакет).
/// Живёт в системе воспроизведения серверных событий и обновляется покадрово до завершения.
/// </summary>
public interface IServerWorldEffect
{
    bool IsDisposed { get; }

    void Update();

    void Dispose();
}
