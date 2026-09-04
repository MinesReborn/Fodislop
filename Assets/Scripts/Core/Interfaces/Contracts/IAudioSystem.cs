#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Core;
using UnityEngine;

namespace Fodinae.Core.Interfaces
{
    public interface IAudioPlaybackHandle
    {
        AudioBusType BusType { get; }
        bool IsPlaying { get; }

        void Stop(float fadeOut = 0f);
        void SetPosition(Vector3 worldPosition);
        void SetVolume(float linearVolume);
        void SetPitch(float pitch);
        void SetParameter(string parameterName, float value);
    }

    public interface IAudioSystem
    {
        bool IsInitialized { get; }

        bool IsDegraded { get; }

        IAudioPlaybackHandle? Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null);
        IAudioPlaybackHandle? PlayAttached(string eventName, GameObject targetGameObject, AudioLayer? overrideLayer = null, float? overrideVolume = null);
        IAudioPlaybackHandle? PlayAt(string eventName, Vector3 worldPosition, AudioLayer? layer = null, float? volume = null);
        IAudioPlaybackHandle? Play2D(string eventName, AudioLayer? layer = null, float? volume = null);
        float GetBusVolume(AudioBusType type);
        void SetBusVolume(AudioBusType type, float volume);

        /// <summary>
        /// Завершается, когда банки FMOD загружены и их сэмплы дозагружены.
        /// Переходы сцен ждут этого, прежде чем объявить сцену готовой:
        /// иначе первые звуки сцены стартуют тишиной.
        /// </summary>
        UniTask WaitUntilBanksReadyAsync(CancellationToken cancellationToken = default);
    }
}
