using UnityEngine;

namespace Fodinae.Scripts.UI
{
    public class FloatingChatBubble : MonoBehaviour
    {
        private TextMesh _textMesh;
        private MeshRenderer _meshRenderer;
        private MeshRenderer _bgRenderer;
        private float _elapsed;
        private const float DURATION = 5f;
        private const float FLOAT_SPEED = 0.3f;
        private const float FADE_START = 4f;
        private Camera _cam;

        public void Init(string text)
        {
            _cam = Camera.main;
            _elapsed = 0f;

            if (_textMesh == null)
            {
                _textMesh = gameObject.AddComponent<TextMesh>();
                _meshRenderer = GetComponent<MeshRenderer>();
                _meshRenderer.sortingOrder = 300;

                var bgGo = new GameObject("ChatBubbleBG");
                bgGo.transform.SetParent(transform, false);
                bgGo.transform.localPosition = new Vector3(0, 0, 0.01f);
                bgGo.AddComponent<MeshFilter>();
                _bgRenderer = bgGo.AddComponent<MeshRenderer>();
                _bgRenderer.sortingOrder = 299;
                _bgRenderer.material = new Material(Shader.Find("Sprites/Default"));
                _bgRenderer.material.color = new Color(0, 0, 0, 0.5f);
            }

            _textMesh.text = text;
            UpdateBackgroundMesh();
            _textMesh.fontSize = 48;
            _textMesh.color = Color.white;
            _textMesh.anchor = TextAnchor.LowerCenter;
            _textMesh.alignment = TextAlignment.Center;

            if (_cam != null)
            {
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);
            }

            gameObject.SetActive(true);
        }

        private void UpdateBackgroundMesh()
        {
            if (_textMesh == null || _bgRenderer == null)
            {
                return;
            }

            float textWidth = _textMesh.text.Length * 0.12f;
            float w = Mathf.Max(textWidth, 1.5f) + 0.4f;
            const float h = 0.3f;

            var mesh = new Mesh();
            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-w / 2, -h / 2, 0),
                new Vector3(w / 2, -h / 2, 0),
                new Vector3(-w / 2, h / 2, 0),
                new Vector3(w / 2, h / 2, 0),
            };
            mesh.vertices = vertices;
            mesh.triangles = new int[] { 0, 1, 2, 2, 1, 3 };
            mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.RecalculateBounds();

            var filter = _bgRenderer.GetComponent<MeshFilter>();
            if (filter != null)
            {
                Object.Destroy(filter.sharedMesh);
                filter.mesh = mesh;
            }
        }

        protected void Update()
        {
            _elapsed += Time.deltaTime;
            transform.Translate(0, FLOAT_SPEED * Time.deltaTime, 0);

            if (_cam != null)
            {
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);
            }

            if (_elapsed >= FADE_START)
            {
                float t = (_elapsed - FADE_START) / (DURATION - FADE_START);
                Color c = _textMesh.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                _textMesh.color = c;
                if (_bgRenderer != null)
                {
                    Color bgC = _bgRenderer.material.color;
                    bgC.a = Mathf.Lerp(0.5f, 0f, t);
                    _bgRenderer.material.color = bgC;
                }
            }

            if (_elapsed >= DURATION)
            {
                gameObject.SetActive(false);
            }
        }

        protected void OnDisable()
        {
            _elapsed = 0f;
            if (_textMesh != null)
            {
                var c = _textMesh.color;
                c.a = 1f;
                _textMesh.color = c;
            }

            if (_bgRenderer != null)
            {
                var bgC = _bgRenderer.material.color;
                bgC.a = 0.5f;
                _bgRenderer.material.color = bgC;
            }
        }

        protected void OnDestroy()
        {
            if (_bgRenderer != null)
            {
                Destroy(_bgRenderer.material);
                Destroy(_bgRenderer.gameObject);
            }
        }
    }
}
