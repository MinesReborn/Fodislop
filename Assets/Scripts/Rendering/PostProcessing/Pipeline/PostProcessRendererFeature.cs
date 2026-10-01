#nullable enable

using System;
using Kern.Core;
using Kern.Rendering.PostProcessing.Scopes;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [DisallowMultipleRendererFeature]
    public class PostProcessRendererFeature : ScriptableRendererFeature, IRenderer2DWorldGridProvider
    {
        public const string WorldUILayerName = ProjectRuntimeContracts.RequiredLayers.WorldUI;

        [Serializable]
        public sealed class Settings
        {
            [SerializeField]
            [Tooltip("Optional override. If empty, the feature loads Resources/Shaders/PostProcessing/PostProcess.compute.")]
            private ComputeShader? _computeShader = null;

            public ComputeShader? ComputeShader => _computeShader;
        }

        [SerializeField]
        private Settings _settings = new();

        private PostProcessRenderPass? _pass;
        private WorldBloomRenderPass? _worldBloomPass;
        private PostProcessRenderPass? _displayPass;
        private ScopesRenderPass? _scopesPass;
        private Camera? _mainCamera;

        internal bool RendersCamera(Camera camera) => _mainCamera == camera ||
            (_mainCamera == GameplayCamera.Resolve() && camera == PostProcessRuntimeState.DiagnosticOffscreenCamera);
        internal PostProcessWorkloadSnapshot? SceneWorkload => PostProcessRuntimeState.DiagnosticLegacyBloom
            ? _pass?.LatestWorkload : _worldBloomPass?.LatestWorkload;
        internal PostProcessWorkloadSnapshot? DisplayWorkload => _displayPass?.LatestWorkload;

        public bool TryGetWorldGrid(Camera camera, out Renderer2DWorldGridLayout layout)
        {
            layout = default;
            if (camera == PostProcessRuntimeState.DiagnosticOffscreenCamera &&
                PostProcessRuntimeState.DiagnosticFullResolutionWorld)
            {
                return false;
            }
            if (camera.cameraType != CameraType.Game ||
                (camera != GameplayCamera.Resolve() && camera != PostProcessRuntimeState.DiagnosticOffscreenCamera))
            {
                return false;
            }

            var binding = WorldRenderGridCamera.Capture(camera);
            var grid = binding.Grid;
            layout = new Renderer2DWorldGridLayout
            {
                Width = grid.Width,
                Height = grid.Height,
                View = binding.ViewMatrix,
                Projection = binding.ProjectionMatrix,
                ViewportToWorldUv = binding.ViewportToWorldUv,
                WorldRect = new Vector4((float)grid.WorldMinX, (float)grid.WorldMinY,
                    (float)grid.WorldWidth, (float)grid.WorldHeight),
            };
            return true;
        }

        public override void Create()
        {
            _worldBloomPass?.Dispose();
            _worldBloomPass = null;
            _displayPass = null;
            _pass = null;
            _scopesPass?.Dispose();
            _scopesPass = null;
            if (PostProcessRuntimeState.MainCamera == _mainCamera)
            {
                PostProcessRuntimeState.SetMainCamera(null);
            }

            _mainCamera = null;
        }

        private void EnsurePassCreated(Camera gameplayCamera)
        {
            // Renderer features outlive a game scope. Rebind the owner even
            // when the existing passes survive and only a diagnostic camera
            // renders this frame (the batch production image path).
            if (_mainCamera != gameplayCamera || PostProcessRuntimeState.MainCamera != gameplayCamera)
            {
                _mainCamera = gameplayCamera;
                PostProcessRuntimeState.SetMainCamera(gameplayCamera);
            }
            // Живой объект прохода ещё не значит живой шейдер: сборка плеера
            // выгружает несохранённую копию ComputeShader. Без этой проверки
            // постпроцесс в редакторе молча пропадал до перезагрузки домена.
            if (_pass != null && _pass.IsShaderAlive &&
                _displayPass != null && _displayPass.IsShaderAlive && _worldBloomPass is { IsAlive: true })
            {
                return;
            }

            _worldBloomPass?.Dispose();
            _worldBloomPass = null;
            _displayPass = null;
            _pass = null;
            // Ниже scopes создаются заново; старый проход освобождается здесь,
            // иначе при пересоздании он утекал бы вместе со своими буферами.
            _scopesPass?.Dispose();
            _scopesPass = null;

            var computeShader = _settings.ComputeShader != null
                ? _settings.ComputeShader
                : Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);

            if (computeShader == null)
            {
                throw new InvalidOperationException(
                    "PostProcessRendererFeature requires PostProcess.compute; " +
                    "the renderer feature cannot be disabled silently.");
            }

            _worldBloomPass = new WorldBloomRenderPass();
            _pass = new PostProcessRenderPass(computeShader);
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
            _displayPass = new PostProcessRenderPass(computeShader, displayPass: true);
            _displayPass.ConfigureInput(ScriptableRenderPassInput.Color);

            ComputeShader? scopesShader = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.ScopesCompute);
            if (scopesShader != null)
            {
                try
                {
                    _scopesPass = new ScopesRenderPass(scopesShader);
                    _scopesPass.ConfigureInput(ScriptableRenderPassInput.Color);
                }
                catch (Exception exception)
                {
                    _scopesPass?.Dispose();
                    _scopesPass = null;
                    Debug.LogError(
                        "[PostProcessRendererFeature] Scopes отключены, основной " +
                        $"постпроцесс продолжает работать: {exception.Message}");
                }
            }
            else
            {
                Debug.LogWarning(
                    "[PostProcessRendererFeature] Scopes.compute is missing; " +
                    "the grading scopes will remain unavailable.");
            }

            _mainCamera = gameplayCamera;
            PostProcessRuntimeState.SetMainCamera(_mainCamera);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                (cameraData.camera.targetTexture != null &&
                 cameraData.camera != PostProcessRuntimeState.DiagnosticOffscreenCamera))
            {
                return;
            }

            Camera? targetCamera = GameplayCamera.Resolve();
            if (targetCamera != null && cameraData.camera != targetCamera &&
                cameraData.camera != PostProcessRuntimeState.DiagnosticOffscreenCamera)
            {
                return;
            }

            EnsurePassCreated(targetCamera ?? cameraData.camera);
            if (_pass == null)
            {
                return;
            }

            if (cameraData.camera == targetCamera &&
                (_mainCamera != cameraData.camera ||
                 PostProcessRuntimeState.MainCamera != cameraData.camera))
            {
                _mainCamera = cameraData.camera;
                PostProcessRuntimeState.SetMainCamera(_mainCamera);
            }

            if (PostProcessRuntimeState.SkipPasses)
            {
                // World-grid presentation is required even during an artistic-effect bypass.
                renderer.EnqueuePass(_displayPass!);
                return;
            }

            bool scopesEnabled = _scopesPass != null && ScopesRenderPass.Enabled;
            if (PostProcessRuntimeState.DiagnosticLegacyBloom)
            {
                renderer.EnqueuePass(_pass);
            }
            else
            {
                renderer.EnqueuePass(_worldBloomPass!);
            }
            if (scopesEnabled && ScopesRenderPass.SourceMode == ScopesSourceMode.Before)
            {
                // Capture after Kern's scene-linear bloom pass, but before URP
                // applies post exposure and tonemapping.
                renderer.EnqueuePass(_scopesPass!);
            }

            renderer.EnqueuePass(_displayPass!);
            if (scopesEnabled && ScopesRenderPass.SourceMode == ScopesSourceMode.After)
            {
                renderer.EnqueuePass(_scopesPass!);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _worldBloomPass?.Dispose();
            _worldBloomPass = null;
            _displayPass = null;
            _pass = null;
            _scopesPass?.Dispose();
            _scopesPass = null;
            if (PostProcessRuntimeState.MainCamera == _mainCamera)
            {
                PostProcessRuntimeState.SetMainCamera(null);
            }

            _mainCamera = null;
        }
    }
}
