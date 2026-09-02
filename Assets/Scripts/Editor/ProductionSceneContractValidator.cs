#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Core;
using Fodinae.Core.Lifecycle;
using Fodinae.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Fodinae.Editor
{
    /// <summary>
    /// Read-only production scene contract validator.
    ///
    /// Replaces the former SceneScopeAuthoring/SceneContractMigration tools:
    /// this validator only CHECKS. It never repairs, moves, re-parents or
    /// saves scenes. Scene setup is data owned by the author; a tool that
    /// "helpfully" fixes it hides real authoring mistakes behind silent
    /// rewrites.
    ///
    /// Checks, per build scene:
    ///  - exactly one composition root per scene; Bootstrap is the only
    ///    scene allowed to hold a root scope and must not hold content roots;
    ///  - no serialized ParentReference TypeName on any LifetimeScope
    ///    (runtime parenting comes from BootstrapLifetimeScope.EnqueueParent);
    ///  - GameLifetimeScope: required serialized references exist, belong to
    ///    the same scene, and the Services root is authored inactive;
    ///  - service groups exist and no manager component is duplicated within
    ///    a service group;
    ///  - exactly one UIDocument per production scene;
    ///  - content scenes contain no enabled display camera (the persistent
    ///    Bootstrap application camera renders the game; only render-texture
    ///    and MenuSceneryController cameras are legal);
    ///  - no serialized object reference points into another loaded scene.
    /// </summary>
    public sealed class ProductionSceneContractValidator : IPreprocessBuildWithReport
    {
        private const string ServicesInactiveMessage =
            "MainGame Services root must be authored inactive: Awake/OnEnable of managers must run only after dependency injection (GameLifetimeScope.ActivateSceneServices).";

        public int callbackOrder => 0;

        [MenuItem("Fodinae/Architecture/Validate Production Scene Contracts")]
        public static void ValidateFromMenu()
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .ToArray();

            if (buildScenes.Length == 0)
            {
                Debug.LogWarning("[SceneContract] No enabled scenes in EditorBuildSettings — nothing to validate.");
                return;
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            List<string> errors = new();
            try
            {
                foreach (EditorBuildSettingsScene buildScene in buildScenes)
                {
                    Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                    ValidateScene(scene, errors);
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            if (errors.Count > 0)
            {
                foreach (string error in errors)
                {
                    Debug.LogError($"[SceneContract] {error}");
                }

                EditorUtility.DisplayDialog(
                    "Scene contract validation failed",
                    $"{errors.Count} violation(s) found. See the Console for details.",
                    "OK");
            }
            else
            {
                Debug.Log($"[SceneContract] All {buildScenes.Length} build scenes satisfy the production scene contract.");
            }
        }

        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .ToArray();
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            List<string> errors = new();
            try
            {
                foreach (EditorBuildSettingsScene buildScene in buildScenes)
                {
                    Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                    ValidateScene(scene, errors);
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "[SceneContract] Build aborted: the open build scenes violate the production scene contract:\n- " +
                    string.Join("\n- ", errors));
            }
        }

        internal static void ValidateAllLoadedScenes(List<string> errors)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded)
                {
                    ValidateScene(scene, errors);
                }
            }
        }

        private static void ValidateScene(Scene scene, List<string> errors)
        {
            string sceneName = scene.name;
            bool isBootstrap = string.Equals(sceneName, "Bootstrap", StringComparison.Ordinal);

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

            string[] requiredGroups = { "Networking", "World", "Rendering", "Gameplay", "UI", "Audio" };
            foreach (string group in requiredGroups)
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
                    "Run Fodinae/Architecture/Populate Manager Contract before building.");
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

            string[] groups = { "Networking", "World", "Rendering", "Gameplay", "UI", "Audio" };
            foreach (string group in groups)
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
                                "Run Fodinae/Architecture/Populate Manager Contract.");
                        }
                    }
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
                if (!camera.enabled || camera.targetTexture != null ||
                    camera.GetComponentInParent<MenuSceneryController>() != null)
                {
                    continue;
                }

                errors.Add(
                    $"{sceneName}: enabled display camera '{camera.name}'. Content scenes must not own a display camera; " +
                    "the persistent Bootstrap application camera renders the game.");
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
}
