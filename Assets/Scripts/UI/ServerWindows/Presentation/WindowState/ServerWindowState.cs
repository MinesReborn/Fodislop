#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using MinesServer.Networking.Server.Packets.GUI.Components;

namespace Kern.UI.ServerWindows;

    internal sealed class ServerWindowState
    {
        public required string Tag { get; init; }
        public required VisualElement Overlay { get; init; }
        public required VisualElement Frame { get; init; }
        public required WindowBinding Binding { get; init; }
        public required IGUIComponentPacket Tree { get; set; }

        /// <summary>Preorder list from the shared window protocol contract.</summary>
        public IReadOnlyList<IGUIComponentPacket> ClickableOrder { get; set; } = Array.Empty<IGUIComponentPacket>();
    }
