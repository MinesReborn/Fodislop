using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public static class ItemRegistry
    {
        private static readonly Dictionary<ItemType, ItemMeta> _items = new()
        {
            [ItemType.Teleport] = new("Телепорт"),
            [ItemType.Resp] = new("Респаун"),
            [ItemType.Up] = new("Ап"),
            [ItemType.Market] = new("Маркет"),
            [ItemType.Clans] = new("Кланы"),
            [ItemType.PlasmBomb] = new("Плазма-бомба"),
            [ItemType.ProtonBomb] = new("Протонная бомба"),
            [ItemType.RazBomb] = new("Раз-бомба"),
            [ItemType.Cred] = new("Cred"),
            [ItemType.Rem] = new("Rem"),
            [ItemType.Geopack] = new("Геопак"),
            [ItemType.GeoCyan] = new("Гео-циан"),
            [ItemType.GeoRed] = new("Гео-ред"),
            [ItemType.GeoViolet] = new("Гео-виолет"),
            [ItemType.GeoBlack] = new("Гео-блек"),
            [ItemType.GeoWhite] = new("Гео-вайт"),
            [ItemType.GeoBlue] = new("Гео-блю"),
            [ItemType.VulkanRadar] = new("Вулкан-радар"),
            [ItemType.AliveRadar] = new("Алайв-радар"),
            [ItemType.RobotRadar] = new("Робот-радар"),
            [ItemType.PortableTeleporter] = new("Портативный телепорт"),
            [ItemType.ConstructionBot] = new("Строительный бот"),
            [ItemType.Generator] = new("Генератор"),
            [ItemType.Charge] = new("Заряд"),
            [ItemType.Craft] = new("Крафт"),
            [ItemType.BombShop] = new("Магазин бомб"),
            [ItemType.Gun] = new("Пушка"),
            [ItemType.Gate] = new("Ворота"),
            [ItemType.Disassembler] = new("Дисассемблер"),
            [ItemType.Storage] = new("Хранилище"),
            [ItemType.Scanner] = new("Сканер"),
            [ItemType.UpgradeBooster] = new("Ускоритель апгрейда"),
            [ItemType.FreeUp] = new("FreeUp"),
            [ItemType.MineBooster] = new("MineBooster"),
            [ItemType.GeoHypno] = new("Гео-гипно"),
            [ItemType.Poly] = new("Поли"),
            [ItemType.Nano] = new("Нано"),
            [ItemType.Battery] = new("Батарея"),
            [ItemType.Trans] = new("Транс"),
            [ItemType.Compressor] = new("Компрессор"),
            [ItemType.C190] = new("190"),
            [ItemType.FED] = new("FED"),
            [ItemType.GeoBlackRock] = new("Гео-блек-рок"),
            [ItemType.GeoRedRock] = new("Гео-ред-рок"),
            [ItemType.Auto] = new("Авто"),
            [ItemType.EMI] = new("EMI"),
            [ItemType.GeoRainbow] = new("Гео-рейнбоу"),
            [ItemType.BotSpot] = new("Бот-спот"),
            [ItemType.ScienceCentre] = new("Научный центр"),
            [ItemType.Currency] = new("Валюта"),
            [ItemType.OPP] = new("OPP"),
        };

        private static readonly Dictionary<ItemType, Texture2D> _iconCache = new();

        public static string GetName(ItemType type) =>
            _items.TryGetValue(type, out var meta) ? meta.Name : type.ToString();

        public static IEnumerable<ItemType> AllTypes => _items.Keys;

        public static Texture2D GetIcon(ItemType type)
        {
            if (_iconCache.TryGetValue(type, out var t)) return t;
            var path = Application.dataPath + "/Textures/items/" + type.ToString().ToLower() + ".png";
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            _iconCache[type] = tex;
            return tex;
        }

        private readonly record struct ItemMeta(string Name);
    }
}
