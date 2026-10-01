using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    public abstract class ShadowShape2D
    {
        public enum OutlineTopology
        {
            Lines,
            Triangles
        }

        public enum WindingOrder
        {
            Clockwise,
            CounterClockwise
        }

        /// <param name="flipX"> Specifies flipping on the local x-axis </param>
        /// <param name="flipY"> Specifies flipping on the local y-axis </param>
        public abstract void SetFlip(bool flipX, bool flipY);

        /// <param name="flipX"> Returns flipping on the local x-axis </param>
        /// <param name="flipY"> Returns flipping on the local y-axis </param>
        public abstract void GetFlip(out bool flipX, out bool flipY);

        /// <param name="trim"> Specifies the starting trim value.</param>
        public abstract void SetDefaultTrim(float trim);

        /// <param name="vertices">The vertices used to create the shadow geometry.</param>
        /// <param name="indices">The indices used to create the shadow geometry (Lines topology) </param>
        /// <param name="radii">The radius at the vertex. Can be used to describe a capsule.</param>
        /// <param name="outputTransform"> The transform applied after creating the shadow geometry.</param>
        /// <param name="windingOrder">The winding order of the supplied geometry.</param>
        /// <param name="allowContraction">Specifies if the ShadowCaster2D is allowed to contract the supplied shape(s).</param>
        /// <param name="createInteriorGeometry">Specifies if the ShadowCaster2D should create interior geometry. Required for shadow casters that do not use renderers as their source.</param>
        public abstract void SetShape(NativeArray<Vector3> vertices, NativeArray<int> indices, NativeArray<float> radii, Matrix4x4 outputTransform, WindingOrder windingOrder = WindingOrder.Clockwise, bool allowContraction = true, bool createInteriorGeometry = false);


        /// <param name="vertices">The vertices used to create the shadow geometry.</param>
        /// <param name="indices">The indices used to create the shadow geometry (Lines topology) </param>
        /// <param name="radii">The radius at the vertex. Can be used to describe a capsule.</param>
        /// <param name="windingOrder">The winding order of the supplied geometry.</param>
        /// <param name="allowContraction">Specifies if the ShadowCaster2D is allowed to contract the supplied shape(s).</param>
        /// <param name="createInteriorGeometry">Specifies if the ShadowCaster2D should create interior geometry. Required for shadow casters that do not use renderers as their source.</param>
        /// <param name="inWorldSpace"> Specifies if the ShadowCaster2D has its vertices in world space </param>
        public abstract void SetShape(NativeArray<Vector3> vertices, NativeArray<int> indices, NativeArray<float> radii, WindingOrder windingOrder = WindingOrder.Clockwise, bool allowContraction = true, bool createInteriorGeometry = false, bool inWorldSpace = false);

        /// <param name="vertices">The vertices used to create the shadow geometry.</param>
        /// <param name="indices">The indices used to create the shadow geometry (Lines topology) </param>
        /// <param name="outlineTopology"> The topology of the input geometry.</param>
        /// <param name="windingOrder">The winding order of the supplied geometry.</param>
        /// <param name="allowContraction">Specifies if the ShadowCaster2D is allowed to contract the supplied shape(s).</param>
        /// <param name="createInteriorGeometry">Specifies if the ShadowCaster2D should create interior geometry. Required for shadow casters that do not use renderers as their source.</param>
        /// <param name="inWorldSpace"> Specifies if the ShadowCaster2D has its vertices in world space </param>
        public abstract void SetShape(NativeArray<Vector3> vertices, NativeArray<int> indices, OutlineTopology outlineTopology, WindingOrder windingOrder = WindingOrder.Clockwise, bool allowContraction = true, bool createInteriorGeometry = false, bool inWorldSpace = false);


        /// <param name="vertices">The vertices used to create the shadow geometry.</param>
        /// <param name="indices">The indices used to create the shadow geometry (Lines topology) </param>
        /// <param name="inWorldSpace"> Specifies if the ShadowCaster2D has its vertices in world space </param>
        public abstract void SetShapeDirect(NativeArray<Vector3> vertices, NativeArray<int> indices, bool inWorldSpace = false);
    }
}
