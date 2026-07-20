namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Logical routing target for every sound played through <see cref="AudioSystem"/>.
    ///
    /// The bus determines:
    /// <list type="bullet">
    ///   <item>Which output mix the sound ends up in (gameplay SFX, UI, music, dialogue, ambient...)</item>
    ///   <item>How the sound participates in ducking / side-chain relationships</item>
    ///   <item>Where the bus sits in the hierarchy — child buses inherit volume/pitch from parent</item>
    /// </list>
    ///
    /// When integrating FMOD or Wwise, map each value to a corresponding bus/vca/group path
    /// inside the IAudioBackend implementation.
    /// </summary>
    public enum AudioBusType
    {
        /// <summary>Master output — every bus ultimately routes here.</summary>
        Master = 0,

        /// <summary>Gameplay sound effects: digging, explosions, footsteps, weapon fire, ambient machinery.</summary>
        Sfx = 10,

        /// <summary>Music / soundtrack.  Long-form, typically stereo, unaffected by spatial positioning.</summary>
        Music = 20,

        /// <summary>Character voice / narration / dialogue system.</summary>
        Voice = 30,

        /// <summary>Environmental ambience: wind, cave reverb hum, lava bubbling.  Usually looped.</summary>
        Ambience = 40,

        /// <summary>User-interface sounds: button clicks, inventory open/close, chat notification chimes.</summary>
        Ui = 50,

        /// <summary>Short-lived narrative stings: quest accepted, achievement unlocked, warning beep.</summary>
        Narrative = 60,
    }

    /// <summary>
    /// Per-layer configuration that determines how a sound instance behaves in the mixer
    /// and how it interacts with spatialisation, priority, and voice-stealing.
    ///
    /// Every <see cref="AudioEvent"/> declares a set of default parameters,
    /// but callers can override individual fields per play via
    /// <c>AudioSystem.Play(myEvent, layer: myCustomLayer)</c>.
    ///
    /// <para>
    /// <b>Why layers matter:</b>
    /// If a bomb detonates 50m away the sound designer may want it quieter than the player's footsteps.
    /// Layers let sound designers declare a priority so that important close-up sounds survive voice-stealing
    /// while distant, low-priority sounds are culled first.
    /// </para>
    /// </summary>
    public readonly struct AudioLayer
    {
        /// <summary>The bus this sound routes through.  Default = <see cref="AudioBusType.Sfx"/>.</summary>
        public AudioBusType Bus { get; init; }

        /// <summary>
        /// Linear volume multiplier applied on top of the bus and master volumes.
        /// <list type="bullet">
        ///   <item>1.0 = play at bus volume</item>
        ///   <item>0.5 = half bus volume</item>
        ///   <item>2.0 = +6 dB boost (use sparingly)</item>
        /// </list>
        /// </summary>
        public float Volume { get; init; }

        /// <summary>
        /// Pitch multiplier.  1.0 = identity.
        /// Useful for randomised variation: play with pitch between 0.95 and 1.05 to avoid repetitive-sounding machines.
        /// </summary>
        public float Pitch { get; init; }

        /// <summary>
        /// Priority [0..255], higher = more important.  Used by <see cref="AudioBus.VoiceStealMode.Quietest"/>.
        /// Defaults:
        /// <list type="bullet">
        ///   <item>128 = normal gameplay SFX</item>
        ///   <item>200 = dialogue / mission-critical audio</item>
        ///   <item>80  = distant ambience / decorative loops</item>
        /// </list>
        /// </summary>
        public int Priority { get; init; }

        /// <summary>
        /// When <c>true</c>, the sound is positioned in the 3D world (stereo panning, distance attenuation).
        /// When <c>false</c>, the sound is 2D (full-volume, no spatialisation).  Default = true for Sfx, false for Ui/Music.
        /// </summary>
        public bool IsSpatial { get; init; }

        /// <summary>
        /// Minimum distance at which the sound starts attenuating.  Only relevant when <see cref="IsSpatial"/> is true.
        /// Default = 1.0 (one world unit = one tile cell).
        /// </summary>
        public float MinDistance { get; init; }

        /// <summary>
        /// Distance at which the sound is fully attenuated.  Sounds beyond this distance are silent.
        /// Default = 20.0 (20 tiles).
        /// </summary>
        public float MaxDistance { get; init; }

        /// <summary>
        /// Convenience factory: gameplay SFX at default priority, spatial, world-scale distances.
        /// </summary>
        public static AudioLayer SfxDefault() => new()
        {
            Bus         = AudioBusType.Sfx,
            Volume      = 1f,
            Pitch       = 1f,
            Priority    = 128,
            IsSpatial   = true,
            MinDistance = 1f,
            MaxDistance = 20f,
        };

        /// <summary>
        /// Convenience factory: non-spatial UI sound.
        /// </summary>
        public static AudioLayer UiDefault() => new()
        {
            Bus       = AudioBusType.Ui,
            Volume    = 1f,
            Pitch     = 1f,
            Priority  = 128,
            IsSpatial = false,
        };

        /// <summary>
        /// Convenience factory: music — always stereo, unconditional.
        /// </summary>
        public static AudioLayer MusicDefault() => new()
        {
            Bus       = AudioBusType.Music,
            Volume    = 1f,
            Pitch     = 1f,
            Priority  = 255,
            IsSpatial = false,
        };

        /// <summary>
        /// Convenience factory: voice / dialogue.
        /// </summary>
        public static AudioLayer VoiceDefault() => new()
        {
            Bus         = AudioBusType.Voice,
            Volume      = 1f,
            Pitch       = 1f,
            Priority    = 200,
            IsSpatial   = true,
            MinDistance = 2f,
            MaxDistance = 15f,
        };
    }
}
