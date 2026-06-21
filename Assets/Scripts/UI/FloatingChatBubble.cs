using UnityEngine;

namespace Fodinae.Scripts.UI
{
    public class FloatingChatBubble : MonoBehaviour
    {
        private TextMesh _textMesh;
        private float _elapsed = 0f;
        private const float DURATION = 5f;
        private const float FLOAT_SPEED = 0.3f;
        private const float FADE_START = 4f;
        private Camera _cam;

        public void Init(string text)
        {
            _cam = Camera.main;
            _textMesh = gameObject.AddComponent<TextMesh>();
            _textMesh.text = text;
            _textMesh.fontSize = 48;
            _textMesh.color = Color.white;
            _textMesh.anchor = TextAnchor.LowerCenter;
            _textMesh.alignment = TextAlignment.Center;

            var renderer = GetComponent<MeshRenderer>();
            renderer.sortingOrder = 300;

            if (_cam != null)
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);

        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            transform.Translate(0, FLOAT_SPEED * Time.deltaTime, 0);

            if (_cam != null)
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);

            if (_elapsed >= FADE_START)
            {
                float t = (_elapsed - FADE_START) / (DURATION - FADE_START);
                Color c = _textMesh.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                _textMesh.color = c;
            }

            if (_elapsed >= DURATION)
                Destroy(gameObject);
        }
    }
}
