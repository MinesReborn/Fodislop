#nullable enable

using Kern.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kern.Editor;

[InitializeOnLoad]
public static class PlayModeSceneBootstrapper
{
    public static readonly string BootstrapScenePath =
        BuildSceneOrder.ScenePath(ProjectRuntimeContracts.SceneNames.Bootstrap);

    static PlayModeSceneBootstrapper()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EnsurePlayModeStartScene();
    }

    public static void EnsurePlayModeStartScene()
    {
        SceneAsset? bootstrapAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
        if (bootstrapAsset != null)
        {
            if (EditorSceneManager.playModeStartScene != bootstrapAsset)
            {
                EditorSceneManager.playModeStartScene = bootstrapAsset;
                Debug.Log($"[PlayModeSceneBootstrapper] Play mode start scene configured to '{BootstrapScenePath}'.");
            }
        }
        else
        {
            Debug.LogWarning($"[PlayModeSceneBootstrapper] Bootstrap scene not found at '{BootstrapScenePath}'.");
        }
    }

    private const string TestRunnerScenePrefix = "InitTestScene";

    private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
    {
        if (stateChange == PlayModeStateChange.ExitingEditMode)
        {
            // Тест-раннер входит в Play Mode со своей служебной сценой и ждёт
            // именно её. Подмена на Bootstrap оставляла раннер без сцены, и
            // PlayMode-тесты висели до таймаута. Тесты поднимают Bootstrap сами.
            if (EditorSceneManager.GetActiveScene().name.StartsWith(TestRunnerScenePrefix, System.StringComparison.Ordinal))
            {
                EditorSceneManager.playModeStartScene = null;
                SessionState.SetString(ProjectRuntimeContracts.EditorSession.PlayModeTargetScene, string.Empty);
                return;
            }

            EnsurePlayModeStartScene();
            CaptureSelectedTargetScene();
        }
        else if (stateChange == PlayModeStateChange.EnteredEditMode)
        {
            EnsurePlayModeStartScene();
        }
    }

    private static void CaptureSelectedTargetScene()
    {
        string? targetScene = null;
        if (Selection.activeObject is SceneAsset selectedSceneAsset)
        {
            targetScene = selectedSceneAsset.name;
        }
        else
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                targetScene = activeScene.name;
            }
        }

        if (!string.IsNullOrEmpty(targetScene) &&
            targetScene != ProjectRuntimeContracts.SceneNames.Bootstrap)
        {
            SessionState.SetString(ProjectRuntimeContracts.EditorSession.PlayModeTargetScene, targetScene);
            Debug.Log($"[PlayModeSceneBootstrapper] Play mode requested with scene '{targetScene}' active/selected; launching Bootstrap first.");
        }
        else
        {
            SessionState.SetString(ProjectRuntimeContracts.EditorSession.PlayModeTargetScene, string.Empty);
        }
    }
}
