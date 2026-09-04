#nullable enable

using System;
using UnityEngine.SceneManagement;

namespace Fodinae.Core;

internal static class SceneTransitionSceneLookup
{
    public static Scene FindFirstLoaded(string sceneName)
    {
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded && string.Equals(scene.name, sceneName, StringComparison.Ordinal))
            {
                return scene;
            }
        }

        return default;
    }

    public static Scene FindUniqueLoaded(string sceneName)
    {
        Scene result = default;
        int count = 0;
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded && string.Equals(scene.name, sceneName, StringComparison.Ordinal))
            {
                result = scene;
                count++;
            }
        }

        if (count > 1)
        {
            throw new InvalidOperationException(
                $"[Bootstrap] Transition target '{sceneName}' is loaded {count} times. " +
                "Unload duplicate scene instances before continuing.");
        }

        return result;
    }
}
