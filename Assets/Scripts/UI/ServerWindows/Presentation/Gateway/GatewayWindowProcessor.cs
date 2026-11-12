#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Networking.Processing;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Utilities;
using VContainer.Unity;

namespace Kern.UI.ServerWindows;

/// <summary>
/// Диспетчер серверных пакетов в фазе гейтвея и владелец подключения меню:
/// сокет открывается вместе с меню (как на проде) — сервер отвечает окном
/// авторизации до входа в мир и токеном после успешного входа. Подписки
/// живут, пока жива область меню; после перехода в игру пакеты разбирает
/// игровой PacketHandler — поэтому здесь прямая подписка на NetworkService,
/// как у PlayerIdentityProcessor, а не второй полноценный диспетчер.
/// Живёт в UI-сборке рядом с презентером: networking не зависит от UI.
/// </summary>
public sealed class GatewayWindowProcessor : IStartable, IDisposable
{
    private readonly INetworkService _network;
    private readonly IConnectionService _connection;
    private readonly WindowPacketProcessor _windowProcessor;
    private readonly ServerWindowPresenter _presenter;
    private readonly AuthTokenProcessor _authToken;
    private readonly StatusProcessor _status;

    public GatewayWindowProcessor(
        INetworkService network,
        IConnectionService connection,
        WindowPacketProcessor windowProcessor,
        ServerWindowPresenter presenter,
        AuthTokenProcessor authToken,
        StatusProcessor status)
    {
        _network = network;
        _connection = connection;
        _windowProcessor = windowProcessor;
        _presenter = presenter;
        _authToken = authToken;
        _status = status;
    }

    public void Start()
    {
        // Пакеты сессии: окна, токен, статусная процессорка (Pong, устаревший
        // клиент, линии статуса).
        _network.Subscribe<OpenWindowPacket>(_windowProcessor.Process);
        _network.Subscribe<CloseWindowPacket>(_windowProcessor.Process);
        _network.Subscribe<ModalWindowPacket>(_windowProcessor.Process);
        _network.Subscribe<OpenURLPacket>(_windowProcessor.Process);
        _network.Subscribe<AuthTokenPacket>(_authToken.Process);
        _network.Subscribe<OnlinePacket>(_status.Process);
        _network.Subscribe<PingPacket>(_status.Process);
        _network.Subscribe<OutdatedClientPacket>(_status.Process);
        _network.Subscribe<AddStatusLinePacket>(_status.Process);
        _network.Subscribe<ClearStatusLinePacket>(_status.Process);
        _network.Subscribe<ClearStatusPacket>(_status.Process);

        // Подключение открывается при загрузке меню, до всякой попытки входа
        // в мир: сервер отвечает окном авторизации, и мир под удержанием
        // доезжает до игровой сцены после успешного входа.
        _connection.HoldInboundPackets();
        _connection.Connect();

        // Разрыв транспорта гасит окна умершей сессии: окно авторизации,
        // брошенное в меню после отказа соединения, не должно висеть над
        // следующим заходом.
        _connection.OnTransportDisconnected += OnTransportDisconnected;
    }

    public void Dispose()
    {
        _network.Unsubscribe<OpenWindowPacket>(_windowProcessor.Process);
        _network.Unsubscribe<CloseWindowPacket>(_windowProcessor.Process);
        _network.Unsubscribe<ModalWindowPacket>(_windowProcessor.Process);
        _network.Unsubscribe<OpenURLPacket>(_windowProcessor.Process);
        _network.Unsubscribe<AuthTokenPacket>(_authToken.Process);
        _network.Unsubscribe<OnlinePacket>(_status.Process);
        _network.Unsubscribe<PingPacket>(_status.Process);
        _network.Unsubscribe<OutdatedClientPacket>(_status.Process);
        _network.Unsubscribe<AddStatusLinePacket>(_status.Process);
        _network.Unsubscribe<ClearStatusLinePacket>(_status.Process);
        _network.Unsubscribe<ClearStatusPacket>(_status.Process);
        _connection.OnTransportDisconnected -= OnTransportDisconnected;
    }

    private void OnTransportDisconnected() => _presenter.CloseAllWindows();
}
