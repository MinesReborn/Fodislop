#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Explicit Resource Registry for the lighting system.
/// Resources are grouped by domain so ownership, lifetime, and access
/// are immediately clear.
/// </summary>
public sealed class LightingResources
{
    public sealed class GeometryResources
    {
        public RenderTexture? Material { get; internal set; }
        public RenderTexture? StaticEmission { get; internal set; }
        public RenderTexture? CellSolidMask { get; internal set; }
        public bool CachesValid { get; internal set; }
        public int CellGridWidth { get; internal set; }
        public int CellGridHeight { get; internal set; }
    }

    public sealed class CascadeResources
    {
        public ComputeBuffer? Atlas { get; internal set; }
        public List<CascadeLayout> Layouts { get; } = new();
        public int AtlasCapacity { get; internal set; }
        public int AtlasEntryCount { get; internal set; }
    }

    public sealed class DirectResources
    {
        public RenderTexture? Static { get; internal set; }
        public RenderTexture? Dynamic { get; internal set; }
        public ComputeBuffer? DynamicLightsBuffer { get; internal set; }
    }

    public sealed class BounceResources
    {
        public RenderTexture? Texture { get; internal set; }
        public ComputeBuffer? Taps { get; internal set; }
        public ComputeBuffer? FilterWeights { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
    }

    public sealed class OutputResources
    {
        public RenderTexture? Lightmap { get; internal set; }
    }

    public GeometryResources Geometry { get; } = new();
    public CascadeResources Cascade { get; } = new();
    public DirectResources Direct { get; } = new();
    public BounceResources Bounce { get; } = new();
    public OutputResources Output { get; } = new();

    public ComputeShader? Compute { get; internal set; }
    public CommandBuffer? CommandBuffer { get; internal set; }

    public int FieldWidth { get; internal set; }
    public int FieldHeight { get; internal set; }

    /// <summary>
    /// Clears registry references after the resource manager has released the
    /// Unity objects. The registry is a view of ownership held elsewhere; it
    /// must never release or destroy those objects itself.
    /// </summary>
    internal void ClearReferences()
    {
        Geometry.Material = null;
        Geometry.StaticEmission = null;
        Geometry.CellSolidMask = null;
        Geometry.CachesValid = false;
        Geometry.CellGridWidth = 0;
        Geometry.CellGridHeight = 0;

        Cascade.Atlas = null;
        Cascade.Layouts.Clear();
        Cascade.AtlasCapacity = 0;
        Cascade.AtlasEntryCount = 0;

        Direct.Static = null;
        Direct.Dynamic = null;
        Direct.DynamicLightsBuffer = null;

        Bounce.Texture = null;
        Bounce.Taps = null;
        Bounce.FilterWeights = null;
        Bounce.Width = 0;
        Bounce.Height = 0;

        Output.Lightmap = null;
        Compute = null;
        CommandBuffer = null;
        FieldWidth = 0;
        FieldHeight = 0;
    }
}
