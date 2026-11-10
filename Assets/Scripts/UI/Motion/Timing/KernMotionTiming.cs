#nullable enable

namespace Kern.UI.Motion;

// Времена движения «Квант» — токены макета (visual/kern-ui/css/tokens.css, в игре KernTokens.uss: --step, --count, --flash).
// Код окна читает их отсюда: transition-delay ячейки, досчёт числа и вспышка задаются из C#, а не из листа.
public static class KernMotionTiming
{
    // Шаг между соседними ячейками шкалы (--step)
    public const long StepMs = 20;

    // Досчёт числа до нового значения (--count)
    public const long CountMs = 250;

    // Половина вспышки тревоги (--flash): тусклая фаза, потом ровная
    public const long FlashMs = 120;
}
