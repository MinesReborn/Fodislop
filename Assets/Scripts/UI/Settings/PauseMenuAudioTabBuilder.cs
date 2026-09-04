#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio;
using Fodinae.Audio.Core;
using Fodinae.Core;
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

        // Список ползунков — это список шин, а не его копия: подпись берётся из
        // [SettingLabel] над полем громкости, шина — из [AudioBus] там же.
        foreach (AudioBusRegistry.BusBinding binding in AudioBusRegistry.Buses)
        {
            audioSection.Add(CreateAudioSlider(binding));
        }

        Toggle muteInBackgroundToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("menu.settings.mute_background"),
            () => _clientConfig.Config.Audio.MuteInBackground,
            value => _clientConfig.UpdateSection(config => config.Audio,
                settings => settings.MuteInBackground = value),
            _refreshers);
        audioSection.Add(muteInBackgroundToggle);

        return audioScroll;
    }

    private VisualElement CreateAudioSlider(AudioBusRegistry.BusBinding binding)
    {
        string title = _loc.Get(
            SettingSchema.LabelOf<AudioSettings>(binding.VolumeField.Name));
        SettingRangeAttribute range =
            SettingSchema.RangeOf<AudioSettings>(binding.VolumeField.Name);
        return PauseMenuUIFactory.CreateBoundSlider(
            title,
            () => GetConfiguredBusVolume(binding),
            volume =>
            {
                if (_audioSystem.IsInitialized)
                {
                    _audioSystem.SetBusVolume(binding.Bus, volume);
                }

                _clientConfig.UpdateSection(
                    config => config.Audio,
                    settings => binding.Write(settings, volume));
            },
            range.Minimum,
            range.Maximum,
            _refreshers);
    }

    private float GetConfiguredBusVolume(AudioBusRegistry.BusBinding binding)
    {
        if (_clientConfig?.Config == null)
        {
            return 1f;
        }

        return binding.Read(_clientConfig.Config.Audio);
    }
}
