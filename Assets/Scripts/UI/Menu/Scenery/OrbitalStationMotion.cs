#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    // ExecuteAlways so the station sits at its correct orbital position (and
    // can be previewed/captured) in Edit Mode too, not only once Play Mode
    // starts ticking Update().
    [ExecuteAlways]
    public class OrbitalStationMotion : MonoBehaviour
    {
        [SerializeField]
        private Transform? _center;
        [SerializeField]
        private float _radius = MenuSceneryDefaults.OrbitRadius;
        [SerializeField]
        private float _startAngleDegrees;
        [SerializeField]
        private Vector3 _orbitPlaneEulerAngles = MenuSceneryDefaults.OrbitPlaneEulerAngles;

        private float _angleDegrees;

        private void OnEnable()
        {
            _angleDegrees = _startAngleDegrees;
            ApplyPosition();
        }

        private void ApplyPosition()
        {
            if (_center == null)
            {
                return;
            }

            var localOffset = new Vector3(
                Mathf.Cos(_angleDegrees * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(_angleDegrees * Mathf.Deg2Rad)) * _radius;
            Quaternion orbitPlane = Quaternion.Euler(_orbitPlaneEulerAngles);
            transform.position = _center.position + (orbitPlane * localOffset);
        }
    }
}
