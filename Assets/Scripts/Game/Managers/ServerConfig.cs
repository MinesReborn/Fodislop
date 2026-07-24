using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class ServerConfig : MonoBehaviour
    {
        private const string TAG = "[ServerConfig]";
        private static ServerConfig _instance;
        public static ServerConfig Instance => _instance;

        public float DigCooldown = 0.3f;
        public int MaxGlobalChatLength = 50;
        public int MaxLocalChatLength = 20;

        protected void Awake()
        {
            _instance = this;
            Debug.Log($"{TAG} Initialized: DigCooldown={DigCooldown}, MaxGlobalChat={MaxGlobalChatLength}, MaxLocalChat={MaxLocalChatLength}");
        }
    }
}
