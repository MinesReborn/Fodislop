#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    // Draws the menu's starfield into a RenderTexture with no camera and no
    // geometry, so MainMenu can show it as a plain UI Image.
    //
    // It used to be a quad parented to a backdrop camera on its own layer, and
    // that is what put the sky on top of the game. The quad is world geometry,
    // its shader sits in the Background queue with ZTest Always and derives its
    // coordinates from screen position, so ANY camera that renders it repaints
    // the entire frame before that camera draws anything else. MainGame's camera
    // has cullingMask Everything, and MainMenu is not unloaded when the game
    // starts - it lives only for the menu scene - so the overlap was
    // guaranteed, not accidental.
    //
    // Culling masks and layers were the wrong tool for that: a layer only helps
    // against a camera that opts out, and every camera in this project opts in
    // to everything. Removing the geometry removes the failure mode instead of
    // guarding it. The shader needs no mesh - it reads
    // positionCS.xy / _ScreenParams.xy - so a full-screen blit is all it ever
    // needed. (UnpremultiplyAlpha.shader already proves TransformObjectToHClip
    // behaves under Graphics.Blit in this project.)
    [ExecuteAlways]
    public sealed class MenuStarfield : MonoBehaviour
    {
        private static readonly int ShaderTimeId = Shader.PropertyToID("_ShaderTime");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int ParallaxOffsetId = Shader.PropertyToID("_ParallaxOffset");

        [SerializeField]
        private Material? _starfieldMaterial;
        private Material? _runtimeMaterial;
        private Material? _runtimeMaterialSource;

        private int _targetWidth = 1920;
        private int _targetHeight = 1080;

        private RenderTexture? _texture;

        /// <summary>
        public RenderTexture? Texture => _texture;

        public void SetDisplaySize(int width, int height)
        {
            int w = Mathf.Max(width, MenuSceneryDefaults.MinimumRenderTextureSide);
            int h = Mathf.Max(height, MenuSceneryDefaults.MinimumRenderTextureSide);

            // Порог, а не точное сравнение: размер приходит из Update каждый
            // кадр и дрожит на пиксель от округлений раскладки, а пересоздание
            // текстуры — не бесплатная операция.
            if (_texture != null &&
                Mathf.Abs(_texture.width - w) <= MenuSceneryDefaults.RenderTextureResizeThresholdPixels &&
                Mathf.Abs(_texture.height - h) <= MenuSceneryDefaults.RenderTextureResizeThresholdPixels)
            {
                return;
            }

            _targetWidth = w;
            _targetHeight = h;

            ReleaseTexture();
            EnsureTexture();
        }

        private void OnEnable()
        {
            EnsureRuntimeMaterial();
            EnsureTexture();
        }

        private void OnDisable()
        {
            ReleaseTexture();
            ReleaseRuntimeMaterial();
        }

        private void OnDestroy()
        {
            ReleaseTexture();
            ReleaseRuntimeMaterial();
        }

        private bool _isDirty = true;

        /// <summary>
        /// Marks the starfield for re-rendering on the next frame or immediately.
        /// </summary>
        public void SetDirty()
        {
            _isDirty = true;
        }

        // Draws one frame of the starfield. Public so the editor capture tool can
        // drive it: LateUpdate does not run on demand outside Play Mode, and the
        // sky is invisible to every camera-based capture.
        public void RenderNow()
        {
            EnsureRuntimeMaterial();
            if (_runtimeMaterial == null)
            {
                return;
            }

            EnsureTexture();
            if (_texture == null)
            {
                return;
            }

            _runtimeMaterial.SetFloat(ShaderTimeId, 0f);
            _runtimeMaterial.SetFloat(AspectId, (float)_texture.width / Mathf.Max(_texture.height, 1));
            _runtimeMaterial.SetVector(ParallaxOffsetId, Vector4.zero);
            Graphics.Blit(Texture2D.whiteTexture, _texture, _runtimeMaterial);
            _isDirty = false;
        }

        private void LateUpdate()
        {
            if (_isDirty || _texture == null || !_texture.IsCreated())
            {
                RenderNow();
            }
        }

        private void EnsureTexture()
        {
            int width = _targetWidth;
            int height = _targetHeight;

            if (_texture != null && _texture.width == width && _texture.height == height)
            {
                return;
            }

            ReleaseTexture();

            // HDR: bright stars are deliberately over-range so the menu's bloom
            // has something to catch.
            _texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "MenuStarfieldRT",

                // Clamp, not the default Repeat: the UI Image samples right up
                // to the edge, and Repeat wraps in texels from the far side as a
                // visible seam.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            _texture.Create();

            // Свежая текстура пуста: пометить для перерисовки, иначе после
            // пересоздания (ресайз / re-enable) LateUpdate не отрисует звёзды.
            _isDirty = true;
        }

        private void ReleaseTexture()
        {
            if (_texture == null)
            {
                return;
            }

            _texture.Release();
            if (Application.isPlaying)
            {
                Destroy(_texture);
            }
            else
            {
                DestroyImmediate(_texture);
            }

            _texture = null;
        }

        private void EnsureRuntimeMaterial()
        {
            if (_starfieldMaterial == null)
            {
                ReleaseRuntimeMaterial();
                return;
            }

            if (_runtimeMaterial != null && ReferenceEquals(_runtimeMaterialSource, _starfieldMaterial))
            {
                return;
            }

            ReleaseRuntimeMaterial();
            _runtimeMaterial = new Material(_starfieldMaterial)
            {
                name = $"{_starfieldMaterial.name} (Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _runtimeMaterialSource = _starfieldMaterial;
        }

        private void ReleaseRuntimeMaterial()
        {
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }
            }

            _runtimeMaterial = null;
            _runtimeMaterialSource = null;
        }
    }
}
