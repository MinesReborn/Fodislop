#nullable enable

#if UNITY_EDITOR
using Fodinae.World.Lighting;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    [CustomEditor(typeof(LightingEngine))]
    public sealed class LightingEngineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            DrawActualShaderUniforms((LightingEngine)target);
        }

        private static void DrawActualShaderUniforms(LightingEngine engine)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actual WorldLighting.compute uniforms", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These are the values currently sent to the compute shader. Derived values are read-only.",
                MessageType.Info);
            EditorGUILayout.Vector2IntField("_FieldSize", new(engine.FieldWidth, engine.FieldHeight));
            EditorGUILayout.Vector2IntField("_BounceSize", new(engine.BounceWidth, engine.BounceHeight));
            EditorGUILayout.FloatField("Requested pixels/cell", engine.RequestedPixelsPerCell);
            EditorGUILayout.FloatField("Effective pixels/cell", engine.EffectivePixelsPerCell);
            EditorGUILayout.Toggle("Texture dimension limited", engine.TextureDimensionLimited);
            EditorGUILayout.Toggle("Cascade budget limited", engine.CascadeBudgetLimited);
            EditorGUILayout.Vector4Field("_WorldRect", engine.WorldRect);
            EditorGUILayout.ColorField(new GUIContent("_AmbientColor"), engine.ComputeAmbientColor, true, true, true);
            EditorGUILayout.ColorField(new GUIContent("_EmptyExtinctionRgb"), engine.ComputeEmptyExtinction, true, true, true);
            EditorGUILayout.ColorField(new GUIContent("_SolidExtinctionRgb"), engine.ComputeSolidExtinction, true, true, true);
            EditorGUILayout.FloatField("_MinimumTransmission", engine.MinimumTransmission);
            EditorGUILayout.FloatField("_BounceStrength", engine.BounceStrength);
            EditorGUILayout.FloatField("_EmissionScale", engine.EmissionScale);
            EditorGUILayout.FloatField("_MaximumLightMultiplier", engine.MaximumLightMultiplier);
            EditorGUILayout.FloatField("_CellSize", engine.CellSize);
            EditorGUILayout.FloatField("_TransmittanceDebugDistanceCells", engine.TransmittanceDebugDistanceCells);
            EditorGUILayout.EnumPopup("_DebugView", engine.ActiveDebugView);
            EditorGUILayout.IntField("_MaterialYFlip", engine.MaterialYFlip);
            EditorGUILayout.IntField("_MaximumIntervalSteps", engine.MaximumIntervalSteps);
            EditorGUILayout.IntField("_EnableDiffuseBounce", engine.DiffuseBounceEnabled ? 1 : 0);
            EditorGUILayout.IntField("Cascade count", engine.CascadeCount);
            foreach (string summary in engine.GetCascadeUniformSummaries())
            {
                EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
            }
        }
    }
}
#endif
