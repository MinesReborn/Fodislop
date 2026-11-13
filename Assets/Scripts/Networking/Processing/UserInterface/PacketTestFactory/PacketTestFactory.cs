#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Contracts.Packets;


namespace Kern.Networking.Processing;

/// <summary>
/// Сборка GUI-пакетов для тестов. Init-сеттеры записей MinesServer.Networking
/// несут modreq IsExternalInit, привязанный к сборке плагина
/// Assets/Plugins/IsExternalInit.System.Runtime.CompilerServices.dll, а
/// тестовые asmdef (TestAssemblies) автоссылок на precompiled-плагины не
/// получают — объектный инициализатор в тесте падает CS0012. Эта сборка
/// плагин видит, поэтому пакеты с init-полями строятся здесь и отдаются
/// тестам через InternalsVisibleTo.
/// </summary>
internal static class PacketTestFactory
{
    internal static TextPacket Text(
        string text,
        string onClickContext,
        params StringPairPacket[] attachedProperties) => new()
    {
        Text = text,
        OnClickContext = onClickContext,
        AttachedProperties = attachedProperties,
    };

    internal static ImagePacket Image(
        string uri,
        byte width,
        byte height,
        string onClickContext,
        params StringPairPacket[] attachedProperties) => new()
    {
        URI = uri,
        Width = width,
        Height = height,
        OnClickContext = onClickContext,
        AttachedProperties = attachedProperties,
    };

    internal static TextBoxPacket TextBox(
        string name,
        string placeholder = "",
        bool isPassword = false,
        params StringPairPacket[] attachedProperties) => new()
    {
        Name = name,
        Placeholder = placeholder,
        IsPassword = isPassword,
        AttachedProperties = attachedProperties,
    };

    internal static DockPanelPacket DockPanel(params IGUIComponentPacket[] children) => new()
    {
        Children = [.. children],
    };

    internal static StringPairPacket Pair(string key, string value) => new(key, value);

    internal static SliderPacket Slider(
        string name,
        float min,
        float max,
        float defaultValue) => new()
    {
        Name = name,
        MinValue = min,
        MaxValue = max,
        DefaultValue = defaultValue,
        Knob = new ImagePacket { URI = string.Empty },
    };

    internal static ScrollViewerPacket Scroll(params IGUIComponentPacket[] children) => new()
    {
        Children = [.. children],
    };
}
