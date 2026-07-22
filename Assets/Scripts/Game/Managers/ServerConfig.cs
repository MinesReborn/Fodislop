using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class ServerConfig : MonoBehaviour
    {
        private const string TAG = "[ServerConfig]";
        private static ServerConfig _instance;
        public static ServerConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<ServerConfig>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[ServerConfig]");
                        _instance = go.AddComponent<ServerConfig>();
                    }
                }

                return _instance;
            }
        }

        public float DigCooldown = 0.3f;
        public int MaxGlobalChatLength = 50;
        public int MaxLocalChatLength = 20;

        protected void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"{TAG} Initialized: DigCooldown={DigCooldown}, MaxGlobalChat={MaxGlobalChatLength}, MaxLocalChat={MaxLocalChatLength}");
        }
    }
}
