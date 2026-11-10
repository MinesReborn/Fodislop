#nullable enable

namespace Kern.UI.Menu;

// Окна главного меню. По макету в меню одно окно — настройки (общее с игрой, Kern.UI.Settings.SettingsWindow);
// его ставит MainMenu. Escape и спуск в мир закрывают открытое окно через этот класс.
public sealed class MenuModalManager
{
    private Kern.UI.Settings.SettingsWindow? _settingsWindow;

    public bool HasActiveModal => _settingsWindow?.IsOpen ?? false;

    // Клавиши сейчас у перехвата в окне настроек (назначение клавиши, калибровка): Escape — его, а не меню
    public bool IsCapturing => _settingsWindow?.IsCapturing ?? false;

    internal void UseSettingsWindow(Kern.UI.Settings.SettingsWindow window) => _settingsWindow = window;

    public void OpenSettings() => _settingsWindow?.Open();

    public void CloseCurrentModal() => _settingsWindow?.Close();
}
