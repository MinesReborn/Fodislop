using System;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Auth
{
    public static class AuthTokenManager
    {
        private const string PlayerPrefsKey = "AuthToken6";

        public static event Action<string> OnTokenChanged;

        public static string LoadToken() => PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);

        public static bool HasToken => PlayerPrefs.HasKey(PlayerPrefsKey);

        public static void SaveToken(string token)
        {
            PlayerPrefs.SetString(PlayerPrefsKey, token);
            PlayerPrefs.Save();
            OnTokenChanged?.Invoke(token);
        }

        public static void ClearToken()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
            OnTokenChanged?.Invoke(string.Empty);
        }
    }
}
