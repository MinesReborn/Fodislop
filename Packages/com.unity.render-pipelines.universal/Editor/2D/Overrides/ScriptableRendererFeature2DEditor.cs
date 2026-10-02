using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(ScriptableRendererFeature2D), true)]
    public class ScriptableRendererFeature2DEditor : Editor
    {
        private SerializedProperty m_InjectionPointProperty;
        private SerializedProperty m_SortingLayerProperty;

        private static readonly GUIContent k_InjectionPointGuiContent = new GUIContent("Injection Point", "Specifies where in the frame this pass will be injected.");

        private void OnEnable()
        {
            m_InjectionPointProperty = serializedObject.FindProperty("injectionPoint2D");
            m_SortingLayerProperty = serializedObject.FindProperty("sortingLayerID");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.PropertyField(m_InjectionPointProperty, k_InjectionPointGuiContent);

            if (ScriptableRenderPass2D.IsSortingLayerEvent((RenderPassEvent2D)m_InjectionPointProperty.intValue))
                SortingLayerEditorUtility.RenderSortingLayerFields(m_SortingLayerProperty);
        }
    }
}
