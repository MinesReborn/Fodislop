#nullable enable

using System;
using Kern.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kern.Editor;

public sealed class UnityRenderLayerValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => -900;

    public void OnPreprocessBuild(BuildReport report)
    {
        Validate();
    }

    [MenuItem("Kern/Diagnostics/Validate Unity Render Layers")]
    public static void Validate()
    {
        int gameObjectLayer = LayerMask.NameToLayer(ProjectRuntimeContracts.RequiredLayers.WorldUI);
        if (gameObjectLayer < 0)
        {
            throw new BuildFailedException(
                $"Required GameObject layer '{ProjectRuntimeContracts.RequiredLayers.WorldUI}' is missing.");
        }

        int sortingLayer = UnityEngine.SortingLayer.NameToID(
            ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer);
        if (sortingLayer == 0 && !string.Equals(
                UnityEngine.SortingLayer.IDToName(sortingLayer),
                ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer,
                StringComparison.Ordinal))
        {
            throw new BuildFailedException(
                $"Required Sorting Layer '{ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer}' is missing.");
        }

        UnityEngine.Debug.Log(
            $"[RenderLayers] World UI: GameObject layer={gameObjectLayer} " +
            $"('{ProjectRuntimeContracts.RequiredLayers.WorldUI}'), " +
            $"Sorting Layer ID={sortingLayer} " +
            $"('{ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer}').");
    }
}
