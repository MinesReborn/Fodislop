#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kern.Audio.Core;

namespace Kern.Core;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class AudioBusPathAttribute(string path) : Attribute
{
    public string Path { get; } = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("Bus path must not be empty.", nameof(path))
        : path;
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class AudioBusAttribute(AudioBusType bus) : Attribute
{
    public AudioBusType Bus { get; } = bus;
}

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

    private static readonly Lazy<BusBinding[]> _LazyBuses = new(Build);

    public static IReadOnlyList<BusBinding> Buses => _LazyBuses.Value;

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
