#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio;
using Fodinae.Audio.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Audio Settings section in the pause menu.
/// </summary>
internal sealed class PauseMenuAudioTabBuilder
{
    private readonly IClientConfigManager _clientConfig;
    private readonly IAudioSystem _audioSystem;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public PauseMenuAudioTabBuilder(
        IClientConfigManager clientConfig,
        IAudioSystem audioSystem,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _clientConfig = clientConfig;
        _audioSystem = audioSystem;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView audioScroll)
    {
        VisualElement audioSection = audioScroll.Q<VisualElement>("AudioSection") ??
            throw new InvalidOperationException("[PauseMenu] AudioSection is missing from PauseMenu.uxml.");

        audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.master_volume"), AudioBusType.Master));
        audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.sfx_volume"), AudioBusType.SFX));
        audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.music_volume"), AudioBusType.Music));
        audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.ambience_volume"), AudioBusType.Ambience));
        audioSection.Add(CreateAudioSlider(_loc.Get("settings.audio.voice"), AudioBusType.Voice));
        audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.ui_volume"), AudioBusType.UI));
        Toggle muteInBackgroundToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("menu.settings.mute_background"),
            () => _clientConfig.Config.Audio.MuteInBackground,
            value => _clientConfig.UpdateAudio(
                settings => settings.MuteInBackground = value),
            _refreshers);
        audioSection.Add(muteInBackgroundToggle);

        return audioScroll;
    }

    private VisualElement CreateAudioSlider(string title, AudioBusType busType)
    {
        float currentVol = GetConfiguredBusVolume(busType);
        return PauseMenuUIFactory.CreateSlider(
            title,
            currentVol,
            v =>
            {
                if (_audioSystem.IsInitialized)
                {
                    _audioSystem.SetBusVolume(busType, v);
                }

                SetBusVolumeInConfig(busType, v);
            },
            0f,
            1f);
    }

    private float GetConfiguredBusVolume(AudioBusType busType)
    {
        if (_clientConfig == null || _clientConfig.Config == null)
        {
            return 1f;
        }

        return busType switch
        {
            AudioBusType.Master => _clientConfig.Config.Audio.MasterVolume,
            AudioBusType.SFX => _clientConfig.Config.Audio.SfxVolume,
            AudioBusType.Music => _clientConfig.Config.Audio.MusicVolume,
            AudioBusType.Voice => _clientConfig.Config.Audio.VoiceVolume,
            AudioBusType.Ambience => _clientConfig.Config.Audio.AmbienceVolume,
            AudioBusType.UI => _clientConfig.Config.Audio.UIVolume,
            _ => throw new ArgumentOutOfRangeException(nameof(busType), busType, "Unsupported audio bus."),
        };
    }

    private void SetBusVolumeInConfig(AudioBusType busType, float volume)
    {
        _clientConfig.UpdateAudio(settings =>
        {
            switch (busType)
            {
                case AudioBusType.Master:
                    settings.MasterVolume = volume;
                    break;
                case AudioBusType.SFX:
                    settings.SfxVolume = volume;
                    break;
                case AudioBusType.Music:
                    settings.MusicVolume = volume;
                    break;
                case AudioBusType.Voice:
                    settings.VoiceVolume = volume;
                    break;
                case AudioBusType.Ambience:
                    settings.AmbienceVolume = volume;
                    break;
                case AudioBusType.UI:
                    settings.UIVolume = volume;
                    break;
            }
        });
    }
}
