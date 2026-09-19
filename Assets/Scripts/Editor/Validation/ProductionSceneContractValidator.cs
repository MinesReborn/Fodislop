#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Lifecycle;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Kern.Editor;

public sealed class ProductionSceneContractValidator : IPreprocessBuildWithReport
{
    private const string ServicesInactiveMessage =
        "MainGame Services root must be authored inactive: Awake/OnEnable of managers must run only after dependency injection (GameLifetimeScope.ActivateSceneServices).";

    public int callbackOrder => 0;

    private static readonly string[] _ServiceGroups = { "World", "Rendering", "UI", "Audio" };

    [MenuItem("Kern/Architecture/Validate Production Scene Contracts")]
    public static void ValidateFromMenu()
    {
        List<string> errors = ValidateBuildScenes(out int sceneCount);
        if (errors.Count == 0)
        {
            Debug.Log($"[SceneContract] All {sceneCount} build scenes satisfy the production scene contract.");
            return;
        }

        foreach (string error in errors)
        {
            Debug.LogError($"[SceneContract] {error}");
        }

        EditorUtility.DisplayDialog(
            "Scene contract validation failed",
            $"{errors.Count} violation(s) found. See the Console for details.",
            "OK");
    }

    void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
    {
        List<string> errors = ValidateBuildScenes(out _);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "[SceneContract] Build aborted: the build scenes violate the production scene contract:\n- " +
                string.Join("\n- ", errors));
        }
    }

    private static List<string> ValidateBuildScenes(out int sceneCount)
    {
        string[] scenePaths = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        sceneCount = scenePaths.Length;

        List<string> errors = new();
        if (scenePaths.Length == 0)
        {
            errors.Add("No enabled scenes in EditorBuildSettings.");
            return errors;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string scenePath in scenePaths)
            {
                ValidateScene(EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single), errors);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        return errors;
    }

    public static void ValidateAllLoadedScenes(List<string> errors)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded)
            {
                ValidateScene(scene, errors);
            }
        }
    }

    private static void ValidateScene(Scene scene, List<string> errors)
    {
        string sceneName = scene.name;
        bool isBootstrap = string.Equals(sceneName, ProjectRuntimeContracts.SceneNames.Bootstrap, StringComparison.Ordinal);

        LifetimeScope[] scopes = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<LifetimeScope>(true))
            .Where(scope => scope.gameObject.scene == scene)
            .ToArray();

        if (isBootstrap)
        {
            if (scopes.OfType<TransitionSceneLifetimeScope>().Any())
            {
                errors.Add($"{sceneName}: Bootstrap must not contain content composition roots.");
            }

            if (scopes.Count(s => s is not TransitionSceneLifetimeScope) != 1)
            {
                errors.Add($"{sceneName}: Bootstrap must contain exactly one root LifetimeScope.");
            }

            if (scene.GetRootGameObjects().Any(root => root.name == "MenuScenery"))
            {
                errors.Add($"{sceneName}: Bootstrap must not own menu scenery (MainMenu owns it).");
            }

            BootstrapLifetimeScope? bootstrap = scopes.OfType<BootstrapLifetimeScope>().SingleOrDefault();
            if (bootstrap != null)
            {
                ValidateBootstrapScope(sceneName, bootstrap, errors);
            }
        }
        else
        {
            if (scopes.Length != 1)
            {
                errors.Add(
                    $"{sceneName}: content scene must contain exactly one LifetimeScope, found {scopes.Length}.");
            }

            if (scopes.OfType<TransitionSceneLifetimeScope>().Count() != scopes.Length)
            {
                errors.Add($"{sceneName}: every composition root in a content scene must derive from TransitionSceneLifetimeScope.");
            }
        }

        foreach (LifetimeScope scope in scopes)
        {
            SerializedObject serialized = new(scope);
            SerializedProperty parentReference = serialized.FindProperty("parentReference")
                ?? serialized.FindProperty("ParentReference");
            if (parentReference != null)
            {
                SerializedProperty typeName = parentReference.FindPropertyRelative("TypeName");
                if (typeName != null && !string.IsNullOrEmpty(typeName.stringValue))
                {
                    errors.Add(
                        $"{sceneName}: {scope.GetType().Name} carries a serialized ParentReference " +
                        $"('{typeName.stringValue}'). Runtime parenting must come from BootstrapLifetimeScope.EnqueueParent only.");
                }
            }
        }

        if (scopes.Length == 1)
        {
            ValidateSingleRoot(sceneName, scene, scopes[0], errors);
        }

        GameLifetimeScope? gameScope = scopes.OfType<GameLifetimeScope>().FirstOrDefault();
        if (gameScope != null)
        {
            ValidateGameScope(sceneName, gameScope, errors);
        }

        int uiDocumentCount = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<UIDocument>(true))
            .Count(document => document.gameObject.scene == scene);
        if (uiDocumentCount != 1)
        {
            errors.Add($"{sceneName}: expected exactly one UIDocument, found {uiDocumentCount}.");
        }

        ValidateCameras(sceneName, scene, isBootstrap, errors);
        ValidateCrossSceneReferences(scene, sceneName, errors);
    }

    private static void ValidateGameScope(string sceneName, GameLifetimeScope scope, List<string> errors)
    {
        string[] requiredFields =
        {
            "_servicesRoot", "_runtimeRoot", "_robotsRoot", "_buildingsRoot",
            "_vfxRoot", "_floatingUIRoot", "_audioEventsRoot",
            "_uiDocument", "_postProcessVolume", "_playerMovement",
        };

        SerializedObject serialized = new(scope);
        foreach (string fieldName in requiredFields)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                errors.Add($"{sceneName}: GameLifetimeScope is missing serialized field {fieldName} (stale scene or stale contract).");
                continue;
            }

            UnityEngine.Object? reference = property.objectReferenceValue;
            if (reference == null)
            {
                errors.Add($"{sceneName}: GameLifetimeScope.{fieldName} is not assigned.");
            }
            else if (reference is Component component && component.gameObject.scene != scope.gameObject.scene)
            {
                errors.Add($"{sceneName}: GameLifetimeScope.{fieldName} references an object from another scene.");
            }
        }

        Transform? servicesRoot = scope.ServicesRoot;
        if (servicesRoot == null)
        {
            return;
        }

        if (servicesRoot.gameObject.activeSelf)
        {
            errors.Add($"{sceneName}: {ServicesInactiveMessage}");
        }

        foreach (string group in _ServiceGroups)
        {
            Transform groupRoot = servicesRoot.Find(group);
            if (groupRoot == null)
            {
                errors.Add($"{sceneName}: Services/{group} group is missing.");
                continue;
            }

            var componentTypes = new Dictionary<Type, int>();
            foreach (Transform child in groupRoot.Cast<Transform>())
            {
                foreach (Component component in child.GetComponents<Component>())
                {
                    if (component is Transform || component is LifetimeScope)
                    {
                        continue;
                    }

                    Type type = component.GetType();
                    componentTypes.TryGetValue(type, out int count);
                    componentTypes[type] = count + 1;
                }
            }

            foreach ((Type type, int count) in componentTypes)
            {
                if (count > 1)
                {
                    errors.Add(
                        $"{sceneName}: Services/{group} contains {count} objects with component {type.Name}; " +
                        "manager components must not be duplicated within a service group.");
                }
            }
        }

        ValidateManagerContract(sceneName, scope, errors);
    }

    private static void ValidateBootstrapScope(string sceneName, BootstrapLifetimeScope scope, List<string> errors)
    {
        string[] requiredFields =
        {
            "_applicationCamera", "_connectionManager", "_networkService", "_audioSystem",
            "_clientConfigManager", "_clientAssetLoader", "_textureStorageManager",
            "_loadingScreen", "_studioListener",
        };

        SerializedObject serialized = new(scope);
        foreach (string fieldName in requiredFields)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                errors.Add($"{sceneName}: BootstrapLifetimeScope is missing serialized field {fieldName}.");
                continue;
            }

            UnityEngine.Object? reference = property.objectReferenceValue;
            if (reference == null)
            {
                errors.Add($"{sceneName}: BootstrapLifetimeScope.{fieldName} is not assigned.");
            }
            else if (reference is Component component && component.gameObject.scene != scope.gameObject.scene)
            {
                errors.Add($"{sceneName}: BootstrapLifetimeScope.{fieldName} references an object from another scene.");
            }
        }
    }

    private static void ValidateManagerContract(string sceneName, GameLifetimeScope scope, List<string> errors)
    {
        // The typed manager contract is both the runtime and build-time
        // guarantee. There is no group-by-name runtime fallback: empty or
        // partial bindings fail startup and must fail validation here too.
        int bindingCount = scope.ManagerBindings.Count;
        if (bindingCount == 0)
        {
            errors.Add(
                $"{sceneName}: GameLifetimeScope has no typed manager contract. " +
                "Run Kern/Architecture/Populate Manager Contract before building.");
            return;
        }

        Transform servicesRoot = scope.ServicesRoot;
        if (servicesRoot == null)
        {
            return;
        }

        var bound = new HashSet<UnityEngine.Object>();
        var boundTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManagerBinding binding in scope.ManagerBindings)
        {
            MonoBehaviour? target = binding.Target;
            if (target == null)
            {
                errors.Add($"{sceneName}: a ManagerBinding for '{binding.ManagerType}' has a null target.");
                continue;
            }

            if (!bound.Add(target))
            {
                errors.Add(
                    $"{sceneName}: manager '{target.GetType().Name}' appears in more than one ManagerBinding.");
            }

            string expectedType = target.GetType().AssemblyQualifiedName ?? string.Empty;
            if (!string.Equals(binding.ManagerType, expectedType, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{sceneName}: ManagerBinding for '{target.GetType().Name}' has stale type identity '{binding.ManagerType}'.");
            }
            else if (!boundTypes.Add(expectedType))
            {
                errors.Add(
                    $"{sceneName}: duplicate ManagerBinding type '{target.GetType().Name}'.");
            }

            if (target.gameObject.scene != scope.gameObject.scene)
            {
                errors.Add(
                    $"{sceneName}: ManagerBinding for '{target.GetType().Name}' references another scene.");
                continue;
            }

            string? serviceGroup = binding.ServiceGroup;
            Transform? groupRoot = string.IsNullOrWhiteSpace(serviceGroup)
                ? null
                : servicesRoot.Find(serviceGroup!);
            if (groupRoot == null || !target.transform.IsChildOf(groupRoot))
            {
                errors.Add(
                    $"{sceneName}: ManagerBinding for '{target.GetType().Name}' does not belong to declared " +
                    $"Services/{serviceGroup ?? "<null>"} group.");
            }
        }

        foreach (string group in _ServiceGroups)
        {
            Transform groupRoot = servicesRoot.Find(group);
            if (groupRoot == null)
            {
                continue;
            }

            foreach (Transform child in groupRoot.Cast<Transform>())
            {
                foreach (Component component in child.GetComponents<Component>())
                {
                    if (component is Transform || component is LifetimeScope)
                    {
                        continue;
                    }

                    // Every concrete manager component authored in a service
                    // group must be represented in the typed contract.
                    if (component is MonoBehaviour manager && !bound.Contains(manager))
                    {
                        errors.Add(
                            $"{sceneName}: manager '{manager.GetType().Name}' under Services/{group} has no typed ManagerBinding. " +
                            "Run Kern/Architecture/Populate Manager Contract.");
                    }
                }
            }
        }
    }

    // Единственный корень сцены — её composition root: объект рядом со
    // scope контейнер не видит, и его [Inject] остаются пустыми.
    private static void ValidateSingleRoot(
        string sceneName,
        Scene scene,
        LifetimeScope scope,
        List<string> errors)
    {
        if (scope.transform.parent != null)
        {
            errors.Add($"{sceneName}: {scope.GetType().Name} must be the scene root, not a child of '{scope.transform.parent.name}'.");
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != scope.gameObject)
            {
                errors.Add(
                    $"{sceneName}: root object '{root.name}' lives outside {scope.GetType().Name}; " +
                    "every authored object must be under the composition root.");
            }
        }
    }

    private static void ValidateCameras(string sceneName, Scene scene, bool isBootstrap, List<string> errors)
    {
        if (isBootstrap)
        {
            return;
        }

        foreach (Camera camera in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                     .Where(camera => camera.gameObject.scene == scene))
        {
            errors.Add(
                $"{sceneName}: camera '{camera.name}'. Only Bootstrap owns a camera; " +
                "the persistent application camera renders the game, offscreen views draw through command buffers.");
        }
    }

    private static void ValidateCrossSceneReferences(Scene scene, string sceneName, List<string> errors)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.gameObject.scene != scene)
                {
                    continue;
                }

                SerializedObject serialized = new(behaviour);
                SerializedProperty iterator = serialized.GetIterator();
                bool enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = true;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    if (iterator.objectReferenceValue is not Component referenced ||
                        !referenced.gameObject.scene.IsValid() ||
                        referenced.gameObject.scene == scene)
                    {
                        continue;
                    }

                    errors.Add(
                        $"{sceneName}: '{behaviour.GetType().Name}' on '{behaviour.name}' has a serialized reference " +
                        $"'{iterator.propertyPath}' into another scene ('{referenced.gameObject.scene.name}'). " +
                        "Cross-scene serialized references are forbidden; use DI or the scene's own contract.");
                }
            }
        }
    }
}
