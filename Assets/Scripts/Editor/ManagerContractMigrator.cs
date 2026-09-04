#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fodinae.Core;
using Fodinae.Core.Lifecycle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using VContainer.Unity;

namespace Fodinae.Editor;

/// <summary>
/// One-way editor migrator for the MainGame typed manager contract.
///
/// Reads the <c>RegisterManager&lt;T&gt;(builder, "group")</c> calls straight out
/// of <c>GameLifetimeScope.cs</c> (so it tracks the code as the contract
/// changes), locates each manager under <c>Services/{group}/{T.Name}</c> in
/// MainGame.unity and writes it as a serialized <see cref="ManagerBinding"/>
/// on the scope.
///
/// The scene must still be validated by
/// <see cref="ProductionSceneContractValidator"/>;
/// this tool only populates the binding list and never repairs, moves or
/// re-parents managers.
/// </summary>
public static class ManagerContractMigrator
{
    private const string ScopeSourcePath = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs";
    private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

    private static readonly Regex CallPattern = new(
        @"RegisterManager<(?<type>[A-Za-z0-9_.]+)>\(\s*builder\s*,\s*\""(?<group>[A-Za-z0-9_]+)\""",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, Type> ResolvedTypes = new();

    [MenuItem("Fodinae/Architecture/Populate Manager Contract")]
    public static void Populate()
    {
        var contracts = ReadContract();
        if (contracts.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Manager contract",
                $"No RegisterManager<T> calls found in {ScopeSourcePath}.",
                "OK");
            return;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(MainGameScenePath, OpenSceneMode.Single);
            GameLifetimeScope scope = FindSingleSceneComponent(scene);
            List<ManagerBinding> bindings = new();
            List<string> errors = new();

            foreach ((string typeName, string group) in contracts)
            {
                Component? component = FindManagerComponent(scene, scope, group, typeName, errors);
                if (component == null)
                {
                    continue;
                }

                Type? type = ResolveType(typeName);
                bindings.Add(new ManagerBinding(
                    type?.AssemblyQualifiedName ?? typeName,
                    group,
                    (MonoBehaviour)component));
            }

            SerializedObject serialized = new(scope);
            SerializedProperty list = serialized.FindProperty("_managerBindings");
            if (list == null)
            {
                errors.Add("GameLifetimeScope has no serialized _managerBindings field.");
            }
            else
            {
                list.ClearArray();
                foreach (ManagerBinding binding in bindings)
                {
                    // ManagerBinding target is a MonoBehaviour; write the serialized
                    // reference through the plain-object field.
                    list.InsertArrayElementAtIndex(list.arraySize);
                    SerializedProperty element = list.GetArrayElementAtIndex(list.arraySize - 1);
                    SerializedProperty target = element.FindPropertyRelative("_target");
                    if (target != null)
                    {
                        target.objectReferenceValue = binding.Target;
                    }

                    SerializedProperty typeProp = element.FindPropertyRelative("_managerType");
                    if (typeProp != null)
                    {
                        typeProp.stringValue = binding.ManagerType ?? string.Empty;
                    }

                    SerializedProperty groupProp = element.FindPropertyRelative("_serviceGroup");
                    if (groupProp != null)
                    {
                        groupProp.stringValue = binding.ServiceGroup ?? string.Empty;
                    }
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            foreach (string error in errors)
            {
                Debug.LogWarning($"[ManagerContract] {error}");
            }

            Debug.Log($"[ManagerContract] Populated {bindings.Count} manager bindings in MainGame.unity from {contracts.Count} RegisterManager calls.");
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    [MenuItem("Fodinae/Architecture/Populate Bootstrap Contract")]
    public static void PopulateBootstrap()
    {
        const string scenePath = "Assets/Scenes/Bootstrap.unity";
        string[] fieldNames =
        {
            "_applicationCamera", "_connectionManager", "_networkService", "_audioSystem",
            "_clientConfigManager", "_clientAssetLoader", "_textureStorageManager",
            "_loadingScreen", "_studioListener",
        };

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            BootstrapLifetimeScope scope = FindSingleBootstrap(scene);
            SerializedObject serialized = new(scope);
            List<string> errors = new();

            foreach (string fieldName in fieldNames)
            {
                SerializedProperty property = serialized.FindProperty(fieldName);
                if (property == null)
                {
                    errors.Add($"BootstrapLifetimeScope has no serialized field '{fieldName}'.");
                    continue;
                }

                Type? fieldType = scope.GetType().GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public)?.FieldType;
                Component? target = fieldType == null ? null : FindComponent(scene, fieldType);
                if (target == null)
                {
                    errors.Add($"No authored component of type '{fieldType?.Name ?? "unknown"}' for '{fieldName}'.");
                    continue;
                }

                property.objectReferenceValue = target;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            foreach (string error in errors)
            {
                Debug.LogWarning($"[BootstrapContract] {error}");
            }

            Debug.Log($"[BootstrapContract] Populated authored references in {scenePath}.");
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static BootstrapLifetimeScope FindSingleBootstrap(Scene scene)
    {
        BootstrapLifetimeScope[] scopes = FindComponents<BootstrapLifetimeScope>(scene);
        if (scopes.Length != 1)
        {
            throw new InvalidOperationException(
                $"Bootstrap scene must contain exactly one BootstrapLifetimeScope, found {scopes.Length}.");
        }

        return scopes[0];
    }

    private static Component? FindComponent(Scene scene, Type componentType)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // Iterate authored components explicitly instead of relying on the
            // typed GetComponentsInChildren overload. During an assembly refresh
            // Unity can briefly hold a scene component whose MonoScript type
            // identity was loaded from the just-rebuilt assembly; comparing the
            // concrete component type keeps the editor migration deterministic.
            Component[] components = root.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component != null && component.gameObject.scene == scene && component.GetType() == componentType)
                {
                    return component;
                }
            }
        }

        return null;
    }

    private static T[] FindComponents<T>(Scene scene)
        where T : Component
    {
        List<T> result = new();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
            {
                if (component.gameObject.scene == scene)
                {
                    result.Add(component);
                }
            }
        }

        return result.ToArray();
    }

    private static List<(string Type, string Group)> ReadContract()
    {
        string source = System.IO.File.ReadAllText(ScopeSourcePath);
        var result = new List<(string, string)>();
        foreach (Match match in CallPattern.Matches(source))
        {
            result.Add((match.Groups["type"].Value, match.Groups["group"].Value));
        }

        return result;
    }

    private static Component? FindManagerComponent(
        UnityEngine.SceneManagement.Scene scene,
        GameLifetimeScope scope,
        string group,
        string typeName,
        List<string> errors)
    {
        // Typed binding already present and valid? Honor it (idempotency).
        Type? type = ResolveType(typeName);
        if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type))
        {
            foreach (ManagerBinding existing in scope.ManagerBindings)
            {
                if (existing.Target != null &&
                    existing.Target.GetType() == type &&
                    existing.Target.gameObject.scene == scene)
                {
                    return existing.Target;
                }
            }
        }

        Transform servicesRoot = scope.ServicesRoot;
        if (servicesRoot == null)
        {
            errors.Add($"MainGame scope has no ServicesRoot reference.");
            return null;
        }

        Transform groupRoot = servicesRoot.Find(group);
        if (groupRoot == null)
        {
            errors.Add($"Services/{group} is missing under ServicesRoot.");
            return null;
        }

        string simpleName = typeName.Split('.')[^1];
        Transform? managerObject = groupRoot.Find(simpleName);
        if (managerObject == null)
        {
            errors.Add($"Manager '{simpleName}' not found under Services/{group}. Author it before populating the contract.");
            return null;
        }

        Component? component = null;
        if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type))
        {
            component = managerObject.GetComponent(type);
        }
        else
        {
            component = managerObject.GetComponent<MonoBehaviour>();
        }

        if (component == null)
        {
            errors.Add($"Object Services/{group}/{simpleName} has no {typeName} component.");
            return null;
        }

        return component;
    }

    private static Type? ResolveType(string name)
    {
        if (!ResolvedTypes.TryGetValue(name, out Type? type))
        {
            type = null;
            string fullName = $"Fodinae.{name}";

            // UnityEditor.TypeCache вместо AppDomain.GetAssemblies (UAC0005):
            // домен отдаёт в том числе уже выгруженные сборки, и обход их типов
            // роняет редактор или течёт. Ищем только среди наследников
            // MonoBehaviour — мигратор ничего другого и не подставляет.
            Type? byShortName = null;
            foreach (Type candidate in UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                string? assemblyName = candidate.Assembly.GetName().Name;
                if (assemblyName?.StartsWith("Fodinae", StringComparison.Ordinal) != true &&
                    assemblyName?.StartsWith("Assembly-CSharp", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                // Полное имя выигрывает у короткого, как и раньше.
                if (candidate.FullName == fullName)
                {
                    type = candidate;
                    break;
                }

                byShortName ??= candidate.Name == name ? candidate : null;
            }

            type ??= byShortName;
            ResolvedTypes[name] = type ?? typeof(MonoBehaviour);
        }

        return type == typeof(MonoBehaviour) ? null : type;
    }

    private static GameLifetimeScope FindSingleSceneComponent(UnityEngine.SceneManagement.Scene scene)
    {
        GameLifetimeScope? result = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (GameLifetimeScope scope in root.GetComponentsInChildren<GameLifetimeScope>(true))
            {
                if (scope.gameObject.scene != scene)
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.name}' contains multiple GameLifetimeScope components.");
                }

                result = scope;
            }
        }

        return result ?? throw new InvalidOperationException(
            $"Scene '{scene.name}' contains no GameLifetimeScope component.");
    }
}
