#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.Core.Localization
{
    public class LocalizationService : ILocalizationService
    {
        private readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ILocalizableUI> _localizable = new();
        private readonly IClientConfigManager _clientConfig;

        public string CurrentLanguage { get; private set; } = "ru";

        public event Action? OnLanguageChanged;

        /// <summary>
        /// Единственная точка входа для UI: регистрация сразу применяет текст и
        /// вешает переприменение на смену языка. Повторная регистрация той же
        /// сущности идемпотентна (HashSet). Вызов до готовности дерева безопасен
        /// — ApplyLocalizedText вьюхи сам гвардится.
        /// </summary>
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
                _clientConfig.UpdateInterface(settings => settings.Language = CurrentLanguage);
            }

            OnLanguageChanged?.Invoke();

            // Реестр — основной канал переприменения: смена языка доходит до всех
            // зарегистрированных UI-сущностей независимо от того, подписался ли
            // кто-то на событие.
            foreach (ILocalizableUI target in _localizable)
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
                var dict = ParseSimpleJsonDictionary(asset.text);
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

        private static Dictionary<string, string> ParseSimpleJsonDictionary(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parsed = JsonUtility.FromJson<JsonDictionaryWrapper>("{\"items\":" + json + "}");
            // High performance token-based flat JSON parser for Unity compatibility
            int index = 0;
            while (index < json.Length)
            {
                int quoteKeyStart = json.IndexOf('"', index);
                if (quoteKeyStart == -1)
                {
                    break;
                }

                int quoteKeyEnd = json.IndexOf('"', quoteKeyStart + 1);
                if (quoteKeyEnd == -1)
                {
                    break;
                }

                string key = json.Substring(quoteKeyStart + 1, quoteKeyEnd - quoteKeyStart - 1);

                int colon = json.IndexOf(':', quoteKeyEnd);
                if (colon == -1)
                {
                    break;
                }

                int quoteValStart = json.IndexOf('"', colon);
                if (quoteValStart == -1)
                {
                    break;
                }

                int quoteValEnd = quoteValStart + 1;
                while (quoteValEnd < json.Length)
                {
                    if (json[quoteValEnd] == '"' && json[quoteValEnd - 1] != '\\')
                    {
                        break;
                    }

                    quoteValEnd++;
                }

                if (quoteValEnd >= json.Length)
                {
                    break;
                }

                string val = json.Substring(quoteValStart + 1, quoteValEnd - quoteValStart - 1)
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n");

                result[key] = val;
                index = quoteValEnd + 1;
            }

            return result;
        }

        [Serializable]
        private class JsonDictionaryWrapper
        {
            public string[]? Items;
        }
    }
}
