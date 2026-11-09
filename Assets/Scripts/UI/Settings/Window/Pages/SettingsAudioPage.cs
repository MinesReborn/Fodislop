#nullable enable

using System;
using System.Collections.Generic;
using Kern.Audio;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Вкладка «Звук» — макет: громкость по шинам («85%») и «Звук в фоне».
// Список ползунков — это список шин: подпись из [SettingLabel], шина из [AudioBus] над полем громкости.
internal sealed class SettingsAudioPage
{
    private readonly IClientConfigManager _config;
    private readonly IAudioSystem _audio;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public SettingsAudioPage(
        IClientConfigManager config,
        IAudioSystem audio,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _config = config;
        _audio = audio;
        _refreshers = refreshers;
        _loc = loc;
    }

    public void Build(VisualElement page)
    {
        foreach (AudioBusRegistry.BusBinding binding in AudioBusRegistry.Buses)
        {
            AudioBusRegistry.BusBinding bus = binding;
            SettingRangeAttribute range = SettingSchema.RangeOf<Kern.Core.AudioSettings>(bus.VolumeField.Name);
            page.Add(SettingsFields.Range(
                _loc.Get(SettingSchema.LabelOf<Kern.Core.AudioSettings>(bus.VolumeField.Name)),
                range.Minimum,
                range.Maximum,
                () => bus.Read(_config.Config.Audio),
                volume =>
                {
                    if (_audio.IsInitialized)
                    {
                        _audio.SetBusVolume(bus.Bus, volume);
                    }

                    _config.UpdateSection(config => config.Audio, settings => bus.Write(settings, volume));
                },
                volume => Mathf.RoundToInt(volume * 100f) + "%",
                _refreshers));
        }

        // «Звук в фоне» включён — звук не глушится, когда окно игры не в фокусе
        VisualElement background = SettingsFields.Switch(
            _loc.Get("settings.window.background_sound"),
            () => !_config.Config.Audio.MuteInBackground,
            value => _config.UpdateSection(config => config.Audio, settings => settings.MuteInBackground = !value),
            _refreshers);
        background.AddToClassList("field--last");
        page.Add(background);
    }
}
