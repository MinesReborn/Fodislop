using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.UI.HUD.Inventory.Model
{
    public class ItemData
    {
        public string Name { get; set; }
        public Color IconColor { get; set; }
        public int Quantity { get; set; }
        public string Description { get; set; }
        public ItemType ItemType { get; set; }
        public Texture2D Icon { get; set; }

        public ItemData(string name, Color iconColor, int quantity)
        {
            Name = name;
            IconColor = iconColor;
            Quantity = quantity;
        }

        public ItemData Clone() => new ItemData(Name, IconColor, Quantity);
    }
}
