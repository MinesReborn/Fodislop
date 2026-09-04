#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    /// <summary>
    /// Выпадающий список любого протокольного типа значений.
    /// </summary>
    /// <remarks>
    /// Целочисленный и строковый списки были двумя файлами с одним и тем же
    /// алгоритмом: собрать подписи, отвергнуть пустой набор, отвергнуть значение
    /// по умолчанию вне набора, включить или выключить. Различие между ними —
    /// одна строка: как значение превращается в подпись. Протокол сам говорит,
    /// что это один компонент (DropdownComponentPacket&lt;TValue&gt;), и здесь
    /// он таким и остаётся.
    /// </remarks>
    public abstract class DropdownPacketBuilder<TPacket, TValue> : PacketUIBuilderBase<TPacket>
        where TPacket : DropdownComponentPacket<TValue>
        where TValue : notnull
    {
        protected virtual string Caption(TValue value) => value.ToString()!;

        protected override VisualElement BuildTyped(TPacket packet, PacketUIBuilder builder)
        {
            var options = new List<string>(packet.Values.Length);
            foreach (TValue value in packet.Values)
            {
                options.Add(Caption(value));
            }

            if (options.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Dropdown '{packet.Name}' has no options.");
            }

            string defaultValue = Caption(packet.DefaultValue);
            if (!options.Contains(defaultValue))
            {
                throw new InvalidOperationException(
                    $"Dropdown '{packet.Name}' default '{defaultValue}' " +
                    "is not present in its options.");
            }

            var dropdown = new DropdownField(options, 0)
            {
                value = defaultValue,
            };
            dropdown.SetEnabled(packet.IsEnabled);
            return dropdown;
        }
    }

    public class IntDropdownPacketBuilder : DropdownPacketBuilder<IntDropdownPacket, int>
    {
    }

    public class StringDropdownPacketBuilder : DropdownPacketBuilder<StringDropdownPacket, string>
    {
        protected override string Caption(string value) => value;
    }
}
