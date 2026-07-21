using UnityEngine;

namespace Fodinae.Scripts.Game
{
    [RequireComponent(typeof(Robot))]
    public class RobotHeadlight : MonoBehaviour
    {
        [SerializeField]
        private float _outerAngle = 60f;
        [SerializeField]
        private float _range = 25f;
        [SerializeField]
        private float _intensity = 1.0f;

        private Robot _robot;
        private float _angleCos;
        private bool _isEnabled = true;

        protected void Awake()
        {
            _robot = GetComponent<Robot>();
            _angleCos = Mathf.Cos(_outerAngle * 0.5f * Mathf.Deg2Rad);
        }

        protected void OnEnable()
        {
            _isEnabled = true;
        }

        protected void OnDisable()
        {
            _isEnabled = false;
        }

        protected void LateUpdate()
        {
            if (!_isEnabled)
            {
                return;
            }

            float angleRad = transform.eulerAngles.z * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(-Mathf.Sin(angleRad), Mathf.Cos(angleRad));
            Vector2 pos = (Vector2)transform.position + (dir * 0.5f);

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
