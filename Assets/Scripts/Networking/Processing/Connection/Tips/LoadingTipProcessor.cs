#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;
using VContainer.Unity;

namespace Kern.Networking.Processing;

// Совет для экрана входа: приходит, пока игрок ещё в меню, поэтому подписка — на уровне приложения
public sealed class LoadingTipProcessor : IStartable, IDisposable
{
    private readonly INetworkService _network;
    private readonly ILoadingTip _tip;

    public LoadingTipProcessor(INetworkService network, ILoadingTip tip)
    {
        _network = network;
        _tip = tip;
    }

    public void Start() => _network.Subscribe<LoadingTipPacket>(Process);

    public void Dispose() => _network.Unsubscribe<LoadingTipPacket>(Process);

    public void Process(LoadingTipPacket packet) => _tip.Set(packet.Tip);
}
