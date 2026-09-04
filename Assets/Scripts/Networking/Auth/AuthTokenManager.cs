#nullable enable

using UnityEngine;

namespace Fodinae.Networking.Auth
{
    public interface IGameTokenStore
    {
        bool HasToken { get; }

        string Load();

        void Save(string token);

        void Clear();
    }

    public sealed class GameTokenStore : IGameTokenStore
    {
        private const string PlayerPrefsKey = "AuthToken6";

        public bool HasToken => PlayerPrefs.HasKey(PlayerPrefsKey);

        public string Load() => PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);

        public void Save(string token)
        {
            PlayerPrefs.SetString(PlayerPrefsKey, token);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
        }
    }
}
