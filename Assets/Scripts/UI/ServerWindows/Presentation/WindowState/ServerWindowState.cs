#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Fodinae.UI.Binding;
using MinesServer.Networking.Server.Packets.GUI.Components;

namespace Kern.UI.ServerWindows;

    internal sealed class ServerWindowState
    {
        public required string Tag { get; init; }
        public required VisualElement Overlay { get; init; }
        public required VisualElement Frame { get; init; }
        public required IGUIComponentPacket Tree { get; set; }

        /// <summary>Preorder list from the shared window protocol contract.</summary>
        public IReadOnlyList<IGUIComponentPacket> ClickableOrder { get; set; } = Array.Empty<IGUIComponentPacket>();

        /// <summary>
        /// Живые числа окна (SmartFormat+NCalc): именованные инпуты — источники,
        /// лейблы-шаблоны — потребители. Пересоздаётся при каждой отрисовке,
        /// освобождается при закрытии/перерисовке: держатели снятых с иерархии
        /// лейблов не должны переживать окно.
        /// </summary>
        public WindowBinding? Binding { get; set; }
    }
