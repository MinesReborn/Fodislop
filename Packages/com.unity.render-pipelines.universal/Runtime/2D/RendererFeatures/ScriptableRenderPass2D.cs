using System;

namespace UnityEngine.Rendering.Universal
{
    public enum RenderPassEvent2D
    {
        BeforeRendering = 0,

        BeforeRenderingNormals = 100,

        AfterRenderingNormals = 200,

        BeforeRenderingShadows = 300,

        AfterRenderingShadows = 400,

        BeforeRenderingLights = 500,

        AfterRenderingLights = 600,

        BeforeRenderingSprites = 700,

        AfterRenderingSprites = 800,
        BeforeRenderingPostProcessing = 900,

        AfterRenderingPostProcessing = 1000,

        AfterRendering = 1100,
    }

    internal static class RenderPassEvents2DEnumValues
    {
        // We cache the values in this array at construction time to avoid runtime allocations, which we would cause if we accessed valuesInternal directly.
        public static int[] values;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod()]
        static void ResetStaticsOnLoad()
        {
        }
#endif

        static RenderPassEvents2DEnumValues()
        {
            Array valuesInternal = Enum.GetValues(typeof(RenderPassEvent2D));

            values = new int[valuesInternal.Length];

            int index = 0;
            foreach (int value in valuesInternal)
            {
                values[index] = value;
                index++;
            }
        }
    }

    public abstract class ScriptableRenderPass2D : ScriptableRenderPass
    {
        public RenderPassEvent2D renderPassEvent2D { get; set; }

        public int renderPassSortingLayerID { get; set; }

        static internal int GetRenderPassEventRange(RenderPassEvent2D renderPassEvent2D)
        {
            int numEvents = RenderPassEvents2DEnumValues.values.Length;
            int currentIndex = 0;

            // Find the index of the renderPassEvent in the values array.
            for (int i = 0; i < numEvents; ++i)
            {
                if (RenderPassEvents2DEnumValues.values[currentIndex] == (int)renderPassEvent2D)
                    break;

                currentIndex++;
            }

            if (currentIndex >= numEvents)
            {
                Debug.LogError("GetRenderPassEventRange: invalid renderPassEvent2D value cannot be found in the RenderPassEvent2D enumeration");
                return 0;
            }

            if (currentIndex + 1 >= numEvents)
                return 50; // If this was the enum's last event, add 50 as the range.

            int nextValue = RenderPassEvents2DEnumValues.values[currentIndex + 1];

            return nextValue - (int)renderPassEvent2D;
        }

        static internal bool IsSortingLayerEvent(RenderPassEvent2D renderPassEvent)
        {
            return renderPassEvent >= RenderPassEvent2D.BeforeRenderingNormals && renderPassEvent <= RenderPassEvent2D.AfterRenderingSprites;
        }
    }

    static internal class ScriptableRenderPass2DExtension
    {
        static internal void GetInjectionPoint2D(this ScriptableRenderPass renderPass, out RenderPassEvent2D rpEvent, out int rpLayer)
        {
            ScriptableRenderPass2D renderPass2D = renderPass as ScriptableRenderPass2D;

            if (renderPass2D == null)
            {
                rpLayer = int.MinValue;

                if (renderPass.renderPassEvent <= RenderPassEvent.BeforeRenderingTransparents)
                    rpEvent = RenderPassEvent2D.BeforeRendering;
                else if (renderPass.renderPassEvent <= RenderPassEvent.BeforeRenderingPostProcessing)
                    rpEvent = RenderPassEvent2D.BeforeRenderingPostProcessing;
                else if (renderPass.renderPassEvent <= RenderPassEvent.AfterRenderingPostProcessing)
                    rpEvent = RenderPassEvent2D.AfterRenderingPostProcessing;
                else
                    rpEvent = RenderPassEvent2D.AfterRendering;
            }
            else
            {
                rpEvent = renderPass2D.renderPassEvent2D;
                rpLayer = renderPass2D.renderPassSortingLayerID;
            }
        }
    }
}
