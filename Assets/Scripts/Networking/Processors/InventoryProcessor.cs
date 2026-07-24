using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Inventory;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class InventoryProcessor : IPacketProcessor<InventoryPacket>, IPacketProcessor<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>, IPacketProcessor<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>
    {
        public void Process(InventoryPacket packet)
        {
            var model = InventoryModel.Instance;
            var remaining = new Dictionary<MinesServer.Data.ItemType, long>(packet.Changes);

            for (int i = 0; i < InventoryModel.TOTALSLOTS; i++)
            {
                var existing = model.GetSlot(i);
                if (existing == null)
                {
                    continue;
                }

                if (remaining.TryGetValue(existing.ItemType, out long qty))
                {
                    if (qty <= 0)
                    {
                        model.SetSlot(i, null);
                    }
                    else
                    {
                        existing.Quantity = (int)qty;
                        model.SetSlot(i, existing);
                    }

                    remaining.Remove(existing.ItemType);
                }
            }

            foreach (var kvp in remaining)
            {
                if (kvp.Value <= 0)
                {
                    continue;
                }

                for (int i = 0; i < InventoryModel.TOTALSLOTS; i++)
                {
                    if (model.GetSlot(i) != null)
                    {
                        continue;
                    }

                    var item = new ItemData(
                        kvp.Key.ToString(),
                        UnityEngine.Color.gray,
                        (int)kvp.Value);
                    item.ItemType = kvp.Key;
                    item.Icon = ItemRegistry.GetIcon(kvp.Key);
                    model.SetSlot(i, item);
                    break;
                }
            }
        }

        public void Process(MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket packet)
        {
            var model = InventoryModel.Instance;
            int slot = model.SelectedSlot;
            if (slot < 0)
            {
                return;
            }

            var item = model.GetSlot(slot);
            if (item == null)
            {
                return;
            }

            item.Name = packet.Name;
            item.Description = packet.Description;
            model.SetSlot(slot, item);
        }

        public void Process(MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket packet)
        {
            InventoryModel.Instance.ClearSelection();
        }
    }
}
