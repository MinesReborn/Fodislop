#nullable enable

using System.Collections.Generic;
using Kern.AssetPipeline.Loading;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

// Рост навыков (макет visual/kern-ui: .mini-skills, js/hud.js showMini): круглый значок навыка,
// справа вплотную — полоска опыта цвета навыка во всю высоту значка. Растущий навык виден 5 с после роста,
// готовый к улучшению остаётся: полоска полная цвета тревоги, над значком — слово «up» цвета навыка.
public sealed class PlayerHUDSkillGrid
{
    private const long VisibleMs = 5000;

    private sealed class Item
    {
        public VisualElement Root = null!;
        public VisualElement Fill = null!;
        public IVisualElementScheduledItem? Hide;
    }

    private readonly Dictionary<SkillType, Item> _items = new();
    private VisualElement? _container;

    public void Initialize(VisualElement skillContainer)
    {
        _container = skillContainer;
        _container.Clear();
        _items.Clear();
    }

    public void UpdateSkillProgress(SkillType skill, long current, long max)
    {
        if (_container == null)
        {
            return;
        }

        if (!_items.TryGetValue(skill, out Item? item))
        {
            item = Create(skill);
            _items[skill] = item;
        }

        if (item.Root.parent == null)
        {
            _container.Add(item.Root);
        }

        float progress = max > 0 ? current / (float)max : 0f;
        bool ready = progress >= 1f;
        item.Root.EnableInClassList("is-ready", ready);
        item.Fill.style.height = Length.Percent(ready ? 100f : Mathf.Clamp01(progress) * 100f);

        item.Hide?.Pause();
        item.Hide = null;
        if (!ready)
        {
            item.Hide = item.Root.schedule.Execute(() => item.Root.RemoveFromHierarchy()).StartingIn(VisibleMs);
        }
    }

    public void ClearSchedules()
    {
        foreach (Item item in _items.Values)
        {
            item.Hide?.Pause();
            item.Hide = null;
        }
    }

    private static Item Create(SkillType skill)
    {
        var root = new VisualElement { pickingMode = PickingMode.Position };
        root.AddToClassList("mini-skill");
        // Цвет навыка (--skill-*) — класс по типу; у навыков без своего цвета — общий
        root.AddToClassList("mini-skill--" + skill);

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("mini-skill__ico");
        Texture2D? texture = Resources.Load<Texture2D>($"UI/HUD/Player/Skills/Icons/{skill}");
        if (texture != null)
        {
            RuntimeTextureFactory.ApplySampling(texture, FilterMode.Point, TextureWrapMode.Clamp);
            icon.style.backgroundImage = new StyleBackground(texture);
        }

        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("mini-skill__bar");
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("mini-skill__fill");
        bar.Add(fill);

        var up = new VisualElement { pickingMode = PickingMode.Ignore };
        up.AddToClassList("mini-skill__up");

        root.Add(icon);
        root.Add(bar);
        root.Add(up);
        return new Item { Root = root, Fill = fill };
    }
}
