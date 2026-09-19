#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kern.Core;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Kern.Editor;

public static class ManagerContractMigrator
{
    private const string ScopeSourcePath = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs";
    private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

    private static readonly Regex _CallPattern = new(
        @"RegisterManager<(?<type>[A-Za-z0-9_.]+)>\(\s*builder\s*,\s*\""(?<group>[A-Za-z0-9_]+)\""",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, Type> _ResolvedTypes = new();

    [MenuItem("Kern/Architecture/Populate Manager Contract")]
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

        Scene scene = OpenOrReuse(MainGameScenePath, out bool openedHere);
        try
        {
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
            CloseIfOpenedHere(scene, openedHere);
        }
    }

    // Сцена, уже открытая в редакторе, правится на месте. OpenScene с диска
    // в режиме Single молча выбрасывал несохранённые правки: удалённые через
    // редактор объекты возвращались, а миграция сохраняла старую версию.
    private static Scene OpenOrReuse(string scenePath, out bool openedHere)
    {
        Scene loaded = SceneManager.GetSceneByPath(scenePath);
        if (loaded.IsValid() && loaded.isLoaded)
        {
            openedHere = false;
            return loaded;
        }

        openedHere = true;
        return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
    }

    private static void CloseIfOpenedHere(Scene scene, bool openedHere)
    {
        if (openedHere && SceneManager.sceneCount > 1)
        {
            EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }

    private static List<(string Type, string Group)> ReadContract()
    {
        string source = System.IO.File.ReadAllText(ScopeSourcePath);
        var result = new List<(string, string)>();
        foreach (Match match in _CallPattern.Matches(source))
        {
            result.Add((match.Groups["type"].Value, match.Groups["group"].Value));
        }

        return result;
    }

    private static Component? FindManagerComponent(
        Scene scene,
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
        if (!_ResolvedTypes.TryGetValue(name, out Type? type))
        {
            type = null;
            string fullName = $"Kern.{name}";

            // UnityEditor.TypeCache вместо AppDomain.GetAssemblies (UAC0005):
            // домен отдаёт в том числе уже выгруженные сборки, и обход их типов
            // роняет редактор или течёт. Ищем только среди наследников
            // MonoBehaviour — мигратор ничего другого и не подставляет.
            Type? byShortName = null;
            foreach (Type candidate in UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                string? assemblyName = candidate.Assembly.GetName().Name;
                if (assemblyName?.StartsWith("Kern", StringComparison.Ordinal) != true &&
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
            _ResolvedTypes[name] = type ?? typeof(MonoBehaviour);
        }

        return type == typeof(MonoBehaviour) ? null : type;
    }

    private static GameLifetimeScope FindSingleSceneComponent(Scene scene)
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
