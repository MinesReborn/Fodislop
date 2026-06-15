using UnityEngine;

namespace Fodinae.Assets.Scripts.UI
{
    public class ItemData
    {
        public string Name { get; set; }
        public Color IconColor { get; set; }
        public int Quantity { get; set; }

        public ItemData(string name, Color iconColor, int quantity)
        {
            Name = name;
            IconColor = iconColor;
            Quantity = quantity;
        }

        public ItemData Clone() => new ItemData(Name, IconColor, Quantity);
    }
}
