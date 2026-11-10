#nullable enable

using MinesServer.Networking.Connection;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Audio;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Programmator;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Server.Packets.Programmator;
using MinesServer.Networking.Contracts;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;
using VContainer;

namespace Kern.Networking.Dummy;

internal static class DummyHelloResponder
{
    internal static void Handle(ClientHelloPacket clientHello,
        DummyScenarioController scenario, DummyConnectionSession session,
        DummyAuthSession authSession, IAsyncOperationSupervisor operations,
        Action<ServerPacket> sendPacket, Func<CancellationToken, UniTask> initializeWorldAsync)
    {
                if (!scenario.TryBeginHello(session.LifecycleVersion))
                {
                    return;
                }

                if (scenario.RejectsAuthentication)
                {
                    sendPacket.Invoke(DummyWindowBuilder.BuildAuthWindow());
                    return;
                }

                string receivedToken = clientHello.AuthToken;
                // Офлайн-сервер пермиссивный и окна авторизации не вызывает:
                // Знакомые токены принимаются как есть, для пустого или
                // незнакомого dummy-сервер сам выдаёт новый
                // и запоминает его, чтобы авто-вход работал и дальше.
                string resolvedToken = authSession.ResolveToken(receivedToken);

                if (clientHello.ClientVersion < global::Kern.Core.ClientProtocolVersion.ClientVersion)
                {
                    // Причина передаётся ключом словаря: StatusProcessor и
                    // ReconnectUI резолвят его через HasKey, если он попадёт
                    // на экран.
                    sendPacket.Invoke(new ServerPacket(new OutdatedClientPacket(
                        2, "Mines 3", "network.error.old_client",
                        "https://minesgame.ru/download", string.Empty)));
                    return;
                }

                sendPacket.Invoke(new ServerPacket(new AuthTokenPacket(resolvedToken)));

                if (scenario.StallsWorldInitialization)
                {
                    return;
                }

                operations.Run("dummy_world_init", initializeWorldAsync);
    }
}
