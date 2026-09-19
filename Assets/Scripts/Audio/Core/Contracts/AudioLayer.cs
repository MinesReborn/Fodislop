#nullable enable

using UnityEngine;

namespace Kern.Audio.Core;
public enum AudioBusType
{
    [Kern.Core.AudioBusPath("bus:/")]
    Master = 0,

    [Kern.Core.AudioBusPath("bus:/sfx")]
    SFX = 10,

    [Kern.Core.AudioBusPath("bus:/music")]
    Music = 20,

    [Kern.Core.AudioBusPath("bus:/voice")]
    Voice = 30,

    [Kern.Core.AudioBusPath("bus:/ambience")]
    Ambience = 40,

    [Kern.Core.AudioBusPath("bus:/ui")]
    UI = 50,
}

[System.Serializable]
public struct AudioLayer
{
    [Tooltip("Шина микшера: SFX, Music, Voice, Ambience, UI.")]
    public AudioBusType Bus;

    [Range(0f, 2f)]
    public float Volume;

    [Range(0.01f, 4f)]
    public float Pitch;

    [Tooltip("Пространственный звук: позиция передаётся в FMOD.")]
    public bool IsSpatial;

    public static AudioLayer SFXDefault() => new()
    {
        Bus = AudioBusType.SFX,
        Volume = 1f,
        Pitch = 1f,
        IsSpatial = true,
    };
    public static AudioLayer MusicDefault() => new()
    {
        Bus = AudioBusType.Music,
        Volume = 1f,
        Pitch = 1f,
        IsSpatial = false,
    };
}
