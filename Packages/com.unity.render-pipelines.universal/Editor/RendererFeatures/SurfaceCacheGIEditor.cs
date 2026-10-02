#if SURFACE_CACHE

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(SurfaceCacheGIRendererFeature))]
    internal class SurfaceCacheGIEditor : Editor
    {
        private bool m_IsInitialized;

        // Debug parameters
        private SerializedProperty _debugEnabled;
        private SerializedProperty _debugViewMode;
        private SerializedProperty _debugShowSamplePosition;

        private struct TextContent
        {
            public static GUIContent DebugEnabled = EditorGUIUtility.TrTextContent("Debug Enabled", "Enable debug visualization.");
            public static GUIContent DebugViewMode = EditorGUIUtility.TrTextContent("Debug View Mode", "Debug visualization mode.");
            public static GUIContent DebugShowSamplePosition = EditorGUIUtility.TrTextContent("Debug Show Sample Position", "Show sample positions in debug view.");
        }

        private void Init()
        {
            m_IsInitialized = true;

            SerializedProperty paramSets = serializedObject.FindProperty("_parameterSet");

            _debugEnabled = paramSets.FindPropertyRelative("DebugEnabled");
            _debugViewMode = paramSets.FindPropertyRelative("DebugViewMode");
            _debugShowSamplePosition = paramSets.FindPropertyRelative("DebugShowSamplePosition");
        }

        private static bool SceneHasSurfaceCacheGIVolume()
        {
            Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude);
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i].sharedProfile != null && volumes[i].sharedProfile.Has<SurfaceCacheGIVolumeOverride>())
                    return true;
            }
            return false;
        }

        public override void OnInspectorGUI()
        {
            if (!m_IsInitialized)
                Init();

            if (PlayerSettings.GetStaticBatchingForPlatform(EditorUserBuildSettings.activeBuildTarget))
            {
                var surfaceCacheFeature = (SurfaceCacheGIRendererFeature)target;

                EditorGUILayout.HelpBox(SurfaceCacheGIRendererFeature.k_StaticBatchingErrorMesssage, surfaceCacheFeature.isActive ? MessageType.Error : MessageType.Warning);
            }
            else if (SceneView.lastActiveSceneView && !SceneView.lastActiveSceneView.sceneViewState.alwaysRefreshEnabled)
            {
                EditorGUILayout.HelpBox("Enable \"Always Refresh\" in the Scene View to see realtime updates in the Scene View.", MessageType.Info);
            }

            // Info box explaining volume-based control — only shown when no volume in the scene has the override
            if (!SceneHasSurfaceCacheGIVolume())
            {
                EditorGUILayout.HelpBox("Many Surface Cache settings are controlled via the Volume system. Add a 'Surface Cache Global Illumination' volume override to your scene to adjust these settings per-scene.", MessageType.Info);
            }

            EditorGUILayout.Space();

            // Debug settings
            EditorGUILayout.LabelField("Debugging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_debugEnabled, TextContent.DebugEnabled);
            EditorGUILayout.PropertyField(_debugViewMode, TextContent.DebugViewMode);
            EditorGUILayout.PropertyField(_debugShowSamplePosition, TextContent.DebugShowSamplePosition);

            serializedObject.ApplyModifiedProperties();
        }
    }
}

#endif
