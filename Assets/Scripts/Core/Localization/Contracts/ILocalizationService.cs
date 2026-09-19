#nullable enable

using System;

namespace Kern.Core.Localization;
public interface ILocalizationService
{
    string CurrentLanguage { get; }

    event Action? OnLanguageChanged;

    void RegisterLocalizable(ILocalizableUI target);

    void UnregisterLocalizable(ILocalizableUI target);

    void SetLanguage(string languageCode);

    string Get(string key, params object[] args);

    bool HasKey(string key);
}
