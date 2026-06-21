using UnityEngine;

namespace Fodinae.Scripts.Effects
{
    public class ExplosionEffect : MonoBehaviour
    {
        private static Sprite[] _sprites;

        private SpriteRenderer _renderer;
        private int _currentFrame;
        private float _timer;

        public static void Play(Vector3 worldPos)
        {
            if (_sprites == null || _sprites.Length == 0)
            {
                _sprites = Resources.LoadAll<Sprite>("explosion");
                if (_sprites == null || _sprites.Length == 0)
                {
                    Debug.LogWarning("[ExplosionEffect] Sprites not found in Resources/explosion");
                    return;
                }
            }

            var go = new GameObject("ExplosionEffect");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -500;
            go.transform.position = worldPos;

            var effect = go.AddComponent<ExplosionEffect>();
            effect._renderer = sr;
            effect._currentFrame = 0;
            effect._timer = 0f;
            effect._renderer.sprite = _sprites[0];
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= 0.04f)
            {
                _timer -= 0.04f;
                _currentFrame++;
                if (_currentFrame >= _sprites.Length)
                {
                    Destroy(gameObject);
                    return;
                }
                _renderer.sprite = _sprites[_currentFrame];
            }
        }
    }
}
