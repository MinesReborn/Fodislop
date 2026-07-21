using System;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Хендл активного проигрывания — возвращается методом AudioSystem.Play().
    ///
    /// Позволяет:
    /// <list type="bullet">
    ///   <item>Остановить звук досрочно: <c>handle.Stop(fadeOut: 0.3f)</c></item>
    ///   <item>Подвинуть позицию за источником: <c>handle.SetPosition(target.position)</c></item>
    ///   <item>Узнать играет ли ещё: <c>handle.IsPlaying</c></item>
    ///   <item>Менять громкость на лету: <c>handle.SetVolume(0.5f)</c></item>
    /// </list>
    /// </summary>
    public sealed class AudioPlaybackHandle
    {
        /// <summary>Шина на которой играет звук.</summary>
        public AudioBus Bus { get; }

        /// <summary>Играет ли звук прямо сейчас.</summary>
        public bool IsPlaying
        {
            get
            {
                if (_disposed)
                {
                    return false;
                }

                return _isPlayingFunc?.Invoke(this) ?? false;
            }
        }

        /// <summary>Идентификатор для сопоставления с внутренним голосом бэкенда.</summary>
        public readonly int HandleId;

        internal bool _disposed;
        internal global::System.Func<AudioPlaybackHandle, bool> _isPlayingFunc;
        internal global::System.Action<AudioPlaybackHandle, float> _stopAction;
        internal global::System.Action<AudioPlaybackHandle, Vector3> _positionAction;
        internal global::System.Action<AudioPlaybackHandle, float> _volumeAction;
        internal global::System.Action<AudioPlaybackHandle, float> _pitchAction;

        /// <summary>Создаётся бэкендом. Не инстанциируй руками.</summary>
        internal AudioPlaybackHandle(int id, AudioBus bus)
        {
            HandleId = id;
            Bus = bus;
        }

        /// <summary>Остановить с плавным затуханием за fadeOut секунд.</summary>
        public void Stop(float fadeOut = 0f)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopAction?.Invoke(this, fadeOut);
        }

        /// <summary>Установить позицию в мире (для пространственных звуков).</summary>
        public void SetPosition(Vector3 worldPosition)
        {
            if (_disposed)
            {
                return;
            }

            _positionAction?.Invoke(this, worldPosition);
        }

        /// <summary>Изменить громкость этого конкретного голоса (0..+∞, линейно).</summary>
        public void SetVolume(float linearVolume)
        {
            if (_disposed)
            {
                return;
            }

            _volumeAction?.Invoke(this, Mathf.Max(0f, linearVolume));
        }

        /// <summary>Изменить питч этого конкретного голоса.</summary>
        public void SetPitch(float pitch)
        {
            if (_disposed)
            {
                return;
            }

            _pitchAction?.Invoke(this, Mathf.Clamp(pitch, 0.01f, 4f));
        }
    }
}
