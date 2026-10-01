using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>Renderer-feature contract for an explicitly sized orthographic world target.</summary>
    public interface IRenderer2DWorldGridProvider
    {
        bool TryGetWorldGrid(Camera camera, out Renderer2DWorldGridLayout layout);
    }

    /// <summary>Frame-local dimensions, matrices and bottom-left UV crop on one world grid.</summary>
    public struct Renderer2DWorldGridLayout
    {
        public int Width;
        public int Height;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Vector4 ViewportToWorldUv;
        public Vector4 WorldRect;

        internal void Validate()
        {
            if (Width <= 0 || Height <= 0 || Width > SystemInfo.maxTextureSize || Height > SystemInfo.maxTextureSize ||
                !float.IsFinite(WorldRect.x) || !float.IsFinite(WorldRect.y) ||
                !float.IsFinite(WorldRect.z) || !float.IsFinite(WorldRect.w) ||
                WorldRect.z <= 0 || WorldRect.w <= 0 ||
                Mathf.Abs(WorldRect.z * 32f - Width) > 0.001f ||
                Mathf.Abs(WorldRect.w * 32f - Height) > 0.001f)
            {
                throw new InvalidOperationException("World grid coverage exceeds the supported target dimensions.");
            }
        }
    }

    /// <summary>Renderer-owned frame context for presentation after world postprocessing.</summary>
    public sealed class Renderer2DWorldGridData : ContextItem
    {
        public bool Active;
        public Renderer2DWorldGridLayout Layout;
        public RenderTextureDescriptor OutputDescriptor;

        public override void Reset()
        {
            Active = false;
            Layout = default;
            OutputDescriptor = default;
        }
    }
}
