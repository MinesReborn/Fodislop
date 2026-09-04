#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    /// <summary>
    /// Строитель одного вида пакета. Фабрика выбирает строителя по типу пакета,
    /// поэтому сюда пакет приходит уже нужного вида.
    /// </summary>
    public abstract class PacketUIBuilderBase
    {
        public abstract VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder);
    }

    /// <summary>
    /// База, знающая свой пакет. Приведение делается здесь один раз, а не в
    /// каждом строителе.
    /// </summary>
    /// <remarks>
    /// Раньше каждый строитель начинался с «is not TPacket — верни null», и это
    /// была не проверка, а обряд: тип уже выбран фабрикой, промахнуться нельзя.
    /// Зато null расходился дальше по коду и гасился восклицательными знаками —
    /// то есть невозможное состояние стоило и повторения, и подавленных
    /// предупреждений. Теперь несоответствие типа выражено сигнатурой и
    /// проверяется компилятором, а не возвращается значением.
    /// </remarks>
    public abstract class PacketUIBuilderBase<TPacket> : PacketUIBuilderBase
        where TPacket : IGUIComponentPacket
    {
        public sealed override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            return BuildTyped((TPacket)packet, builder);
        }

        protected abstract VisualElement BuildTyped(TPacket packet, PacketUIBuilder builder);
    }
}
