#nullable enable

using System;
using Fodinae.Audio.Core;

namespace Fodinae.Core;

/// <summary>Громкости по шинам.</summary>
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
    [SettingLabel("menu.settings.sfx_volume")]
    [AudioBus(AudioBusType.SFX)]
    [SettingConsumer(SettingConsumerTarget.AudioSystem, "AudioBusRegistry -> AudioSystem.SetBusVolume")]
    public float SfxVolume = 1f;

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
