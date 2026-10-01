#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Inventory;

/// <summary>
/// Drives the shared tooltip card (ItemTooltipCard from Inventory.uxml):
/// fills the icon, the yellow name badge, and the description for the
/// selected item. Server metadata (SelectItemPacket applied by
/// InventoryProcessor) has priority; when it is missing or empty the local
/// item catalog (ItemRegistry → русские названия/описания) is used instead,
/// so the card is never left with the raw enum name and an empty text.
/// InventoryView re-shows the card when that metadata arrives.
/// </summary>
internal sealed class InventoryTooltipController
{
    private readonly VisualElement _card;
    private readonly Image _icon;
    private readonly Label _type;
    private readonly Label _desc;
    private readonly IItemCatalog _catalog;

    public InventoryTooltipController(VisualElement root, IItemCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _card = root.Q<VisualElement>("ItemTooltipCard") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] ItemTooltipCard missing from Inventory.uxml.");
        _icon = _card.Q<Image>("TooltipIcon") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipIcon missing from ItemTooltipCard.");
        _type = _card.Q<Label>("TooltipType") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipType missing from ItemTooltipCard.");
        _desc = _card.Q<Label>("TooltipDesc") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipDesc missing from ItemTooltipCard.");
    }

    public void ShowItemInfo(ItemData item, Texture2D? icon)
    {
        if (icon != null)
        {
            _icon.image = icon;
            _icon.style.display = DisplayStyle.Flex;
        }
        else
        {
            _icon.image = null;
            _icon.style.display = DisplayStyle.None;
        }

        // Жёлтый бейдж — единственное место с названием: сначала серверное
        // имя из метаданных, при его отсутствии (или если сервер прислал
        // голое имя типа) — локальный справочник предметов.
        string name = item.Name;
        if (string.IsNullOrWhiteSpace(name) || name == item.ItemType.ToString())
        {
            name = _catalog.GetName(item.ItemType);
        }

        _type.text = name;

        // Описание: серверное из метаданных, при пустом — локальный
        // справочник предметов.
        string description = item.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = _catalog.GetDescription(item.ItemType);
        }

        _desc.text = description;
        _card.style.display = DisplayStyle.Flex;
    }

    public void HideTooltip()
    {
        _card.style.display = DisplayStyle.None;
    }
}