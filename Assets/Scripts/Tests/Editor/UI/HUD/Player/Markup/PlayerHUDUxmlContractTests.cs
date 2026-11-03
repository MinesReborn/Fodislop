#nullable enable

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class PlayerHUDUxmlContractTests
{
    private static readonly string[] s_requiredHudElements =
    new[] {

        "TopStatusPill",
        "FPSCounterLabel",
        "PlayerStatusPanel",
        "NicknameLabel",
        "LevelLabel",
        "HPCountLabel",
        "HPBarFill",
        "MoneyLabel",
        "CreditsLabel",
        "GeologyLabel",
        "BasketPercentLabel",
        "BasketContainer",
        "AutoDigButton",
        "MobileControls",
        "MobileMovePad",
        "MobileMoveKnob",
        "MobileDigButton",
        "MobileGeoButton",
        "MobileHealButton",
        "MobileBuildCyanButton",
        "MobileBuildGrayButton",
        "MobileBuildGreenButton",
        "MobileBuildWhiteButton",
        "ProgrammatorButton",
        "ProgramRunButton",
        "RespawnButton",
        "BuildingsButton",
        "FaqButton",
        "MConsoleButton",
        "SkillContainer",
        "MissionPanel",

    };

    private static readonly string[] s_requiredOverlayElements =
    new[] {

        "WorldMapOverlay",
        "WorldMapWindow",
        "WorldMapImage",
        "WorldMapCloseButton",
        "WorldMapFollowPlayerButton",
        "WorldMapStatus",
        "RespawnPopup",
    };

    [Test]
    public void PlayerHudResourceLoadsAndContainsHudRoot()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/HUD/Player/Templates/PlayerHUD");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        VisualElement? hudRoot = tree.Q("PlayerHUDRoot");
        Assert.That(hudRoot, Is.Not.Null);
    }

    [Test]
    public void GameplayHudElementsAreContainedWithinHUDContent()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/HUD/Player/Templates/PlayerHUD");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        VisualElement? hudContent = tree.Q("HUDContent");
        Assert.That(hudContent, Is.Not.Null, "HUDContent container must exist in PlayerHUD.uxml");

        foreach (string elementName in s_requiredHudElements)
        {
            VisualElement? element = hudContent!.Q(elementName);
            Assert.That(element, Is.Not.Null, $"Element #{elementName} must be inside HUDContent");
        }
    }

    [Test]
    public void OverlaysAndPopupsAreNotContainedWithinHUDContent()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/HUD/Player/Templates/PlayerHUD");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        VisualElement? hudContent = tree.Q("HUDContent");
        Assert.That(hudContent, Is.Not.Null);

        foreach (string overlayName in s_requiredOverlayElements)
        {
            VisualElement? insideContent = hudContent!.Q(overlayName);
            Assert.That(insideContent, Is.Null, $"Overlay #{overlayName} must NOT be inside HUDContent");

            VisualElement? inTree = tree.Q(overlayName);
            Assert.That(inTree, Is.Not.Null, $"Overlay #{overlayName} must exist in PlayerHUD.uxml");
        }
    }

    [Test]
    public void HidingHUDContentLeavesWorldMapOverlayUnaffected()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/HUD/Player/Templates/PlayerHUD");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        VisualElement? hudContent = tree.Q("HUDContent");
        VisualElement? worldMapOverlay = tree.Q("WorldMapOverlay");

        Assert.That(hudContent, Is.Not.Null);
        Assert.That(worldMapOverlay, Is.Not.Null);

        hudContent!.style.display = DisplayStyle.None;

        Assert.That(worldMapOverlay!.style.display.value, Is.Not.EqualTo(DisplayStyle.None));
    }
}
