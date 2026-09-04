#nullable enable

using System;

namespace Fodinae.Core;

/// <summary>Подключение к серверу.</summary>
[Serializable]
public sealed class ConnectionSettings
{
    [SettingUnbounded("Тумблер штатной заглушки транспорта.")]
    [SettingConsumer(SettingConsumerTarget.NetworkService, "ConnectionManager transport selector")]
    public bool UseDummyConnection = ProjectRuntimeContracts.ClientConfiguration.DefaultUseDummyConnection;

    [SettingUnbounded("Адрес сервера — строка, а не величина.")]
    [SettingConsumer(SettingConsumerTarget.NetworkService, "ConnectionManager host endpoint")]
    public string ServerHost = ProjectRuntimeContracts.ClientConfiguration.DefaultServerHost;

    [SettingRange(1f, 65535f)]
    [SettingConsumer(SettingConsumerTarget.NetworkService, "ConnectionManager port endpoint")]
    public int ServerPort = ProjectRuntimeContracts.ClientConfiguration.DefaultServerPort;
}
