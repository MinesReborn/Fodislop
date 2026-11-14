#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public enum Dock
{
    Left,
    Top,
    Right,
    Bottom,
}

public class DockPanelPacketBuilder : PacketUIBuilderBase<DockPanelPacket>
{
    private const string DockKey = "DockPanel.Dock";

    protected override VisualElement BuildTyped(DockPanelPacket packet, PacketUIBuilder builder)
    {
        // Раскладка — утилитарным классом, а не инлайном: инлайн выигрывает
        // у любого правила USS, и серверное окно выпадает из-под темы и тира.
        // Классы печатает генератор токенов в TokenUtilities.uss.
        VisualElement element = Fill(packet.Children, builder);
        element.AddToClassList("no-grow");
        return element;
    }

    private static VisualElement Fill(
        IReadOnlyList<IGUIComponentPacket> children,
        PacketUIBuilder builder)
    {
        // Одинаковая стыковка сохраняет порядок детей без дополнительных обёрток.
        // ai-center — вертикальное центрирование ленты, как align-items:center
        // у флекс-строк макета: иконка и подпись составной кнопки, чекбокс и
        // его подпись сидят по центру строки, а не у верхней грани.
        if (AllDockedLeft(children))
        {
            var strip = new VisualElement();
            strip.AddToClassList("row");
            strip.AddToClassList("jc-center");
            strip.AddToClassList("ai-center");
            foreach (IGUIComponentPacket child in children)
            {
                VisualElement built = builder.Build(child);
                built.AddToClassList("no-shrink");
                strip.Add(built);
            }

            return strip;
        }

        // Панель без стыковки (или со всеми наверх) — обычная колонка: дети
        // складываются сверху вниз, как секции страницы.
        if (AllStacked(children))
        {
            var column = new VisualElement();
            column.AddToClassList("col");
            foreach (IGUIComponentPacket child in children)
            {
                column.Add(builder.Build(child));
            }

            return column;
        }

        // Смешанная раскладка пакуется обёртками от растущего ядра; филлера
        // без явного Dock больше нет — сервер присылает стыковки явно.
        VisualElement current = new VisualElement();
        current.AddToClassList("grow");

        for (int i = children.Count - 1; i >= 0; i--)
        {
            current = Wrap(current, builder.Build(children[i]), DockOf(children[i]));
        }

        return current;
    }

    private static bool AllDockedLeft(IReadOnlyList<IGUIComponentPacket> children)
    {
        foreach (IGUIComponentPacket child in children)
        {
            if (DockOf(child) != Dock.Left)
            {
                return false;
            }
        }

        return children.Count > 0;
    }

    private static bool AllStacked(IReadOnlyList<IGUIComponentPacket> children)
    {
        if (children.Count == 0)
        {
            return false;
        }

        foreach (IGUIComponentPacket child in children)
        {
            if (DockOf(child) != Dock.Top)
            {
                return false;
            }
        }

        return true;
    }

    private static VisualElement Wrap(VisualElement current, VisualElement child, Dock dock)
    {
        // alignSelf: Auto здесь не задаётся — это и есть значение по
        // умолчанию у только что созданного элемента.
        var wrapper = new VisualElement();
        wrapper.AddToClassList("grow");
        wrapper.AddToClassList(dock is Dock.Top or Dock.Bottom ? "col" : "row");
        if (dock is Dock.Left or Dock.Right)
        {
            wrapper.AddToClassList("ai-center");
        }

        child.AddToClassList("no-shrink");
        bool childFirst = dock is Dock.Top or Dock.Left;
        wrapper.Add(childFirst ? child : current);
        wrapper.Add(childFirst ? current : child);
        return wrapper;
    }

    private static Dock DockOf(IGUIComponentPacket packet)
    {
        // Отсутствие ключа читается через Find, а не через FirstOrDefault:
        // присоединённое свойство — структура, и «первое или умолчание»
        // возвращает пустую пару, неотличимую от найденной. Ребёнок без
        // Dock складывается в столбец — как при явном Top.
        string? raw = AttachedProperties.Find(packet, DockKey);
        if (raw == null)
        {
            return Dock.Top;
        }

        if (!Enum.TryParse(raw, true, out Dock dock) || !Enum.IsDefined(typeof(Dock), dock))
        {
            throw new InvalidOperationException($"Invalid {DockKey}='{raw}'.");
        }

        return dock;
    }
}
