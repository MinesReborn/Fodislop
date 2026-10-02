using System;

namespace UnityEngine.Rendering.Universal
{
    public class UniversalPostProcessingData : ContextItem
    {
        public bool isEnabled;

        /// <seealso cref="ColorGradingMode"/>
        public ColorGradingMode gradingMode;

        public int lutSize;

        public bool useFastSRGBLinearConversion;

        public bool supportScreenSpaceLensFlare;

        public bool supportDataDrivenLensFlare;

#if ENABLE_UPSCALER_FRAMEWORK
        internal IUpscaler activeUpscaler;
#endif

        public override void Reset()
        {
            isEnabled = default;
            gradingMode = ColorGradingMode.LowDynamicRange;
            lutSize = 0;
            useFastSRGBLinearConversion = false;
            supportScreenSpaceLensFlare = false;
            supportDataDrivenLensFlare = false;

#if ENABLE_UPSCALER_FRAMEWORK
            activeUpscaler = null;
#endif
        }
    }
}
