#nullable enable

using System;
using Kern.Core;
using UnityEngine;

namespace Kern.Rendering;

public static class GraphicsQualityProfileLoader
{
    public static GraphicsQualityProfile LoadRequired()
    {
        GraphicsQualityProfile profile =
            Resources.Load<GraphicsQualityProfile>(
                ProjectRuntimeContracts.ResourcePaths.GraphicsQualityProfile) ??
            throw new InvalidOperationException(
                $"Required graphics profile Resources/" +
                $"{ProjectRuntimeContracts.ResourcePaths.GraphicsQualityProfile}.asset is missing.");
        profile.Validate();
        return profile;
    }
}
