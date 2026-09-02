#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using VContainer;

namespace Fodinae.UI
{
    public class FloatingChatBubble : MonoBehaviour
    {
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private ISharedMaterialCache _sharedMaterials = null!;

        private TextMesh? _textMesh;
        private MeshRenderer? _meshRenderer;
        private MeshRenderer? _bgRenderer;
        private MeshFilter? _bgFilter;
        private Mesh? _bgMesh;
        private MaterialPropertyBlock? _bgPropertyBlock;
        private float _elapsed;
        private const float Duration = 5f;
        private const float FloatSpeed = 0.3f;
        private const float FadeStart = 4f;
        private Camera? _cam;
        private float _lastOrthoSize = -1f;

        private static readonly int[] QuadTriangles = { 0, 1, 2, 2, 1, 3 };
        private static readonly Vector2[] QuadUvs = { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        private readonly Vector3[] _quadVertices = new Vector3[4];

        private static Font? s_chatFont;

        public void Init(string text)
        {
            _cam = _gameplayCamera?.Camera;
            _elapsed = 0f;
            if (_textMesh == null)
            {
                _textMesh = gameObject.AddComponent<TextMesh>();
                // Legacy TextMesh has no fallback chain (unlike TMP/UI Toolkit),
                // and the default font has no CJK glyphs. The bundled Noto Sans SC
                // covers Latin/Cyrillic + Simplified & Traditional CJK in one font,
                // so chat text renders Chinese instead of boxes.
                s_chatFont ??= Resources.Load<Font>("Fonts/NotoSansSC-Regular");
                if (s_chatFont != null)
                {
                    _textMesh.font = s_chatFont;
                }

                _meshRenderer = GetComponent<MeshRenderer>();
                UnityRenderLayerContracts.ApplyWorldUI(_meshRenderer, 300);

                GameObject bgGo = _sceneObjects.Create("ChatBubbleBG", RuntimeOwner.FloatingUI);
                bgGo.transform.SetParent(transform, false);
                bgGo.transform.localPosition = new Vector3(0, 0, 0.01f);
                _bgFilter = bgGo.AddComponent<MeshFilter>();
                _bgRenderer = bgGo.AddComponent<MeshRenderer>();
                UnityRenderLayerContracts.ApplyWorldUI(_bgRenderer, 299);
                _bgRenderer.sharedMaterial = _sharedMaterials.GetForTexture(Texture2D.whiteTexture);
                _bgPropertyBlock = new MaterialPropertyBlock();
                SetBackgroundAlpha(0.5f);
            }

            _textMesh.text = text;
            UpdateBackgroundMesh();
            _textMesh.fontSize = 48;
            _textMesh.color = Color.white;
            _textMesh.anchor = TextAnchor.LowerCenter;
            _textMesh.alignment = TextAlignment.Center;

            if (_cam != null)
            {
                _lastOrthoSize = _cam.orthographicSize;
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

            bool isNewMesh = _bgMesh == null;
            _bgMesh ??= new Mesh { name = "ChatBubbleBackground" };

            _quadVertices[0] = new Vector3(-w / 2, -h / 2, 0);
            _quadVertices[1] = new Vector3(w / 2, -h / 2, 0);
            _quadVertices[2] = new Vector3(-w / 2, h / 2, 0);
            _quadVertices[3] = new Vector3(w / 2, h / 2, 0);

            _bgMesh.vertices = _quadVertices;
            if (isNewMesh)
            {
                _bgMesh.triangles = QuadTriangles;
                _bgMesh.uv = QuadUvs;
            }

            _bgMesh.RecalculateBounds();

            if (_bgFilter != null)
            {
                _bgFilter.sharedMesh = _bgMesh;
            }
        }

        private void SetBackgroundAlpha(float alpha)
        {
            if (_bgRenderer == null)
            {
                return;
            }

            _bgPropertyBlock ??= new MaterialPropertyBlock();
            _bgPropertyBlock.SetColor("_Color", new Color(0f, 0f, 0f, alpha));
            _bgRenderer.SetPropertyBlock(_bgPropertyBlock);
        }

        protected void Update()
        {
            _elapsed += Time.deltaTime;
            transform.Translate(0, FloatSpeed * Time.deltaTime, 0);

            if (_cam != null && _textMesh != null && !Mathf.Approximately(_cam.orthographicSize, _lastOrthoSize))
            {
                _lastOrthoSize = _cam.orthographicSize;
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);
            }

            if (_elapsed >= FadeStart && _textMesh != null)
            {
                float t = (_elapsed - FadeStart) / (Duration - FadeStart);
                Color c = _textMesh.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                _textMesh.color = c;
                SetBackgroundAlpha(Mathf.Lerp(0.5f, 0f, t));
            }

            if (_elapsed >= Duration)
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

            SetBackgroundAlpha(0.5f);
        }

        protected void OnDestroy()
        {
            if (_bgRenderer != null)
            {
                Destroy(_bgRenderer.gameObject);
            }

            if (_bgMesh != null)
            {
                Destroy(_bgMesh);
            }
        }
    }
}
