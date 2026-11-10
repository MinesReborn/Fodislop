#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.Models;
using UnityEngine.UIElements;

namespace Kern.UI.Inventory;

/// <summary>
/// Подсказка выбранного предмета (макет visual/kern-ui .item-hint): строка над игроком
/// «Название: Enter — применить, Esc — отмена». Название — серверное из метаданных (SelectItemPacket),
/// при пустом или голом имени типа — локальный справочник предметов.
/// </summary>
internal sealed class InventoryTooltipController
{
    private readonly Label _hint;
    private readonly IItemCatalog _catalog;
    private readonly ILocalizationService _loc;

    public InventoryTooltipController(VisualElement root, IItemCatalog catalog, ILocalizationService loc)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _hint = root.Q<Label>("ItemHint") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] ItemHint missing from Inventory.uxml.");
    }

    public void ShowItemInfo(ItemData item)
    {
        string name = item.Name;
        if (string.IsNullOrWhiteSpace(name) || name == item.ItemType.ToString())
        {
            name = _catalog.GetName(item.ItemType);
        }

        _hint.text = _loc.Get("inventory.hint", name);
        _hint.style.display = DisplayStyle.Flex;
    }

    public void HideTooltip()
    {
        _hint.style.display = DisplayStyle.None;
    }
}
