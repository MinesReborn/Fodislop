#nullable enable

using System;
using Fodinae.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Core;

public static class UnityRenderLayerContracts
{
    public static int RequireWorldUIGameObjectLayer()
    {
        int layer = LayerMask.NameToLayer(ProjectRuntimeContracts.RequiredLayers.WorldUI);
        if (layer < 0)
        {
            throw new InvalidOperationException(
                $"Required Unity GameObject layer '{ProjectRuntimeContracts.RequiredLayers.WorldUI}' is missing.");
        }

        return layer;
    }

    public static int RequireWorldUISortingLayer()
    {
        int layerId = SortingLayer.NameToID(ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer);
        if (layerId == 0 && !string.Equals(
                SortingLayer.IDToName(layerId),
                ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Required Unity Sorting Layer '{ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer}' is missing.");
        }

        return layerId;
    }

    public static void ApplyWorldUI(Renderer renderer, int sortingOrder)
    {
        renderer.gameObject.layer = RequireWorldUIGameObjectLayer();
        renderer.sortingLayerName = ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer;
        renderer.sortingOrder = sortingOrder;
    }

    public static (
        Camera Camera,
        UniversalAdditionalCameraData CameraData) EnsureWorldUIOverlayCamera(
        Camera mainCamera,
        UniversalAdditionalCameraData mainCameraData,
        ISceneObjectFactory sceneObjects,
        int worldUILayerMask,
        Camera? existingCamera)
    {
        mainCamera.cullingMask &= ~worldUILayerMask;
        Camera? worldUICamera = existingCamera;
        if (worldUICamera == null)
        {
            GameObject cameraObject = sceneObjects.Create("WorldUICamera");
            worldUICamera = cameraObject.AddComponent<Camera>();
            worldUICamera.CopyFrom(mainCamera);
        }

        worldUICamera.worldToCameraMatrix = mainCamera.worldToCameraMatrix;
        worldUICamera.cullingMask = worldUILayerMask;
        worldUICamera.clearFlags = CameraClearFlags.Nothing;
        worldUICamera.depth = mainCamera.depth + 1f;
        worldUICamera.enabled = true;

        UniversalAdditionalCameraData worldUICameraData =
            worldUICamera.GetComponent<UniversalAdditionalCameraData>() ??
            worldUICamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        worldUICameraData.renderType = CameraRenderType.Overlay;
        worldUICameraData.renderPostProcessing = false;
        worldUICameraData.allowHDROutput = false;
        if (!mainCameraData.cameraStack.Contains(worldUICamera))
        {
            mainCameraData.cameraStack.Add(worldUICamera);
        }

        return (worldUICamera, worldUICameraData);
    }
}
