#nullable enable

namespace Kern.Core;

public static class ClientConfigurationDefaults
{
    // Девелопмент-дефолт: клиент сразу подключается к локальному серверу
    // 127.0.0.1:8090 (порт из appsettings.json сервера, ключ Mines3:Port).
    // Заглушка включается кнопкой оффлайна в гейте входа или пунктом
    // DUMMY SERVER в окне выбора сервера и сохраняется в конфиге.
    // По умолчанию включена: тестовые сборки (в том числе Android) должны
    // играться без сервера. Реальный транспорт выбирается в окне выбора
    // сервера и сохраняется в конфиге.
    public const bool DefaultUseDummyConnection = true;
    public const string DefaultServerHost = "127.0.0.1";
    public const int DefaultServerPort = 8090;
    // HTTP-порт API сервера (эндпоинт /api/status/online для бейджа онлайна
    // в главном меню). Закреплён в appsettings.json сервера (Kestrel:Http).
    public const int DefaultApiPort = 5460;
    public const bool DefaultHDREnabled = false;
}
