#nullable enable

using System;
using System.IO;
using Kern.Core;
using UnityEditor;

namespace Kern.Editor;

// Порядок production-сцен в Build Settings: Bootstrap первой, дальше Gateway,
// MainMenu, MainGame. Проверяется перед сборкой; правится руками в Build
// Settings, а не молча чинится кодом.
public static class BuildSceneOrder
{
    private static readonly string[] _RequiredScenePaths =
    [
        ScenePath(ProjectRuntimeContracts.SceneNames.Bootstrap),
        ScenePath(ProjectRuntimeContracts.SceneNames.Gateway),
        ScenePath(ProjectRuntimeContracts.SceneNames.MainMenu),
        ScenePath(ProjectRuntimeContracts.SceneNames.MainGame),
    ];

    public static string ScenePath(string sceneName) => $"Assets/Scenes/{sceneName}.unity";

    public static void Validate()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Length < _RequiredScenePaths.Length)
        {
            throw new InvalidOperationException(
                $"Build Settings contain {scenes.Length} scene(s); " +
                $"at least {_RequiredScenePaths.Length} production scenes are required.");
        }

        for (int index = 0; index < _RequiredScenePaths.Length; index++)
        {
            string requiredPath = _RequiredScenePaths[index];
            if (!File.Exists(requiredPath))
            {
                throw new FileNotFoundException("Required production scene is missing.", requiredPath);
            }

            EditorBuildSettingsScene scene = scenes[index];
            if (scene == null || !scene.enabled || scene.path != requiredPath)
            {
                string actual = scene == null
                    ? "<null>"
                    : $"{scene.path} (enabled={scene.enabled})";
                throw new InvalidOperationException(
                    $"Build Settings scene {index} must be '{requiredPath}' and enabled; actual: {actual}.");
            }
        }
    }
}
