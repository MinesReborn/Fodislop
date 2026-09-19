#nullable enable

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MainMenuUxmlContractTests
{
    private static readonly string[] _RequiredLoaderElements =
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
    public void MainMenuResourceContainsCompleteLoadingUi()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/MainMenu");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        foreach (string elementName in _RequiredLoaderElements)
        {
            Assert.That(tree.Q(elementName), Is.Not.Null, $"Missing #{elementName}");
        }
    }
}
