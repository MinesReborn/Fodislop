using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IAudioSystem
    {
        AudioPlaybackHandle Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null);
        AudioPlaybackHandle PlayAttached(string eventName, GameObject targetGameObject, AudioLayer? overrideLayer = null, float? overrideVolume = null);
        AudioPlaybackHandle PlaySnapshot(string snapshotPath);
        AudioPlaybackHandle PlayAt(string eventName, Vector3 worldPosition, AudioLayer? layer = null, float? volume = null);
        AudioPlaybackHandle Play2D(string eventName, AudioLayer? layer = null, float? volume = null);
        void SetGlobalParameter(string parameterName, float value);
        float GetBusVolume(AudioBusType type);
        void SetBusVolume(AudioBusType type, float volume);
        UniTask<bool> EnsureBankLoadedAsync(string bankName);
        void UnloadBank(string bankName);
    }
}
