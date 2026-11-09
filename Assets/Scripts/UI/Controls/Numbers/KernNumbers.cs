#nullable enable

using System;
using System.Globalization;

namespace Kern.UI.Controls
{
    // Сокращение чисел — одно правило на весь интерфейс (visual/kern-ui/js/numbers.js): число пишется целиком с группами
    // разрядов, пока помещается в своё место (max — длина с пробелами групп; кошелёк, ряд кристаллов — 5, «9 999»),
    // иначе K, M, B, T, Q и не длиннее четырёх знаков (12K, 340K, 4.7M, 85B); лишнее отбрасывается, а не округляется.
    // Полное значение показывает подсказка элемента.
    public static class KernNumbers
    {
        private const string Units = "KMBTQ";

        public static string Group(long n)
        {
            string digits = Math.Abs(n).ToString(CultureInfo.InvariantCulture);
            var chars = new System.Text.StringBuilder(digits.Length + digits.Length / 3 + 1);
            if (n < 0)
            {
                chars.Append('-');
            }

            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0)
                {
                    chars.Append(' ');
                }

                chars.Append(digits[i]);
            }

            return chars.ToString();
        }

        // Сокращение; null — число короткое, сокращать нечего
        public static string? Short(long n, int max = 5)
        {
            if (Group(n).Length <= max)
            {
                return null;
            }

            int unit = -1;
            double v = n;
            while (v >= 1e3 && unit < Units.Length - 1)
            {
                v /= 1e3;
                unit++;
            }

            string head = v < 10
                ? (Math.Floor(v * 10) / 10).ToString(CultureInfo.InvariantCulture)
                : Math.Floor(v).ToString(CultureInfo.InvariantCulture);
            return unit < 0 ? head : head + Units[unit];
        }

        // Текст для места длиной max: целиком или сокращённо
        public static string Cut(long n, int max = 5) => Short(n, max) ?? Group(n);
    }
}
