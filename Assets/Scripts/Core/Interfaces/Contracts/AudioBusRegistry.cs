#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fodinae.Audio.Core;

namespace Fodinae.Core;

/// <summary>
/// Путь шины в FMOD Studio. Объявляется на самом значении перечисления.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class AudioBusPathAttribute(string path) : Attribute
{
    public string Path { get; } = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("Bus path must not be empty.", nameof(path))
        : path;
}

/// <summary>
/// Связь поля громкости в конфиге с шиной. Объявляется на самом поле.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class AudioBusAttribute(AudioBusType bus) : Attribute
{
    public AudioBusType Bus { get; } = bus;
}

/// <summary>
/// Единственное место, где шина связывается с путём FMOD и с полем громкости.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Каждая из шести шин была перечислена руками в шести списках: словарь
/// путей в <c>FmodAudioBackend.MapBuses</c>, шесть вызовов
/// <c>SetBusVolume</c> в <c>AudioSystem.ApplySavedBusVolumes</c>, поле в
/// <c>AudioSettings</c>, создание ползунка в меню, <c>switch</c> на чтение и
/// <c>switch</c> на запись.
///
/// Пропуск в каждом из них проваливался по-разному, и худший был тихим:
/// <c>switch</c> на запись стоял без <c>default</c>, поэтому новая шина давала
/// ползунок, который двигается, ничего не сохраняет и ни на что не жалуется.
/// Пропуск в <c>ApplySavedBusVolumes</c> был не лучше: громкость навсегда
/// оставалась той, что в банке FMOD.
///
/// Теперь шина объявляется дважды и оба раза рядом со смыслом: путь — на
/// значении перечисления, поле громкости — атрибутом над самим полем. Всё
/// остальное перечисляет <see cref="Buses"/>. Пропуск любого из двух объявлений
/// — исключение при первом обращении, с именем шины.
/// </remarks>
public static class AudioBusRegistry
{
    public readonly record struct BusBinding(AudioBusType Bus, string Path, FieldInfo VolumeField)
    {
        public float Read(AudioSettings audio) =>
            VolumeField.GetValue(audio) is float volume
                ? volume
                : throw new InvalidOperationException(
                    $"Audio bus '{Bus}' is bound to {VolumeField.Name}, which is not a float.");

        public void Write(AudioSettings audio, float volume) => VolumeField.SetValue(audio, volume);
    }

    private static readonly Lazy<BusBinding[]> LazyBuses = new(Build);

    public static IReadOnlyList<BusBinding> Buses => LazyBuses.Value;

    public static BusBinding For(AudioBusType bus)
    {
        foreach (BusBinding binding in Buses)
        {
            if (binding.Bus == bus)
            {
                return binding;
            }
        }

        throw new InvalidOperationException($"Audio bus '{bus}' has no binding.");
    }

    private static BusBinding[] Build()
    {
        Dictionary<AudioBusType, FieldInfo> volumeFields = typeof(AudioSettings)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => (field, bus: field.GetCustomAttribute<AudioBusAttribute>()))
            .Where(pair => pair.bus != null)
            .ToDictionary(pair => pair.bus!.Bus, pair => pair.field);

        var bindings = new List<BusBinding>();
        foreach (AudioBusType bus in Enum.GetValues(typeof(AudioBusType)))
        {
            FieldInfo enumMember = typeof(AudioBusType).GetField(bus.ToString())!;
            AudioBusPathAttribute path = enumMember.GetCustomAttribute<AudioBusPathAttribute>() ??
                throw new InvalidOperationException(
                    $"Audio bus '{bus}' has no [AudioBusPath]; it would be mapped to no FMOD bus " +
                    "and stay silent without any error.");
            if (!volumeFields.TryGetValue(bus, out FieldInfo? volumeField))
            {
                throw new InvalidOperationException(
                    $"Audio bus '{bus}' has no volume field in AudioSettings marked [AudioBus({bus})]; " +
                    "its slider would move without ever being saved or applied.");
            }

            bindings.Add(new BusBinding(bus, path.Path, volumeField));
        }

        return bindings.ToArray();
    }
}
