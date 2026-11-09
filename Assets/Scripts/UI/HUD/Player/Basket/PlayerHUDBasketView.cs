#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.UI.Controls;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

// Кристаллы груза — шесть в ряд (visual/kern-ui css/hud.css .crystals): значок, под ним число цветом кристалла;
// пустой приглушён, ряд не прыгает от состава груза. Число — до «9 999» целиком, дальше сокращение, полное — подсказкой.
public sealed class PlayerHUDBasketView
{
    private const int CountDigits = 5;

    // Порядок ряда — как в макете (CRYSTALS: g r b v w c); класс задаёт цвет (--cry-*)
    private static readonly (CrystalType Type, string Class)[] Order =
    {
        (CrystalType.Green, "crystal--g"),
        (CrystalType.Red, "crystal--r"),
        (CrystalType.Blue, "crystal--b"),
        (CrystalType.Violet, "crystal--v"),
        (CrystalType.White, "crystal--w"),
        (CrystalType.Cyan, "crystal--c"),
    };

    private readonly Texture2D?[] _textures = new Texture2D?[Order.Length];
    private readonly VisualElement[] _items = new VisualElement[Order.Length];
    private readonly VisualElement[] _icons = new VisualElement[Order.Length];
    private readonly Label[] _counts = new Label[Order.Length];
    private VisualElement? _container;

    public void Initialize(VisualElement container)
    {
        _container = container;
        _container.Clear();
        for (int i = 0; i < Order.Length; i++)
        {
            var item = new VisualElement { pickingMode = PickingMode.Position };
            item.AddToClassList("crystal");
            item.AddToClassList(Order[i].Class);
            item.AddToClassList("is-zero");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("crystal__icon");
            var count = new Label("0") { pickingMode = PickingMode.Ignore };
            count.AddToClassList("crystal__count");
            item.Add(icon);
            item.Add(count);
            _container.Add(item);
            _items[i] = item;
            _icons[i] = icon;
            _counts[i] = count;
        }
    }

    public async UniTask LoadCrystalTextures(IAssetLoader assetLoader, CancellationToken cancellationToken)
    {
        for (int i = 0; i < Order.Length; i++)
        {
            string name = Order[i].Type.ToString().ToLowerInvariant();
            try
            {
                _textures[i] = await assetLoader.GetTextureAsync("Visual/Crystals/" + name + ".png", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Загрузчик может уйти раньше HUD при смене сцены: запрос брошен намеренно, это не сбой ассета
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayerHUD] Optional crystal texture '{name}' was skipped: " + exception.Message);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public void ApplyTextures()
    {
        for (int i = 0; i < Order.Length; i++)
        {
            if (_icons[i] != null && _textures[i] != null)
            {
                _icons[i].style.backgroundImage = new StyleBackground(_textures[i]);
            }
        }
    }

    public void Refresh(IPlayerStats stats)
    {
        if (_container == null)
        {
            return;
        }

        long[] contents = stats.BasketContents;
        for (int i = 0; i < Order.Length; i++)
        {
            int index = (int)Order[i].Type;
            long value = index >= 0 && index < contents.Length ? contents[index] : 0;
            _items[i].EnableInClassList("is-zero", value <= 0);
            _counts[i].text = KernNumbers.Cut(value, CountDigits);
            _items[i].tooltip = KernNumbers.Short(value, CountDigits) != null ? KernNumbers.Group(value) : string.Empty;
        }
    }
}
