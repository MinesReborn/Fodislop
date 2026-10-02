#nullable enable

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MainMenuUxmlContractTests
{
    private static readonly string[] s_requiredLoaderElements =
    [
        "LoaderContainer",
        "LoaderContent",
        "LoaderProgressFill",
        "LoaderPhaseLabel",
        "LoaderPhaseCount",
        "LoaderPhaseList",
        "CancelDescentButton",
    ];

    [Test]
    public void MainMenuResourceContainsCompleteLoadingUI()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/Menus/MainMenu");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        foreach (string elementName in s_requiredLoaderElements)
        {
            Assert.That(tree.Q(elementName), Is.Not.Null, $"Missing #{elementName}");
        }
    }
}
