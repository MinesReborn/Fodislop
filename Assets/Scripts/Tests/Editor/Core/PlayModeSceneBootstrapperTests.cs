#nullable enable

using Kern.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Kern.Tests.Core;

[TestFixture]
public sealed class PlayModeSceneBootstrapperTests
{
    private SceneAsset? _originalStartScene;

    [SetUp]
    public void SetUp()
    {
        _originalStartScene = EditorSceneManager.playModeStartScene;
    }

    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.playModeStartScene = _originalStartScene;
    }

    [Test]
    public void BootstrapSceneAsset_ExistsAtExpectedPath()
    {
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayModeSceneBootstrapper.BootstrapScenePath);
        Assert.That(sceneAsset, Is.Not.Null, $"Bootstrap scene must exist at {PlayModeSceneBootstrapper.BootstrapScenePath}");
    }

    [Test]
    public void EnsurePlayModeStartScene_AssignsBootstrapScene()
    {
        EditorSceneManager.playModeStartScene = null;
        PlayModeSceneBootstrapper.EnsurePlayModeStartScene();

        Assert.That(EditorSceneManager.playModeStartScene, Is.Not.Null);
        Assert.That(EditorSceneManager.playModeStartScene!.name, Is.EqualTo("Bootstrap"));
    }
}
