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

        "StatusBar",
        "OnlinePlayersLabel",
        "OnlineProgramsLabel",
        "BuildVersionLabel",
        "FPSCounterLabel",
        "AssetStatusPill",
        "AssetStatusFill",
        "AssetStatusLabel",
        "PlayerStatusPanel",
        "ClanIcon",
        "PlayerIdLabel",
        "NicknameLabel",
        "HPDots",
        "HPCountLabel",
        "MoneyLabel",
        "CreditsLabel",
        "CargoPanel",
        "CargoPercent",
        "BasketPercentLabel",
        "CargoCells",
        "BasketContainer",
        "GeologyBlock",
        "GeologyCells",
        "GeologyCountLabel",
        "GeologyLabel",
        "StatusPanel",
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
        "ProgramLabel",
        "ProgramNameLabel",
        "ProgramRunButton",
        "RespawnButton",
        "BuildingsButton",
        "FaqButton",
        "MConsoleButton",
        "SysMenuButton",
        "SkillContainer",
        "MissionPanel",
        "MissionCells",

    };

    private static readonly string[] s_requiredOverlayElements =
    new[] {

        "WorldMapOverlay",
        "WorldMapWindow",
        "WorldMapImage",
        "WorldMapCloseButton",
        "WorldMapFollowPlayerButton",
        "WorldMapCoords",
        "WorldMapScale",
        "WorldMapMarkButton",
        "WorldMapMarkPin",
    };

    // Элементы, которые код HUD запрашивает как Q<Button> с throw при отсутствии:
    // если в UXML элемент объявлен не Button (например ui:VisualElement), Q<Button>
    // вернёт null и InitializeHUD упадёт, оторвав весь HUD от данных (деньги,
    // корзина и кодовые надписи кнопок перестанут обновляться).
    private static readonly string[] s_requiredButtonElements =
    new[] {

        "ClanButton",
        "DropBoxButton",
        "DonateButton",
        "ProgrammatorButton",
        "ProgramRunButton",
        "RespawnButton",
        "BuildingsButton",
        "FaqButton",
        "MConsoleButton",
        "SysMenuButton",
        "AutoDigButton",

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
    public void CodeRequiredElementsResolveAsButtons()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/HUD/Player/Templates/PlayerHUD");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();

        foreach (string buttonName in s_requiredButtonElements)
        {
            VisualElement? typed = tree.Q<Button>(buttonName);
            Assert.That(typed, Is.Not.Null,
                $"Element #{buttonName} must be a ui:Button in PlayerHUD.uxml: " +
                "PlayerHUDView queries it as Q<Button> and throws on mismatch.");
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
