using Fodinae.Scripts.Player.Logic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game
{
    public class RobotHeadlight : MonoBehaviour
    {
        [SerializeField]
        private float _outerAngle = 60f;
        [SerializeField]
        private float _range = 25f;
        [SerializeField]
        private float _intensity = 1.0f;

        private float _angleCos;
        private bool _isEnabled = true;

        protected void Awake()
        {
            _angleCos = Mathf.Cos(_outerAngle * 0.5f * Mathf.Deg2Rad);
        }

        protected void OnEnable()
        {
            _isEnabled = true;
        }

        protected void OnDisable()
        {
            _isEnabled = false;
            Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
        }

        protected void OnDestroy()
        {
            Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
        }

        protected void LateUpdate()
        {
            if (!_isEnabled)
            {
                Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
                return;
            }

            var player = PlayerMovementController.LocalPlayer
                ?? GetComponent<PlayerMovementController>()
                ?? GetComponentInParent<PlayerMovementController>()
                ?? FindAnyObjectByType<PlayerMovementController>();

            if (player == null)
            {
                // No active player found in scene — disable global headlight intensity to prevent orphan light spots on map
                Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
                return;
            }

            Vector2 dir = player.transform.up;
            Vector2 pos = (Vector2)player.transform.position + (dir * 0.5f);

            Shader.SetGlobalVector("_HeadlightPos", pos);
            Shader.SetGlobalVector("_HeadlightDir", dir);
            Shader.SetGlobalFloat("_HeadlightAngleCos", _angleCos);
            Shader.SetGlobalFloat("_HeadlightRange", _range);
            Shader.SetGlobalFloat("_HeadlightIntensity", _intensity);
        }

        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled)
            {
                Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
            }
        }
    }
}
