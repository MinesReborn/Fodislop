#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kern.Tests.Core;

[TestFixture]
public sealed class ProductionSceneContractValidatorTests
{
    private SceneSetup[] _originalSetup = null!;
    private EditorBuildSettingsScene[] _originalBuildScenes = null!;

    [SetUp]
    public void SetUp()
    {
        _originalSetup = EditorSceneManager.GetSceneManagerSetup();
        _originalBuildScenes = EditorBuildSettings.scenes;
    }

    [TearDown]
    public void TearDown()
    {
        if (_originalSetup.Any(item => item.isLoaded && item.isActive))
        {
            EditorSceneManager.RestoreSceneManagerSetup(_originalSetup);
        }

        EditorBuildSettings.scenes = _originalBuildScenes;
    }

    [Test]
    public void EmptyContentScene_ReportsScopeAndDocumentViolations()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var errors = new List<string>();

        ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

        Assert.That(errors.Any(error => error.Contains("exactly one LifetimeScope")), Is.True);
        Assert.That(errors.Any(error => error.Contains("exactly one UIDocument")), Is.True);
    }

    [Test]
    public void ContentCamera_IsRejected()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var documentObject = new GameObject("Document");
        documentObject.AddComponent<UIDocument>();
        var cameraObject = new GameObject("DisplayCamera");
        cameraObject.AddComponent<Camera>().enabled = false;
        SceneManager.MoveGameObjectToScene(documentObject, scene);
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var errors = new List<string>();

        ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

        Assert.That(errors.Any(error => error.Contains("camera 'DisplayCamera'")), Is.True);
    }

    [Test]
    public void RootNextToCompositionRoot_IsRejected()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var scopeObject = new GameObject("Scope");
        scopeObject.AddComponent<VContainer.Unity.LifetimeScope>();
        var strayObject = new GameObject("Stray");
        SceneManager.MoveGameObjectToScene(scopeObject, scene);
        SceneManager.MoveGameObjectToScene(strayObject, scene);
        var errors = new List<string>();

        ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

        Assert.That(errors, Has.Some.Contains("root object 'Stray' lives outside LifetimeScope"));
    }

    [Test]
    public void ChildOfCompositionRoot_IsAccepted()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var scopeObject = new GameObject("Scope");
        scopeObject.AddComponent<VContainer.Unity.LifetimeScope>();
        var childObject = new GameObject("Child");
        childObject.transform.SetParent(scopeObject.transform);
        SceneManager.MoveGameObjectToScene(scopeObject, scene);
        var errors = new List<string>();

        ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

        Assert.That(errors, Has.None.Contains("lives outside"));
    }

    [Test]
    public void ProductionScenes_AreValid_AndOriginalEditorSetupIsRestored()
    {
        SceneSetup[] before = EditorSceneManager.GetSceneManagerSetup();
        var errors = new List<string>();

        try
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                errors.Clear();
                ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);
                Assert.That(errors, Is.Empty, buildScene.path);
            }
        }
        finally
        {
            if (before.Any(item => item.isLoaded && item.isActive))
            {
                EditorSceneManager.RestoreSceneManagerSetup(before);
            }
        }

        SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
        if (before.Any(item => item.isLoaded && item.isActive))
        {
            Assert.That(after.Select(item => item.path), Is.EqualTo(before.Select(item => item.path)));
            Assert.That(after.Select(item => item.isLoaded), Is.EqualTo(before.Select(item => item.isLoaded)));
            Assert.That(after.Select(item => item.isActive), Is.EqualTo(before.Select(item => item.isActive)));
        }
    }

    [Test]
    public void BuildSettingsValidation_AcceptsAuthoredProductionOrder()
    {
        Assert.DoesNotThrow(BuildSceneOrder.Validate);
    }

    [Test]
    public void BuildSettingsValidation_RejectsWrongOrderWithoutRepairingAuthoringData()
    {
        EditorBuildSettingsScene[] invalid = EditorBuildSettings.scenes.ToArray();
        Assert.That(invalid.Length, Is.GreaterThanOrEqualTo(2));
        (invalid[0], invalid[1]) = (invalid[1], invalid[0]);
        EditorBuildSettings.scenes = invalid;

        Assert.Throws<System.InvalidOperationException>(
            BuildSceneOrder.Validate);
        Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(invalid[0].path));
        Assert.That(EditorBuildSettings.scenes[1].path, Is.EqualTo(invalid[1].path));
    }

    [Test]
    public void BootstrapMissingAuthoredReference_IsReportedWithoutSavingScene()
    {
        Scene bootstrapScene = EditorSceneManager.OpenScene(
            "Assets/Scenes/Bootstrap.unity",
            OpenSceneMode.Single);
        BootstrapLifetimeScope scope = bootstrapScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BootstrapLifetimeScope>(true))
            .Single();
        SerializedObject serialized = new(scope);
        SerializedProperty connection = serialized.FindProperty("_connectionManager");
        Assert.That(connection, Is.Not.Null);
        UnityEngine.Object? original = connection.objectReferenceValue;

        try
        {
            connection.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var errors = new List<string>();

            ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

            Assert.That(
                errors,
                Has.Some.Contains("BootstrapLifetimeScope._connectionManager is not assigned"));
        }
        finally
        {
            connection.objectReferenceValue = original;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(bootstrapScene);
        }
    }
}
