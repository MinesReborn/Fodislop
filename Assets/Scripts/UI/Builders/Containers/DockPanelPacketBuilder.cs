#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
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
            var element = new VisualElement();
            element.AddToClassList("col");
            element.AddToClassList("no-grow");
            element.Add(Fill(packet.Children, builder));
            return element;
        }

        /// <summary>
        /// Складывает пристыкованных детей вокруг заполнителя, идя от внешнего
        /// края к центру.
        /// </summary>
        private static VisualElement Fill(
            IReadOnlyList<IGUIComponentPacket> children,
            PacketUIBuilder builder)
        {
            IGUIComponentPacket? filler = LastUndocked(children);
            VisualElement current = filler != null
                ? builder.Build(filler)
                : new VisualElement();
            current.AddToClassList("grow");

            for (int i = children.Count - 1; i >= 0; i--)
            {
                IGUIComponentPacket child = children[i];
                if (ReferenceEquals(child, filler))
                {
                    continue;
                }

                current = Wrap(current, builder.Build(child), DockOf(child));
            }

            return current;
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
            // возвращает пустую пару, неотличимую от найденной. Из-за этого
            // ребёнок, у которого есть любые другие свойства, но нет Dock,
            // молча прибивался влево вместо того, чтобы просто встать в столбец.
            string? raw = AttachedProperties.Find(packet, DockKey);
            return raw != null && Enum.TryParse(raw, true, out Dock dock) ? dock : Dock.Left;
        }

        private static IGUIComponentPacket? LastUndocked(IReadOnlyList<IGUIComponentPacket> children)
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (!AttachedProperties.Has(children[i], DockKey))
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
