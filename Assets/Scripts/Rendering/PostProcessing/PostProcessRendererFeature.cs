#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [DisallowMultipleRendererFeature]
    public class PostProcessRendererFeature : ScriptableRendererFeature
    {
        public const string WorldUILayerName = ProjectRuntimeContracts.RequiredLayers.WorldUI;

        [Serializable]
        public sealed class Settings
        {
            [SerializeField]
            [Tooltip("Optional override. If empty, the feature loads Resources/Shaders/PostProcessing/PostProcess.compute.")]
            private ComputeShader? _computeShader;

            [SerializeField]
            private bool _runInSceneView = true;

            [SerializeField]
            private bool _runInPreviewCameras;

            public ComputeShader? ComputeShader => _computeShader;
            public bool RunInSceneView => _runInSceneView;
            public bool RunInPreviewCameras => _runInPreviewCameras;
        }

        [SerializeField]
        private Settings _settings = new();

        private PostProcessRenderPass? _pass;
        private Camera? _mainCamera;

        public override void Create()
        {
            _pass?.Dispose();
            _pass = null;
            _mainCamera = null;
        }

        private void EnsurePassCreated(Camera gameplayCamera)
        {
            if (_pass != null)
            {
                return;
            }

            var computeShader = _settings.ComputeShader != null
                ? _settings.ComputeShader
                : Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);

            if (computeShader == null)
            {
                throw new InvalidOperationException(
                    "PostProcessRendererFeature requires PostProcess.compute; " +
                    "the renderer feature cannot be disabled silently.");
            }

            _pass = new PostProcessRenderPass(computeShader);
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
            _mainCamera = gameplayCamera;
            PostProcessRenderPass.SetMainCamera(_mainCamera);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera.targetTexture != null ||
                cameraData.camera != GameplayCamera.Resolve())
            {
                return;
            }

            EnsurePassCreated(cameraData.camera);
            if (_pass == null)
            {
                return;
            }

            if (_mainCamera != cameraData.camera)
            {
                _mainCamera = cameraData.camera;
                PostProcessRenderPass.SetMainCamera(_mainCamera);
            }

            PostProcessRenderPass.SetMainCamera(_mainCamera);

            if (!_settings.RunInSceneView && cameraData.isSceneViewCamera)
            {
                return;
            }

            if (!_settings.RunInPreviewCameras && cameraData.isPreviewCamera)
            {
                return;
            }

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }
    }
}
