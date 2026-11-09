#nullable enable

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MainMenuUxmlContractTests
{
    private static readonly string[] s_requiredLoaderElements =
    new[] {

        "LoaderContainer",
        "LoaderContent",
        "LoaderProgressFill",
        "LoaderPhaseLabel",
        "LoaderPhaseCount",
        "LoaderPhaseList",
        "LoaderExplain",
        "LoaderTip",
        "RetryDescentButton",
        "CancelDescentButton",

    };

    [Test]
    public void MainMenuResourceContainsCompleteLoadingUI()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/Menu/Templates/MainMenu");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        foreach (string elementName in s_requiredLoaderElements)
        {
            Assert.That(tree.Q(elementName), Is.Not.Null, $"Missing #{elementName}");
        }
    }
}
