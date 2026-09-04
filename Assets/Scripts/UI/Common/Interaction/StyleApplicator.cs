#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    /// <summary>
    /// GUIStylePacket применяется буквально: что сервер прислал, то и рисуется.
    /// </summary>
    /// <remarks>
    /// Здесь была попытка «интерпретировать» протокол: цвет из пакета искал
    /// ближайший токен палитры, и элемент получал класс вместо инлайна — ради
    /// того, чтобы серверные окна слушались темы и тира. Цена была в том, что
    /// клиент переставал слушаться сервера: цвет #1a2b3c становился похожим
    /// токеном, а не собой.
    ///
    /// Это отменено. Протокол есть, его надо соблюдать — не больше и не
    /// меньше. Окно, которому сервер назначил цвет, показывает назначенный
    /// цвет; примагничивание было лишним слоем поверх готового договора.
    /// Вместе с ним ушли таблица палитры для кода и 278 утилитарных классов,
    /// существовавших только ради неё.
    ///
    /// Дизайн-система при этом не потеряна: она отвечает за то, о чём протокол
    /// молчит. Style у компонента необязательный (<c>GUIStylePacket?</c>), и
    /// когда его нет, вид целиком выбирает клиент — своими классами, своей
    /// темой, своим тиром. Договор такой: сказано — исполняем, не сказано —
    /// решаем сами.
    ///
    /// Нулевое значение читается как «не задано»: отличить его от осознанного
    /// нуля протокол не позволяет — у Color и Margins нет признака заданности.
    /// Поэтому прозрачный фон, нулевая рамка и нулевой отступ не пишутся, и
    /// собственный вид клиента под ними сохраняется.
    /// </remarks>
    public static class StyleApplicator
    {
        public static void ApplyStyles(VisualElement element, IGUIComponentPacket packet)
        {
            if (packet.Style is null)
            {
                return;
            }

            var style = packet.Style.Value;

            if (style.Background.A > 0)
            {
                element.style.backgroundColor = ConvertColor(style.Background);
            }

            if (style.BorderWidth > 0)
            {
                Color border = ConvertColor(style.Border);
                element.style.borderTopColor = border;
                element.style.borderBottomColor = border;
                element.style.borderLeftColor = border;
                element.style.borderRightColor = border;

                element.style.borderTopWidth = style.BorderWidth;
                element.style.borderBottomWidth = style.BorderWidth;
                element.style.borderLeftWidth = style.BorderWidth;
                element.style.borderRightWidth = style.BorderWidth;
            }

            ApplyMargins(style.Margin,
                left => element.style.marginLeft = left,
                top => element.style.marginTop = top,
                right => element.style.marginRight = right,
                bottom => element.style.marginBottom = bottom);

            ApplyMargins(style.Padding,
                left => element.style.paddingLeft = left,
                top => element.style.paddingTop = top,
                right => element.style.paddingRight = right,
                bottom => element.style.paddingBottom = bottom);
        }

        private static void ApplyMargins(
            Margins margins,
            System.Action<int> left,
            System.Action<int> top,
            System.Action<int> right,
            System.Action<int> bottom)
        {
            if (margins.Left > 0) left(margins.Left);
            if (margins.Top > 0) top(margins.Top);
            if (margins.Right > 0) right(margins.Right);
            if (margins.Bottom > 0) bottom(margins.Bottom);
        }

        public static Color ConvertColor(System.Drawing.Color color)
        {
            return new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }
    }
}
