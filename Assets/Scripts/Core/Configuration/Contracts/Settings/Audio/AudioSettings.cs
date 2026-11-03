#nullable enable

using System;
using Kern.Audio.Core;
using UnityEngine.Serialization;


namespace Kern.Core;

[Serializable]
public sealed class AudioSettings
{
    public const float VolumeMin = 0f;
    public const float VolumeMax = 1f;

    [SettingRange(VolumeMin, VolumeMax)]
    [SettingLabel("menu.settings.master_volume")]
    [AudioBus(AudioBusType.Master)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    public float MasterVolume = 1f;

    [SettingRange(VolumeMin, VolumeMax)]
    [SettingLabel("menu.settings.sound_effects_volume")]
    [AudioBus(AudioBusType.SoundEffects)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    [FormerlySerializedAs("SfxVolume")]
    public float SoundEffectsVolume = 1f;

    [SettingRange(VolumeMin, VolumeMax)]
    [SettingLabel("menu.settings.music_volume")]
    [AudioBus(AudioBusType.Music)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    public float MusicVolume = 0.5f;

    [SettingRange(VolumeMin, VolumeMax)]
    [SettingLabel("menu.settings.ambience_volume")]
    [AudioBus(AudioBusType.Ambience)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    public float AmbienceVolume = 0.7f;

    [SettingRange(VolumeMin, VolumeMax)]
    [SettingLabel("settings.audio.voice")]
    [AudioBus(AudioBusType.Voice)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    public float VoiceVolume = 1f;

    [SettingRange(VolumeMin, VolumeMax)]
    [SettingLabel("menu.settings.ui_volume")]
    [AudioBus(AudioBusType.UI)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    public float UIVolume = 1f;

    [SettingUnbounded("Тумблер приглушения в фоне.")]
    [SettingLabel("menu.settings.mute_background")]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioSystem.OnApplicationFocus")]
    public bool MuteInBackground = true;
}
