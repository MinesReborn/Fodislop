#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Kern.Core.Localization;
public class LocalizationService : ILocalizationService
{
    private readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ILocalizableUI> _localizable = new();
    private readonly IClientConfigManager _clientConfig;

    public string CurrentLanguage { get; private set; } = "ru";

    public event Action? OnLanguageChanged;

    public void RegisterLocalizable(ILocalizableUI target)
    {
        if (target == null || !_localizable.Add(target))
        {
            return;
        }

        target.ApplyLocalizedText();
    }

    public void UnregisterLocalizable(ILocalizableUI target)
    {
        if (target != null)
        {
            _localizable.Remove(target);
        }
    }

    [Inject]
    public LocalizationService(IClientConfigManager clientConfig)
    {
        _clientConfig = clientConfig ?? throw new ArgumentNullException(nameof(clientConfig));
        _clientConfig.EnsureInitialized();
        string initialLang = _clientConfig.Config.Interface.Language;
        SetLanguage(initialLang);
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            languageCode = "ru";
        }

        CurrentLanguage = languageCode.ToLowerInvariant();
        LoadTranslations();

        if (_clientConfig.Config.Interface.Language != CurrentLanguage)
        {
            _clientConfig.UpdateSection(config => config.Interface, settings => settings.Language = CurrentLanguage);
        }

        OnLanguageChanged?.Invoke();

        // Реестр — основной канал переприменения: смена языка доходит до всех
        // зарегистрированных UI-сущностей независимо от того, подписался ли
        // кто-то на событие.
        ILocalizableUI[] targets = new ILocalizableUI[_localizable.Count];
        _localizable.CopyTo(targets);
        foreach (ILocalizableUI target in targets)
        {
            target.ApplyLocalizedText();
        }
    }

    public string Get(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (!_translations.TryGetValue(key, out string? value))
        {
            value = key;
        }

        if (args != null && args.Length > 0 && !string.IsNullOrEmpty(value))
        {
            try
            {
                return string.Format(value, args);
            }
            catch (FormatException)
            {
                return value;
            }
        }

        return value ?? key;
    }

    public bool HasKey(string key)
    {
        return _translations.ContainsKey(key);
    }

    private void LoadTranslations()
    {
        _translations.Clear();
        LoadDictionaryInto(CurrentLanguage, _translations);
        if (_translations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Required localization dictionary '{CurrentLanguage}' is missing or empty.");
        }
    }

    private static void LoadDictionaryInto(string langCode, Dictionary<string, string> targetDict)
    {
        var asset = Resources.Load<TextAsset>($"Localization/{langCode}");
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
        {
            return;
        }

        try
        {
            Dictionary<string, string> dict = LocalizationDictionaryJson.Parse(asset.text);
            foreach (var kv in dict)
            {
                targetDict[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalizationService] Failed to parse localization for '{langCode}': {ex.Message}");
        }
    }

}
