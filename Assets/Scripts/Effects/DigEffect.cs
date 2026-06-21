using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;

namespace Fodinae.Scripts.Effects
{
    public class DigEffect : MonoBehaviour
    {
        private static Sprite[] _sprites;

        private SpriteRenderer _renderer;
        private int _currentFrame;
        private float _frameDuration;
        private float _timer;

        public static UniTaskVoid Play(int worldX, int worldY, int worldHeight, Direction direction)
        {
            if (_sprites == null || _sprites.Length == 0)
            {
                _sprites = Resources.LoadAll<Sprite>("fx");
                if (_sprites == null || _sprites.Length == 0)
                {
                    Debug.LogWarning("[DigEffect] Sprites not found in Resources/fx");
                    return default;
                }
            }

            var go = new GameObject("DigEffect");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -500;

            float unityY = worldHeight - 1 - worldY;
            go.transform.position = new Vector3(worldX + 0.5f, unityY + 0.5f, 0);

            // Поворот по направлению робота
            float angle = direction switch
            {
                Direction.Up => 0f,
                Direction.Right => -90f,
                Direction.Down => 180f,
                Direction.Left => 90f,
                _ => 0f
            };
            go.transform.rotation = Quaternion.Euler(0, 0, angle);

            var effect = go.AddComponent<DigEffect>();
            effect._renderer = sr;
            effect._frameDuration = 0.04f; // ~6 FPS, не зависит от FPS игры
            effect._currentFrame = 0;
            effect._timer = 0f;
            effect._renderer.sprite = _sprites[0];

            return default;
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            if (_timer >= _frameDuration)
            {
                _timer -= _frameDuration;
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
