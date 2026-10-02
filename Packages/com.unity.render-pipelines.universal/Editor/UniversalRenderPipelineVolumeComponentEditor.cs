using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using StringBuilder = System.Text.StringBuilder;

namespace UnityEditor.Rendering.Universal
{
    /// <seealso cref="VolumeComponentEditor"/>
    /// <seealso cref="UniversalRenderPipelineAsset"/>
    /// <seealso cref="GraphicsSettings"/>
    /// <seealso cref="CoreEditorUtils"/>
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [CustomEditor(typeof(VolumeComponent), true)]
    public class UniversalRenderPipelineVolumeComponentEditor : VolumeComponentEditor
    {
        private VolumeRequiresRendererFeatures m_FeatureAttribute;

        /// <example>
        /// The <see cref="OnEnable"/> method is automatically called when the editor is initialized, making it suitable
        /// for setting up references and caching.
        /// </example>
        public override void OnEnable()
        {
            base.OnEnable();

            // Caching the attribute as UI code can be called multiple times in the same editor frame
            if (m_FeatureAttribute == null)
                m_FeatureAttribute = target.GetType().GetCustomAttribute<VolumeRequiresRendererFeatures>();
        }

        /// <param name="types">A set of <see cref="Type"/> objects representing the missing renderer features.</param>
        /// <returns>A formatted string containing the names of the missing feature types.</returns>
        private string GetFeatureTypeNames(in HashSet<Type> types)
        {
            var typeNameString = new StringBuilder();

            foreach (var type in types)
                typeNameString.AppendFormat("\"{0}\" ", type.Name);

            return typeNameString.ToString();
        }

        /// <example>
        /// The <see cref="OnBeforeInspectorGUI"/> method is called before rendering the inspector GUI for the volume component,
        /// ensuring that feature checks are performed and UI warnings are shown before displaying the inspector.
        /// </example>
        protected override void OnBeforeInspectorGUI()
        {
            if (m_FeatureAttribute == null)
                return;

            var rendererFeatures = (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset && urpAsset.scriptableRendererData != null)
                ? urpAsset.scriptableRendererData.rendererFeatures
                : null;

            using (HashSetPool<Type>.Get(out var missingFeatureTypes))
            {
                foreach (var elem in m_FeatureAttribute.TargetFeatureTypes)
                    missingFeatureTypes.Add(elem);

                if (rendererFeatures != null)
                {
                    foreach (var feature in rendererFeatures)
                    {
                        var featureType = feature.GetType();
                        if (missingFeatureTypes.Contains(featureType))
                        {
                            missingFeatureTypes.Remove(featureType);
                            if (missingFeatureTypes.Count == 0)
                                break;
                        }
                    }
                }

                if (missingFeatureTypes.Count > 0)
                {
                    CoreEditorUtils.DrawFixMeBox(
                        $"For this effect to work, the following renderer feature(s) need to be added and enabled on the active renderer asset: {GetFeatureTypeNames(in missingFeatureTypes)}",
                        MessageType.Warning,
                        "Open",
                        () =>
                        {
                            Selection.activeObject = UniversalRenderPipeline.asset.scriptableRendererData;
                            GUIUtility.ExitGUI();
                        });
                }
            }
        }
    }
}
