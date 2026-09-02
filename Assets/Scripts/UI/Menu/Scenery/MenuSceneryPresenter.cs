#nullable enable

using System;
using System.IO;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Owns the main menu's ambient scene: the starfield backdrop, the planet
    /// render target, the descent camera fly-in and the surface markers.
    /// </summary>
    internal sealed class MenuSceneryPresenter
    {
        private readonly IRuntimeAssetPaths _runtimeAssetPaths;

        public MenuSceneryPresenter(IRuntimeAssetPaths runtimeAssetPaths)
        {
            _runtimeAssetPaths = runtimeAssetPaths;
        }

        // Единый источник направления точки высадки: его используют и камера
        // подлёта, и ретикль на поверхности. Дублировать константу в двух
        // местах значило бы рискнуть тихим расхождением цели и маркера.
        internal static readonly Vector3 LandingSiteDirection = new(-0.48f, 0.10f, -0.87f);
        private const float DescentAnimationSeconds = 2.6f;

        private VisualElement? _tree;
        private Image? _spaceBgImage;
        private Image? _planetBodyImage;
        private Image? _loaderShade;
        private Image? _planetIcon;
        private VisualElement? _beacon;
        private VisualElement? _beaconPing;
        private VisualElement? _stationBadge;
        private VisualElement? _sidebar;
        private VisualElement? _targetReticle;

        private MenuSceneryController? _scenery;
        private MenuStarfield? _starfield;
        private float _scenerySearchStartedAt = -1f;
        private bool _scenerySearchWarned;

        private float _descentCameraProgress;
        private float _descentCameraTarget;

#if UNITY_EDITOR
        private float _uiBuiltAt;
        private int _uiBuiltFrame;
        private bool _planetTimingLogged;
#endif
        private bool _uiTexturesReady;

        public float DescentTarget
        {
            get => _descentCameraTarget;
            set => _descentCameraTarget = value;
        }

        public bool IsSceneryReady =>
            _uiTexturesReady &&
            _planetBodyImage != null &&
            _planetBodyImage.image != null &&
            _spaceBgImage != null &&
            _spaceBgImage.image != null;

        public void Tick(ref Texture2D? spaceBgTexture)
        {
            TryApplyStarfieldTexture(ref spaceBgTexture);
            TryApplySceneryTexture();
            Animate();
        }

        public void BindScene(MenuStarfield? starfield, MenuSceneryController? scenery)
        {
            _starfield = starfield;
            _scenery = scenery;
        }

        public void Bind(VisualElement tree)
        {
            _tree = tree;
            _spaceBgImage = tree.Q<Image>("SpaceBgImage");
            _planetBodyImage = tree.Q<Image>("MainMenuPlanetImage");
            if (_planetBodyImage != null && _scenery?.OutputTexture != null)
            {
                _planetBodyImage.image = _scenery.OutputTexture;
            }

            _loaderShade = tree.Q<Image>("LoaderShade");
            _planetIcon = tree.Q<Image>("MainMenuPlanetIcon");
            _beacon = tree.Q<VisualElement>("MainMenuBeacon");
            _beaconPing = tree.Q<VisualElement>("BeaconPing");
            _stationBadge = tree.Q<VisualElement>("StationBadge");
            _sidebar = tree.Q<VisualElement>(className: "mm-sidebar");
            _targetReticle = tree.Q<VisualElement>("TargetReticle");
        }

        public void MarkUIBuilt()
        {
#if UNITY_EDITOR
            _uiBuiltAt = Time.realtimeSinceStartup;
            _uiBuiltFrame = Time.frameCount;
            _planetTimingLogged = false;
#endif
        }

        public void ShutdownRenderers()
        {
            if (_scenery != null)
            {
                _scenery.gameObject.SetActive(false);
            }

            if (_starfield != null)
            {
                _starfield.gameObject.SetActive(false);
            }
        }

        public void ResumeRenderers()
        {
            if (_scenery != null)
            {
                _scenery.gameObject.SetActive(true);
            }

            if (_starfield != null)
            {
                _starfield.gameObject.SetActive(true);
            }
        }

        public void ApplyTextures(ref Texture2D? shadeTexture, ref Texture2D? spaceBgTexture)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            TryApplyStarfieldTexture(ref spaceBgTexture);
            TryApplySceneryTexture();

            ApplyImageTexture(_loaderShade, ref shadeTexture, "Assets/Textures/UI/mm_shade.png", nameof(_loaderShade));

            Texture2D? unusedLogoCache = null;
            ApplyImageTexture(_planetIcon, ref unusedLogoCache, "Assets/Textures/UI/mm_logo.png", nameof(_planetIcon));

            ApplyIconTexture("SideChronicleIcon", "Assets/Textures/UI/mm_icon_chronicle.png");
            ApplyIconTexture("SideSettingsIcon", "Assets/Textures/UI/mm_icon_settings.png");
            ApplyIconTexture("SideRepairIcon", "Assets/Textures/UI/mm_icon_repair.png");
            ApplyIconTexture("SideUpdateIcon", "Assets/Textures/UI/mm_icon_update.png");
            ApplyIconTexture("SideDiscordIcon", "Assets/Textures/UI/mm_icon_discord.png");
            ApplyIconTexture("SideTelegramIcon", "Assets/Textures/UI/mm_icon_telegram.png");
            ApplyIconTexture("SideVkIcon", "Assets/Textures/UI/mm_icon_vk.png");
            ApplyIconTexture("SideExitIcon", "Assets/Textures/UI/mm_icon_exit.png");

            _uiTexturesReady = true;
        }

        private void ApplyImageTexture(Image? image, ref Texture2D? cache, string assetPath, string debugName)
        {
            if (image == null)
            {
                Debug.LogWarning($"[MainMenu] Optional image '{debugName}' is missing from UXML ({assetPath}).");
                return;
            }

            if (cache == null)
            {
                cache = LoadDirectTexture(assetPath);
            }

            if (cache != null)
            {
                image.image = cache;
            }
            else
            {
                Debug.LogWarning($"[MainMenu] {debugName}: texture FAILED to load from '{assetPath}'");
            }
        }

        private void ApplyIconTexture(string elementName, string assetPath)
        {
            if (_tree == null)
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture('{elementName}'): _tree is null, UI not built yet");
                return;
            }

            var element = _tree.Q<VisualElement>(elementName);
            if (element == null)
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture: element '{elementName}' not found in UXML tree");
                return;
            }

            Texture2D? iconTex = LoadDirectTexture(assetPath);
            if (iconTex != null)
            {
                element.style.backgroundImage = new StyleBackground(iconTex);
            }
            else
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture('{elementName}'): texture FAILED to load from '{assetPath}'");
            }
        }

        private void TryApplyStarfieldTexture(ref Texture2D? spaceBgTexture)
        {
            if (_spaceBgImage == null)
            {
                return;
            }

            if (_starfield != null)
            {
                float resolvedWidth = _spaceBgImage.resolvedStyle.width;
                float resolvedHeight = _spaceBgImage.resolvedStyle.height;

                if (float.IsNaN(resolvedWidth) || resolvedWidth <= 1f ||
                    float.IsNaN(resolvedHeight) || resolvedHeight <= 1f)
                {
                    return;
                }

                float panelScale = _spaceBgImage.panel?.scaledPixelsPerPoint ?? 1f;
                _starfield.SetDisplaySize(
                    Mathf.RoundToInt(resolvedWidth * panelScale),
                    Mathf.RoundToInt(resolvedHeight * panelScale));

                if (_starfield.Texture != null && !ReferenceEquals(_spaceBgImage.image, _starfield.Texture))
                {
                    _spaceBgImage.image = _starfield.Texture;
                }

                return;
            }

            ApplyImageTexture(_spaceBgImage, ref spaceBgTexture, "Assets/Textures/UI/mm_space_bg.png", nameof(_spaceBgImage));
        }

        private void TryApplySceneryTexture()
        {
            if (_planetBodyImage == null)
            {
                if (!_scenerySearchWarned)
                {
                    Debug.LogWarning("[MainMenu] Optional 'MainMenuPlanetImage' element is missing from UXML.");
                    _scenerySearchWarned = true;
                }

                return;
            }

            if (_scenery == null)
            {
                if (_scenerySearchStartedAt < 0f)
                {
                    _scenerySearchStartedAt = Time.realtimeSinceStartup;
                }

                if (!_scenerySearchWarned &&
                    Time.realtimeSinceStartup - _scenerySearchStartedAt > 3f)
                {
                    Debug.LogWarning(
                        "[MainMenu] MenuSceneryController не зарегистрировался за 3 с — планета останется пустой.");
                    _scenerySearchWarned = true;
                }

                return;
            }

            _scenerySearchStartedAt = -1f;

            if (_scenery.OutputTexture != null &&
                !ReferenceEquals(_planetBodyImage.image, _scenery.OutputTexture))
            {
                _planetBodyImage.image = _scenery.OutputTexture;

#if UNITY_EDITOR
                if (!_planetTimingLogged)
                {
                    _planetTimingLogged = true;
                    Debug.Log(
                        $"[Планета] Текстура подставлена через {(Time.realtimeSinceStartup - _uiBuiltAt) * 1000f:F0} мс " +
                        $"после сборки UI, кадр {Time.frameCount - _uiBuiltFrame} от неё.");
                }
#endif
            }

            float resolvedWidth = _planetBodyImage.resolvedStyle.width;
            float resolvedHeight = _planetBodyImage.resolvedStyle.height;

            if (float.IsNaN(resolvedWidth) || resolvedWidth <= 1f ||
                float.IsNaN(resolvedHeight) || resolvedHeight <= 1f)
            {
                return;
            }

            float panelScale = _planetBodyImage.panel?.scaledPixelsPerPoint ?? 1f;
            _scenery.SetDisplaySize(
                Mathf.RoundToInt(resolvedWidth * panelScale),
                Mathf.RoundToInt(resolvedHeight * panelScale));
        }

        private Texture2D? LoadDirectTexture(string assetPath)
        {
            string relativePath = assetPath.StartsWith("Assets/Textures/", StringComparison.Ordinal)
                ? assetPath.Substring("Assets/Textures/".Length)
                : (assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    ? assetPath.Substring("Assets/".Length)
                    : assetPath);

            string? absolutePath = _runtimeAssetPaths.FindBundledTextureFile(relativePath);

            if (absolutePath == null)
            {
                return null;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(absolutePath);
                return RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
                    fileData,
                    Path.GetFileNameWithoutExtension(assetPath),
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    makeNoLongerReadable: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MainMenu] Failed to load direct texture '{absolutePath}': {ex.Message}");
                return null;
            }
        }

        private void UpdateDescentCamera()
        {
            if (Mathf.Approximately(_descentCameraProgress, _descentCameraTarget))
            {
                return;
            }

            _descentCameraProgress = Mathf.MoveTowards(
                _descentCameraProgress,
                _descentCameraTarget,
                Time.unscaledDeltaTime / DescentAnimationSeconds);

            _scenery?.SetDescentFraming(_descentCameraProgress, LandingSiteDirection);
        }

        public void Animate()
        {
            float time = Time.time;
            UpdateDescentCamera();
            MenuSceneryMarkers.Animate(
                time,
                _beacon,
                _beaconPing,
                _stationBadge,
                _sidebar,
                _targetReticle,
                _planetBodyImage,
                _scenery);
        }
    }
}
