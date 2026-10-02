using System;
using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    public class UniversalLightData : ContextItem
    {
        public int mainLightIndex;

        public int additionalLightsCount;

        public int maxPerObjectAdditionalLightsCount;

        public NativeArray<VisibleLight> visibleLights;

        public bool shadeAdditionalLightsPerVertex;

        public bool supportsMixedLighting;

        public bool reflectionProbeBoxProjection;

        public bool reflectionProbeBlending;

        public bool reflectionProbeAtlas;

        public bool supportsLightLayers;

        public bool supportsAdditionalLights;

        /// <inheritdoc/>
        public override void Reset()
        {
            mainLightIndex = -1;
            additionalLightsCount = 0;
            maxPerObjectAdditionalLightsCount = 0;
            visibleLights = default;
            shadeAdditionalLightsPerVertex = false;
            supportsMixedLighting = false;
            reflectionProbeBoxProjection = false;
            reflectionProbeBlending = false;
            supportsLightLayers = false;
            supportsAdditionalLights = false;
        }
    }
}
