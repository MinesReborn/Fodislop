using System;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Auth
{
    public static class AuthTokenManager
    {
        private const string PlayerPrefsKey = "AuthToken5";

        public static event Action<string> OnTokenChanged;

        public static string LoadToken()
        {
            return PlayerPrefs.GetString(PlayerPrefsKey, "");
        }

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
            OnTokenChanged?.Invoke("");
        }
    }
}
