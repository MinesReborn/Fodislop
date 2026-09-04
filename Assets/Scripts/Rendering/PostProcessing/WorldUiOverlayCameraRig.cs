#nullable enable

using System;
using System.Linq;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    /// <summary>
    /// Держит наложенную камеру мирового интерфейса в согласии с основной.
    /// </summary>
    /// <remarks>
    /// Выделено из <see cref="PostProcessController"/>. Тот отвечает за
    /// эффекты кадра, а здесь — сложение двух камер: интерфейс мира рисуется
    /// отдельной наложенной камерой, чтобы не попадать под постпроцесс, и
    /// её проекцию, отсечение и место в стеке приходится каждый кадр
    /// сверять с основной. Общего с настройкой эффектов у этой работы
    /// только то, что обе жили в одном файле.
    /// </remarks>
    internal sealed class WorldUiOverlayCameraRig
    {
        private readonly ISceneObjectFactory _sceneObjects;

        private Camera? _configuredMainCamera;
        private UniversalAdditionalCameraData? _configuredMainCameraData;
        private UniversalAdditionalCameraData? _cachedMainCameraData;
        private Camera? _worldUICamera;
        private UniversalAdditionalCameraData? _worldUICameraData;
        private int _worldUILayerMask;

        private float _lastOrthographicSize;
        private float _lastFieldOfView;
        private float _lastNearClipPlane;
        private float _lastFarClipPlane;
        private Matrix4x4 _lastProjection;
        private bool _hasProjection;

        public WorldUiOverlayCameraRig(ISceneObjectFactory sceneObjects)
        {
            _sceneObjects = sceneObjects ?? throw new ArgumentNullException(nameof(sceneObjects));
        }

        public Camera? ConfiguredMainCamera => _configuredMainCamera;

        public int WorldUILayerMask => _worldUILayerMask;

        /// <summary>
        /// Сверяет наложенную камеру с основной. Вызывать раз в кадр.
        /// </summary>
        /// <remarks>
        /// Проверок разъезда много и они подробные не от избытка
        /// осторожности: любая из них, оставшись без внимания, даёт не
        /// исключение, а тихо неверный кадр — интерфейс под постпроцессом,
        /// двойное отсечение или пропавший слой.
        /// </remarks>
        public void Sync(Camera mainCamera, Volume volume)
        {
            bool separationIsBroken =
                _configuredMainCamera != mainCamera ||
                _configuredMainCameraData == null ||
                _worldUICamera == null ||
                _worldUICameraData == null ||
                (mainCamera.cullingMask & _worldUILayerMask) != 0 ||
                !_worldUICamera.enabled ||
                _worldUICamera.cullingMask != _worldUILayerMask ||
                _worldUICameraData.renderType != CameraRenderType.Overlay ||
                _worldUICameraData.renderPostProcessing ||
                !_configuredMainCameraData.cameraStack.Contains(_worldUICamera);

            if (separationIsBroken)
            {
                EnsureCameraSetup(mainCamera, volume);
            }

            if (_worldUICamera == null)
            {
                return;
            }

            _worldUICamera.worldToCameraMatrix = mainCamera.worldToCameraMatrix;
            Matrix4x4 projection = mainCamera.projectionMatrix;
            bool projectionChanged =
                !_hasProjection ||
                _worldUICamera.orthographic != mainCamera.orthographic ||
                !Mathf.Approximately(_lastOrthographicSize, mainCamera.orthographicSize) ||
                !Mathf.Approximately(_lastFieldOfView, mainCamera.fieldOfView) ||
                !Mathf.Approximately(_lastNearClipPlane, mainCamera.nearClipPlane) ||
                !Mathf.Approximately(_lastFarClipPlane, mainCamera.farClipPlane) ||
                _lastProjection != projection;
            if (!projectionChanged)
            {
                return;
            }

            _worldUICamera.orthographic = mainCamera.orthographic;
            _worldUICamera.orthographicSize = mainCamera.orthographicSize;
            _worldUICamera.fieldOfView = mainCamera.fieldOfView;
            _worldUICamera.nearClipPlane = mainCamera.nearClipPlane;
            _worldUICamera.farClipPlane = mainCamera.farClipPlane;
            _worldUICamera.projectionMatrix = projection;
            _lastOrthographicSize = mainCamera.orthographicSize;
            _lastFieldOfView = mainCamera.fieldOfView;
            _lastNearClipPlane = mainCamera.nearClipPlane;
            _lastFarClipPlane = mainCamera.farClipPlane;
            _lastProjection = projection;
            _hasProjection = true;
        }

        public void EnsureCameraSetup(Camera mainCamera, Volume volume)
        {
            UniversalAdditionalCameraData cameraData;
            if (mainCamera == _configuredMainCamera && _cachedMainCameraData != null)
            {
                cameraData = _cachedMainCameraData;
            }
            else
            {
                cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>()
                    ?? mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                _cachedMainCameraData = cameraData;
            }

            DisplayManager.HDROutput.ConfigureCamera(mainCamera);
            cameraData.volumeLayerMask = 1 << volume.gameObject.layer;
            cameraData.volumeTrigger = mainCamera.transform;

            _configuredMainCamera = mainCamera;
            _configuredMainCameraData = cameraData;

            int uiLayer = UnityRenderLayerContracts.RequireWorldUIGameObjectLayer();
            UnityRenderLayerContracts.RequireWorldUISortingLayer();
            _worldUILayerMask = 1 << uiLayer;
            (_worldUICamera, _worldUICameraData) = UnityRenderLayerContracts.EnsureWorldUIOverlayCamera(
                mainCamera, cameraData, _sceneObjects, _worldUILayerMask, _worldUICamera);
        }

        /// <summary>
        /// Возвращает основной камере слой интерфейса при выключении.
        /// </summary>
        public void ReleaseWorldUILayer()
        {
            if (_configuredMainCamera != null)
            {
                _configuredMainCamera.cullingMask |= _worldUILayerMask;
            }
        }

        public void DisableOverlay()
        {
            if (_worldUICamera != null)
            {
                _worldUICamera.enabled = false;
            }
        }
    }
}
