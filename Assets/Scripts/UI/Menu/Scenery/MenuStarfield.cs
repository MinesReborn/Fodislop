#nullable enable

using UnityEngine;

namespace Kern.UI
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
        private static readonly int _ShaderTimeID = Shader.PropertyToID("_ShaderTime");
        private static readonly int _AspectID = Shader.PropertyToID("_Aspect");
        private static readonly int _ParallaxOffsetID = Shader.PropertyToID("_ParallaxOffset");
        private static readonly int _DensityID = Shader.PropertyToID("_Density");
        private static readonly int _BrightnessID = Shader.PropertyToID("_Brightness");
        private static readonly int _CoreSizeID = Shader.PropertyToID("_CoreSize");
        private static readonly int _GlowSizeID = Shader.PropertyToID("_GlowSize");
        private static readonly int _TwinkleAmountID = Shader.PropertyToID("_TwinkleAmount");
        private static readonly int _TwinkleSpeedID = Shader.PropertyToID("_TwinkleSpeed");
        private static readonly int _SkyColorID = Shader.PropertyToID("_SkyColor");
        private static readonly int _NebulaIntensityID = Shader.PropertyToID("_NebulaIntensity");
        private static readonly int _NebulaColor1ID = Shader.PropertyToID("_NebulaColor1");
        private static readonly int _NebulaColor2ID = Shader.PropertyToID("_NebulaColor2");

        [SerializeField]
        private Material? _starfieldMaterial = null;
        private Material? _runtimeMaterial;
        private Material? _runtimeMaterialSource;

        private int _targetWidth = 1920;
        private int _targetHeight = 1080;

        private RenderTexture? _texture;

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

            _runtimeMaterial.SetFloat(_ShaderTimeID, 0f);
            _runtimeMaterial.SetFloat(_AspectID, (float)_texture.width / Mathf.Max(_texture.height, 1));
            _runtimeMaterial.SetVector(_ParallaxOffsetID, Vector4.zero);
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
            _runtimeMaterial.SetFloat(_DensityID, MenuStarfieldLook.Density);
            _runtimeMaterial.SetFloat(_BrightnessID, MenuStarfieldLook.Brightness);
            _runtimeMaterial.SetFloat(_CoreSizeID, MenuStarfieldLook.CoreSize);
            _runtimeMaterial.SetFloat(_GlowSizeID, MenuStarfieldLook.GlowSize);
            _runtimeMaterial.SetFloat(_TwinkleAmountID, MenuStarfieldLook.TwinkleAmount);
            _runtimeMaterial.SetFloat(_TwinkleSpeedID, MenuStarfieldLook.TwinkleSpeed);
            _runtimeMaterial.SetColor(_SkyColorID, MenuStarfieldLook.SkyColor);
            _runtimeMaterial.SetFloat(_NebulaIntensityID, MenuStarfieldLook.NebulaIntensity);
            _runtimeMaterial.SetColor(_NebulaColor1ID, MenuStarfieldLook.NebulaColor1);
            _runtimeMaterial.SetColor(_NebulaColor2ID, MenuStarfieldLook.NebulaColor2);
            _runtimeMaterial.SetFloat("_NebulaFbmStartAmplitude", MenuStarfieldLook.NebulaFbmStartAmplitude);
            _runtimeMaterial.SetFloat("_NebulaFbmFrequencyScale", MenuStarfieldLook.NebulaFbmFrequencyScale);
            _runtimeMaterial.SetFloat("_NebulaFbmAmplitudeDecay", MenuStarfieldLook.NebulaFbmAmplitudeDecay);
            _runtimeMaterial.SetInt("_NebulaFbmOctaves", MenuStarfieldLook.NebulaFbmOctaves);
            _runtimeMaterial.SetFloat("_NebulaCoordinateScale", MenuStarfieldLook.NebulaCoordinateScale);
            _runtimeMaterial.SetVector("_NebulaFbmShift", MenuStarfieldLook.NebulaFbmShift);
            _runtimeMaterial.SetVector("_NebulaWarpOffset", MenuStarfieldLook.NebulaWarpOffset);
            _runtimeMaterial.SetFloat("_NebulaWarpStrength", MenuStarfieldLook.NebulaWarpStrength);
            _runtimeMaterial.SetFloat("_NebulaDustThresholdStart", MenuStarfieldLook.NebulaDustThresholdStart);
            _runtimeMaterial.SetFloat("_NebulaDustThresholdEnd", MenuStarfieldLook.NebulaDustThresholdEnd);
            _runtimeMaterial.SetFloat("_NebulaGasThresholdStart", MenuStarfieldLook.NebulaGasThresholdStart);
            _runtimeMaterial.SetFloat("_NebulaGasThresholdEnd", MenuStarfieldLook.NebulaGasThresholdEnd);
            _runtimeMaterial.SetFloat("_NebulaGasContrast", MenuStarfieldLook.NebulaGasContrast);
            _runtimeMaterial.SetFloat("_NebulaGasMix", MenuStarfieldLook.NebulaGasMix);
            _runtimeMaterial.SetFloat("_StarPresenceThreshold", MenuStarfieldLook.StarPresenceThreshold);
            _runtimeMaterial.SetFloat("_StarMagnitudePower", MenuStarfieldLook.StarMagnitudePower);
            _runtimeMaterial.SetFloat("_StarMinimumRadiusScale", MenuStarfieldLook.StarMinimumRadiusScale);
            _runtimeMaterial.SetFloat("_StarMagnitudeRadiusScale", MenuStarfieldLook.StarMagnitudeRadiusScale);
            _runtimeMaterial.SetFloat("_StarWingDistanceScale", MenuStarfieldLook.StarWingDistanceScale);
            _runtimeMaterial.SetFloat("_StarWingMix", MenuStarfieldLook.StarWingMix);
            _runtimeMaterial.SetFloat("_StarRateMinimum", MenuStarfieldLook.StarRateMinimum);
            _runtimeMaterial.SetFloat("_StarRateRange", MenuStarfieldLook.StarRateRange);
            _runtimeMaterial.SetFloat("_StarSensorBreathingAmplitude", MenuStarfieldLook.StarSensorBreathingAmplitude);
            _runtimeMaterial.SetFloat("_StarMagnitudeFloor", MenuStarfieldLook.StarMagnitudeFloor);
            _runtimeMaterial.SetFloat("_StarPresenceHashOffset", MenuStarfieldLook.StarPresenceHashOffset);
            _runtimeMaterial.SetFloat("_StarMagnitudeHashOffset", MenuStarfieldLook.StarMagnitudeHashOffset);
            _runtimeMaterial.SetFloat("_StarPhaseHashOffset", MenuStarfieldLook.StarPhaseHashOffset);
            _runtimeMaterial.SetFloat("_StarRateHashOffset", MenuStarfieldLook.StarRateHashOffset);
            _runtimeMaterial.SetFloat("_StarColorHashOffset", MenuStarfieldLook.StarColorHashOffset);
            _runtimeMaterial.SetFloat("_SecondaryStarDensityScale", MenuStarfieldLook.SecondaryStarDensityScale);
            _runtimeMaterial.SetFloat("_SecondaryStarSizeScale", MenuStarfieldLook.SecondaryStarSizeScale);
            _runtimeMaterial.SetFloat("_SecondaryStarSeed", MenuStarfieldLook.SecondaryStarSeed);
            _runtimeMaterial.SetFloat("_SecondaryStarBrightness", MenuStarfieldLook.SecondaryStarBrightness);
            _runtimeMaterial.SetVector("_StarColorBlue", MenuStarfieldLook.StarColorBlue);
            _runtimeMaterial.SetVector("_StarColorWhite", MenuStarfieldLook.StarColorWhite);
            _runtimeMaterial.SetVector("_StarColorYellow", MenuStarfieldLook.StarColorYellow);
            _runtimeMaterial.SetVector("_StarColorOrange", MenuStarfieldLook.StarColorOrange);
            _runtimeMaterial.SetVector("_StarColorRed", MenuStarfieldLook.StarColorRed);
            _runtimeMaterial.SetFloat("_StarColorBlueEnd", MenuStarfieldLook.StarColorBlueEnd);
            _runtimeMaterial.SetFloat("_StarColorYellowEnd", MenuStarfieldLook.StarColorYellowEnd);
            _runtimeMaterial.SetFloat("_StarColorOrangeEnd", MenuStarfieldLook.StarColorOrangeEnd);
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
