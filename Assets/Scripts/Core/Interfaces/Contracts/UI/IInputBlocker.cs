#nullable enable

namespace Kern.Core.Interfaces;
public interface IInputBlocker
{
    bool IsInputBlocked { get; }

    // Игровой ввод (движение, действия): открытая карта мира блокировкой не
    // считается — карта оверлей, робот идёт, пока она на экране. Блокируют
    // только окна сервера, фокус чата, пауза и инструменты.
    bool IsInputBlockedExcludingMapMode { get; }

    string? TopWindowTag { get; }

    // Идёт перехват новой клавиши на вкладке «Управление» настроек паузы:
    // игровые действия по клавишам (IPlayerInput) на это время молчат,
    // нажатие означает выбор бинда, а не запуск действия.
    bool IsKeyCaptureInProgress { get; }
}
